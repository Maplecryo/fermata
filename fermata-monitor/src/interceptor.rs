//! Kill-and-relaunch interceptor. Terminates a restricted process, notifies the
//! UI over IPC, and relaunches it if the user chooses to continue.
//! Does not poll for processes — that is `monitor.rs`.

use std::{
    collections::{HashMap, HashSet},
    sync::{Arc, atomic::{AtomicBool, Ordering}},
    time::{Duration, Instant},
};

use anyhow::Result;

use crate::ipc::{self, InterceptEvent};

// ── Constants ────────────────────────────────────────────────────────────────

/// How long to ignore an exe after relaunching it. Must be long enough to
/// cover app startup time — most apps appear in the process list within 10s.
const RELAUNCH_COOLDOWN: Duration = Duration::from_secs(15);

/// How long to ignore an exe after killing it (before the user responds).
const KILL_COOLDOWN: Duration = Duration::from_secs(3);

// ── Types ────────────────────────────────────────────────────────────────────

/// Captured details about a running process, ready for relaunch.
#[derive(Debug, Clone)]
pub struct ProcessInfo {
    pub pid: u32,
    pub exe_name: String, // lowercase filename only, e.g. "steam" or "steam.exe"
    pub exe_path: String, // full path
    pub args: String,
    pub working_dir: String,
}

/// Tracks per-exe cooldowns and session-level allow decisions.
pub struct Interceptor {
    cooldowns: HashMap<String, Instant>,
    /// Apps the user explicitly chose to continue this session.
    /// Cleared only when the monitor restarts — not time-limited.
    session_allowed: HashSet<String>,
}

impl Interceptor {
    pub fn new() -> Self {
        Self {
            cooldowns: HashMap::new(),
            session_allowed: HashSet::new(),
        }
    }

    /// Returns `true` if `exe_name` should be skipped — either because the
    /// user already allowed it this session, or it's within the short
    /// post-kill cooldown window.
    pub fn is_cooling_down(&self, exe_name: &str) -> bool {
        let key = exe_name.to_lowercase();
        if self.session_allowed.contains(&key) {
            return true;
        }
        self.cooldowns
            .get(&key)
            .map(|t| t.elapsed() < RELAUNCH_COOLDOWN)
            .unwrap_or(false)
    }

    /// Called each poll cycle with the set of exe names currently running.
    /// Removes any whitelisted app that is no longer running, so the next
    /// time the user opens it they are intercepted again.
    pub fn expire_whitelist(&mut self, running_exe_names: &HashSet<String>) {
        self.session_allowed.retain(|allowed| {
            let still_running = running_exe_names.contains(allowed);
            if !still_running {
                log::info!("{allowed} closed — removed from session whitelist");
            }
            still_running
        });
    }

    /// Intercept flow: kill → IPC → relaunch (if "continue").
    ///
    /// While blocked waiting for the user to respond, a background thread
    /// continuously kills any new instances of the same app that open —
    /// preventing the user from bypassing the block by relaunching quickly.
    pub fn intercept(&mut self, info: &ProcessInfo) -> Result<()> {
        let key = info.exe_name.to_lowercase();

        kill(info.pid)?;
        log::info!("killed {} (pid {})", info.exe_name, info.pid);

        // Short cooldown so the monitor loop doesn't re-detect this PID
        // during the brief window before the process fully exits.
        self.cooldowns.insert(key.clone(), Instant::now() - RELAUNCH_COOLDOWN + KILL_COOLDOWN);

        let event = InterceptEvent::new(
            display_name(&info.exe_name),
            &info.exe_path,
            &info.args,
            &info.working_dir,
        );

        // Spawn a thread that kills any new instances launched while we are
        // blocked waiting for the user to respond in the UI. Without this,
        // a second launch of the same app would run freely because the
        // monitor poll loop is frozen inside this function.
        let waiting = Arc::new(AtomicBool::new(true));
        let waiting_clone = Arc::clone(&waiting);
        let exe_name_clone = info.exe_name.clone();
        let killer = std::thread::spawn(move || {
            while waiting_clone.load(Ordering::Relaxed) {
                std::thread::sleep(Duration::from_millis(400));
                if !waiting_clone.load(Ordering::Relaxed) {
                    break;
                }
                if let Ok(procs) = crate::monitor::snapshot_processes() {
                    for p in procs {
                        if p.exe_name == exe_name_clone {
                            log::info!(
                                "killing duplicate instance of {} (pid {}) launched during block",
                                exe_name_clone, p.pid
                            );
                            kill(p.pid).ok();
                        }
                    }
                }
            }
        });

        let action = match ipc::send_event(&event) {
            Ok(a) => {
                log::info!("IPC response for {}: '{a}'", info.exe_name);
                a
            }
            Err(e) => {
                log::error!("IPC error for {}, treating as cancel: {e}", info.exe_name);
                "cancel".to_string()
            }
        };

        // Stop the killer thread before relaunching so it doesn't immediately
        // kill the freshly relaunched process.
        waiting.store(false, Ordering::Relaxed);
        let _ = killer.join();

        if action == "continue" {
            self.session_allowed.insert(key.clone());
            log::info!("{} added to session whitelist", info.exe_name);
            match relaunch(info) {
                Ok(()) => log::info!("relaunch command sent for {}", info.exe_name),
                Err(e) => log::error!("relaunch failed: {e}"),
            }
        } else {
            log::info!("action was '{action}', skipping relaunch for {}", info.exe_name);
        }

        Ok(())
    }
}

// ── Platform: Windows ────────────────────────────────────────────────────────

#[cfg(windows)]
fn kill(pid: u32) -> Result<()> {
    use windows::Win32::{
        Foundation::CloseHandle,
        System::Threading::{OpenProcess, TerminateProcess, PROCESS_TERMINATE},
    };
    // SAFETY: pid is a valid process id from the snapshot. CloseHandle is always
    // called to prevent a handle leak.
    unsafe {
        let h = OpenProcess(PROCESS_TERMINATE, false, pid)?;
        let result = TerminateProcess(h, 1);
        CloseHandle(h)?;
        result?;
    }
    Ok(())
}

#[cfg(windows)]
fn relaunch(info: &ProcessInfo) -> Result<()> {
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use windows::Win32::System::Threading::{
        CreateProcessW, PROCESS_INFORMATION, STARTUPINFOW,
    };

    // Edge case 7: verify the exe still exists.
    if !std::path::Path::new(&info.exe_path).exists() {
        log::warn!("exe no longer exists, skipping relaunch: {}", info.exe_path);
        return Ok(());
    }

    let mut cmd_line = format!(r#""{}""#, info.exe_path);
    if !info.args.is_empty() {
        cmd_line.push(' ');
        cmd_line.push_str(&info.args);
    }
    let mut cmd_wide: Vec<u16> = OsStr::new(&cmd_line)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    let wd_wide: Vec<u16> = OsStr::new(&info.working_dir)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();

    let mut si = STARTUPINFOW {
        cb: std::mem::size_of::<STARTUPINFOW>() as u32,
        ..Default::default()
    };
    let mut pi = PROCESS_INFORMATION::default();

    // SAFETY: all wide string slices are null-terminated; structs are correctly
    // sized and zeroed above.
    unsafe {
        CreateProcessW(
            None,
            windows::core::PWSTR(cmd_wide.as_mut_ptr()),
            None,
            None,
            false,
            Default::default(),
            None,
            windows::core::PCWSTR(wd_wide.as_ptr()),
            &mut si,
            &mut pi,
        )?;
        windows::Win32::Foundation::CloseHandle(pi.hProcess)?;
        windows::Win32::Foundation::CloseHandle(pi.hThread)?;
    }
    log::info!("relaunched {}", info.exe_name);
    Ok(())
}

// ── Platform: macOS ──────────────────────────────────────────────────────────

#[cfg(target_os = "macos")]
fn kill(pid: u32) -> Result<()> {
    // SAFETY: SIGTERM is a valid signal; pid is from the live process snapshot.
    let ret = unsafe { libc::kill(pid as libc::pid_t, libc::SIGTERM) };
    if ret != 0 {
        anyhow::bail!(
            "kill({pid}) failed: {}",
            std::io::Error::last_os_error()
        );
    }
    Ok(())
}

#[cfg(target_os = "macos")]
fn relaunch(info: &ProcessInfo) -> Result<()> {
    // Edge case 7: verify the path still exists.
    if !std::path::Path::new(&info.exe_path).exists() {
        log::warn!("path no longer exists, skipping relaunch: {}", info.exe_path);
        return Ok(());
    }

    // On macOS, apps are .app bundles — use `open` so the system handles
    // bundle vs raw binary correctly. For plain binaries, fall back to direct
    // exec via std::process::Command.
    let status = if info.exe_path.contains(".app/") || info.exe_path.ends_with(".app") {
        // Walk up to the .app bundle root so `open` gets the bundle, not the binary.
        let bundle = bundle_root(&info.exe_path);
        std::process::Command::new("open")
            .arg(&bundle)
            .args(if info.args.is_empty() { vec![] } else { vec!["--args", &info.args] })
            .status()?
    } else {
        let mut cmd = std::process::Command::new(&info.exe_path);
        if !info.args.is_empty() {
            cmd.arg(&info.args);
        }
        cmd.status()?
    };

    if !status.success() {
        log::warn!("relaunch of {} exited with {status}", info.exe_name);
    } else {
        log::info!("relaunched {}", info.exe_name);
    }
    Ok(())
}

/// Walks up the path to find the enclosing `.app` bundle root.
/// e.g. `/Applications/Steam.app/Contents/MacOS/steam` → `/Applications/Steam.app`
#[cfg(target_os = "macos")]
fn bundle_root(exe_path: &str) -> String {
    let path = std::path::Path::new(exe_path);
    let mut current = path;
    loop {
        if current
            .extension()
            .map(|e| e == "app")
            .unwrap_or(false)
        {
            return current.to_string_lossy().into_owned();
        }
        match current.parent() {
            Some(p) if p != current => current = p,
            _ => return exe_path.to_string(),
        }
    }
}

#[cfg(not(any(windows, target_os = "macos")))]
fn kill(_pid: u32) -> Result<()> {
    anyhow::bail!("kill is not supported on this platform")
}

#[cfg(not(any(windows, target_os = "macos")))]
fn relaunch(_info: &ProcessInfo) -> Result<()> {
    anyhow::bail!("relaunch is not supported on this platform")
}

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Strips the `.exe` suffix (Windows) or `.app` suffix (macOS) and capitalises
/// the first letter for human-readable display in the reflection window.
fn display_name(exe_name: &str) -> String {
    let base = exe_name
        .trim_end_matches(".exe")
        .trim_end_matches(".EXE")
        .trim_end_matches(".app");
    let mut chars = base.chars();
    match chars.next() {
        None => String::new(),
        Some(c) => c.to_uppercase().collect::<String>() + chars.as_str(),
    }
}

// ── Tests ────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cooldown_prevents_immediate_reintercept() {
        let mut ic = Interceptor::new();
        assert!(!ic.is_cooling_down("steam.exe"));
        ic.cooldowns.insert("steam.exe".into(), Instant::now());
        assert!(ic.is_cooling_down("steam.exe"));
        assert!(ic.is_cooling_down("STEAM.EXE")); // case-insensitive
    }

    #[test]
    fn cooldown_expires() {
        let mut ic = Interceptor::new();
        ic.cooldowns.insert(
            "steam.exe".into(),
            Instant::now() - RELAUNCH_COOLDOWN - Duration::from_millis(1),
        );
        assert!(!ic.is_cooling_down("steam.exe"));
    }

    #[test]
    fn display_name_strips_exe() {
        assert_eq!(display_name("steam.exe"), "Steam");
        assert_eq!(display_name("discord.EXE"), "Discord");
        assert_eq!(display_name("notepad.exe"), "Notepad");
    }

    #[test]
    fn display_name_strips_app() {
        assert_eq!(display_name("Steam.app"), "Steam");
        assert_eq!(display_name("Discord.app"), "Discord");
    }

    #[cfg(target_os = "macos")]
    #[test]
    fn bundle_root_finds_app() {
        assert_eq!(
            bundle_root("/Applications/Steam.app/Contents/MacOS/steam"),
            "/Applications/Steam.app"
        );
        assert_eq!(
            bundle_root("/usr/local/bin/sometool"),
            "/usr/local/bin/sometool"
        );
    }
}

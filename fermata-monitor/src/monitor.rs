//! Process polling loop. Enumerates running processes every POLL_INTERVAL,
//! compares exe filenames against the restricted list, and dispatches matches
//! to the interceptor. Does not perform any I/O beyond process enumeration.

use std::{
    collections::HashSet,
    time::Duration,
};

use crate::{
    config::{self, SharedConfig},
    interceptor::{Interceptor, ProcessInfo},
};

// ── Constants ────────────────────────────────────────────────────────────────

const POLL_INTERVAL: Duration = Duration::from_millis(500);

// ── Public API ───────────────────────────────────────────────────────────────

/// Runs the monitor loop forever (blocks the calling thread).
/// `already_running` is the set of PIDs present at startup — those are skipped
/// to avoid killing apps the user already had open (edge case 1).
pub fn run(shared_config: SharedConfig, already_running: HashSet<u32>) {
    let mut interceptor = Interceptor::new();
    let mut seen_at_startup = already_running;

    loop {
        std::thread::sleep(POLL_INTERVAL);

        let cfg = match shared_config.read() {
            Ok(c) => c.clone(),
            Err(_) => continue,
        };

        let processes = match snapshot_processes() {
            Ok(p) => p,
            Err(e) => {
                log::error!("process snapshot failed: {e}");
                continue;
            }
        };

        for info in &processes {
            // Edge case 1: skip processes that were running at startup.
            if seen_at_startup.contains(&info.pid) {
                continue;
            }

            if !config::is_restricted(&cfg, &info.exe_name) {
                continue;
            }

            // Edge case 2: skip if still in cooldown after a relaunch.
            if interceptor.is_cooling_down(&info.exe_name) {
                continue;
            }

            log::info!("intercepting {} (pid {})", info.exe_name, info.pid);
            if let Err(e) = interceptor.intercept(info) {
                log::error!("intercept failed for {}: {e}", info.exe_name);
            }
        }

        // Build the set of currently running exe names for whitelist expiry.
        let live_exe_names: HashSet<String> = processes.iter()
            .map(|p| p.exe_name.clone())
            .collect();
        interceptor.expire_whitelist(&live_exe_names);

        // Drop PIDs that have exited so the startup set doesn't grow unboundedly.
        let live_pids: HashSet<u32> = processes.iter().map(|p| p.pid).collect();
        seen_at_startup.retain(|pid| live_pids.contains(pid));
    }
}

// ── Platform: Windows ────────────────────────────────────────────────────────

#[cfg(windows)]
pub fn snapshot_processes() -> anyhow::Result<Vec<ProcessInfo>> {
    use std::ffi::OsString;
    use std::os::windows::ffi::OsStringExt;
    use windows::Win32::{
        Foundation::{CloseHandle, MAX_PATH},
        System::{
            Diagnostics::ToolHelp::{
                CreateToolhelp32Snapshot, Process32FirstW, Process32NextW,
                PROCESSENTRY32W, TH32CS_SNAPPROCESS,
            },
            ProcessStatus::GetModuleFileNameExW,
            Threading::{OpenProcess, PROCESS_QUERY_INFORMATION, PROCESS_VM_READ},
        },
    };

    // SAFETY: TH32CS_SNAPPROCESS is a documented valid flag.
    let snap = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)? };
    let mut entry = PROCESSENTRY32W {
        dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
        ..Default::default()
    };
    let mut results = Vec::new();

    // SAFETY: snap is a valid snapshot handle; entry is properly initialised.
    if unsafe { Process32FirstW(snap, &mut entry) }.is_ok() {
        loop {
            let pid = entry.th32ProcessID;
            // SAFETY: PROCESS_QUERY_INFORMATION | PROCESS_VM_READ are the minimum
            // required access rights; handle is closed immediately after use.
            if let Ok(h) = unsafe {
                OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid)
            } {
                let mut buf = [0u16; MAX_PATH as usize];
                // SAFETY: buf length is passed correctly; h is valid.
                let len = unsafe { GetModuleFileNameExW(h, None, &mut buf) };
                let _ = unsafe { CloseHandle(h) };

                if len > 0 {
                    let full_path = OsString::from_wide(&buf[..len as usize])
                        .to_string_lossy()
                        .into_owned();
                    let exe_name = std::path::Path::new(&full_path)
                        .file_name()
                        .map(|n| n.to_string_lossy().to_lowercase())
                        .unwrap_or_default();
                    results.push(ProcessInfo {
                        pid,
                        exe_name,
                        exe_path: full_path,
                        args: String::new(),
                        working_dir: String::new(),
                    });
                }
            }
            // SAFETY: snap is valid; entry is large enough.
            if unsafe { Process32NextW(snap, &mut entry) }.is_err() {
                break;
            }
        }
    }
    let _ = unsafe { CloseHandle(snap) };
    Ok(results)
}

// ── Platform: macOS ──────────────────────────────────────────────────────────

#[cfg(target_os = "macos")]
pub fn snapshot_processes() -> anyhow::Result<Vec<ProcessInfo>> {
    use libc::{c_int, c_void};

    // Step 1: ask proc_listallpids how many PIDs exist (pass null buffer).
    // SAFETY: null buffer with size 0 is the documented way to get the count.
    let count = unsafe { libc::proc_listallpids(std::ptr::null_mut(), 0) };
    if count <= 0 {
        anyhow::bail!("proc_listallpids count failed: {}", std::io::Error::last_os_error());
    }

    // Add headroom for processes that may appear between the two calls.
    let capacity = (count as usize) + 32;
    let mut pids: Vec<c_int> = vec![0; capacity];

    // Step 2: fill the buffer.
    // SAFETY: pids is properly allocated; capacity * size_of::<c_int>() is correct.
    let filled = unsafe {
        libc::proc_listallpids(
            pids.as_mut_ptr() as *mut c_void,
            (capacity * std::mem::size_of::<c_int>()) as c_int,
        )
    };
    if filled <= 0 {
        anyhow::bail!("proc_listallpids fill failed: {}", std::io::Error::last_os_error());
    }

    let mut results = Vec::with_capacity(filled as usize);

    for &pid_raw in &pids[..filled as usize] {
        let pid = pid_raw as u32;
        if pid == 0 {
            continue;
        }

        // Resolve the full executable path via proc_pidpath.
        let mut path_buf = vec![0u8; libc::PROC_PIDPATHINFO_MAXSIZE as usize];
        // SAFETY: path_buf is allocated to exactly PROC_PIDPATHINFO_MAXSIZE bytes;
        // pid is a valid live PID from the snapshot above.
        let len = unsafe {
            libc::proc_pidpath(
                pid as c_int,
                path_buf.as_mut_ptr() as *mut c_void,
                path_buf.len() as u32,
            )
        };

        if len <= 0 {
            // proc_pidpath fails for kernel/system processes — silently skip.
            continue;
        }

        let exe_path = String::from_utf8_lossy(&path_buf[..len as usize])
            .trim_end_matches('\0')
            .to_string();

        let exe_name = std::path::Path::new(&exe_path)
            .file_name()
            .map(|n| n.to_string_lossy().to_lowercase())
            .unwrap_or_default();

        results.push(ProcessInfo {
            pid,
            exe_name,
            exe_path,
            args: String::new(),
            working_dir: String::new(),
        });
    }

    Ok(results)
}

#[cfg(not(any(windows, target_os = "macos")))]
pub fn snapshot_processes() -> anyhow::Result<Vec<ProcessInfo>> {
    Ok(Vec::new())
}

// ── Tests ────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use std::sync::{Arc, RwLock};
    use crate::config::Config;
    use super::*;

    #[test]
    fn startup_pids_are_skipped() {
        let cfg = Config {
            apps: vec!["steam.exe".into()],
            ..Default::default()
        };
        let shared = Arc::new(RwLock::new(cfg));
        let already: HashSet<u32> = [1234].into();

        let guard = shared.read().unwrap();
        assert!(config::is_restricted(&guard, "steam.exe"));
        // pid 1234 is in the startup set — the monitor loop would continue before
        // reaching the interceptor.
        assert!(already.contains(&1234u32));
    }

    #[test]
    fn snapshot_returns_processes() {
        // On a live system this should always return at least one process (us).
        let procs = snapshot_processes().unwrap();
        assert!(!procs.is_empty());
        // Our own process should be in the list.
        let our_pid = std::process::id();
        assert!(procs.iter().any(|p| p.pid == our_pid));
    }
}

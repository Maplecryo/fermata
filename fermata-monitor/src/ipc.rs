//! IPC client. On Windows: connects to `\\.\pipe\FermataIPC` (named pipe).
//! On macOS: connects to a Unix domain socket at `$TMPDIR/fermata.sock`.
//! Sends an `InterceptEvent` JSON line and blocks for an `InterceptResponse`.
//! Does not relaunch processes — that is `interceptor.rs`.

use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::io::{BufRead, BufReader};
use std::time::{Duration, Instant};

// ── Constants ────────────────────────────────────────────────────────────────

#[cfg(windows)]
pub const PIPE_NAME: &str = r"\\.\pipe\FermataIPC";

#[cfg(target_os = "macos")]
pub fn socket_path() -> std::path::PathBuf {
    std::env::temp_dir().join("fermata.sock")
}

const CONNECT_RETRY_INTERVAL: Duration = Duration::from_millis(500);
const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);

// ── Message types ─────────────────────────────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct InterceptEvent {
    #[serde(rename = "type")]
    pub msg_type: String,
    pub app_name: String,
    pub exe_path: String,
    pub args: String,
    pub working_dir: String,
    pub timestamp: String,
}

impl InterceptEvent {
    pub fn new(
        app_name: impl Into<String>,
        exe_path: impl Into<String>,
        args: impl Into<String>,
        working_dir: impl Into<String>,
    ) -> Self {
        Self {
            msg_type: "InterceptEvent".into(),
            app_name: app_name.into(),
            exe_path: exe_path.into(),
            args: args.into(),
            working_dir: working_dir.into(),
            timestamp: chrono::Utc::now().to_rfc3339(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct InterceptResponse {
    #[serde(rename = "type")]
    pub msg_type: String,
    pub action: String, // "continue" | "cancel"
}

// ── Platform: Windows named pipe ─────────────────────────────────────────────

#[cfg(windows)]
pub fn send_event(event: &InterceptEvent) -> Result<String> {
    use windows::Win32::{
        Foundation::GENERIC_READ,
        Storage::FileSystem::{
            CreateFileW, FILE_FLAG_OVERLAPPED, FILE_SHARE_NONE, OPEN_EXISTING,
        },
    };

    let pipe_wide: Vec<u16> = PIPE_NAME
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();

    let deadline = Instant::now() + CONNECT_TIMEOUT;
    let handle = loop {
        // SAFETY: pipe_wide is a valid null-terminated UTF-16 string; all other
        // parameters follow the documented Win32 CreateFileW contract.
        let h = unsafe {
            CreateFileW(
                windows::core::PCWSTR(pipe_wide.as_ptr()),
                GENERIC_READ.0 | 0x4000_0000u32, // GENERIC_READ | GENERIC_WRITE
                FILE_SHARE_NONE,
                None,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                None,
            )
        };
        match h {
            Ok(h) => break h,
            Err(_) if Instant::now() < deadline => {
                std::thread::sleep(CONNECT_RETRY_INTERVAL);
            }
            Err(e) => bail!("could not connect to pipe after timeout: {e}"),
        }
    };

    // SAFETY: handle is a valid open pipe handle obtained from CreateFileW above.
    let file = unsafe {
        <std::fs::File as std::os::windows::io::FromRawHandle>::from_raw_handle(handle.0 as _)
    };
    write_and_read(file)(event)
}

// ── Platform: macOS Unix socket ───────────────────────────────────────────────

#[cfg(target_os = "macos")]
pub fn send_event(event: &InterceptEvent) -> Result<String> {
    use std::os::unix::net::UnixStream;

    let path = socket_path();
    let deadline = Instant::now() + CONNECT_TIMEOUT;

    // Retry until the UI creates the socket or we time out.
    let stream = loop {
        match UnixStream::connect(&path) {
            Ok(s) => break s,
            Err(_) if Instant::now() < deadline => {
                std::thread::sleep(CONNECT_RETRY_INTERVAL);
            }
            Err(e) => bail!("could not connect to socket {path:?} after timeout: {e}"),
        }
    };

    write_and_read(stream)(event)
}

#[cfg(not(any(windows, target_os = "macos")))]
pub fn send_event(_event: &InterceptEvent) -> Result<String> {
    bail!("IPC is not supported on this platform")
}

// ── Shared read/write logic ───────────────────────────────────────────────────

fn write_and_read(mut stream: impl std::io::Read + std::io::Write) -> impl FnOnce(&InterceptEvent) -> Result<String> {
    move |event: &InterceptEvent| {
        let json = serde_json::to_string(event).context("serialize event")?;
        stream.write_all(json.as_bytes())?;
        stream.write_all(b"\n")?;
        stream.flush()?;

        let mut reader = BufReader::new(&mut stream);
        let mut line = String::new();
        reader.read_line(&mut line)?;
        let trimmed = line.trim();
        if trimmed.is_empty() {
            anyhow::bail!("empty response — connection closed before UI replied");
        }
        let resp: InterceptResponse =
            serde_json::from_str(trimmed).context("deserialize response")?;
        Ok(resp.action)
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn intercept_event_serialises() {
        let ev = InterceptEvent::new("Steam", "/Applications/Steam.app", "-silent", "/");
        let json = serde_json::to_string(&ev).unwrap();
        assert!(json.contains(r#""type":"InterceptEvent""#));
        assert!(json.contains(r#""app_name":"Steam""#));
    }

    #[test]
    fn intercept_response_deserialises_continue() {
        let json = r#"{"type":"InterceptResponse","action":"continue"}"#;
        let resp: InterceptResponse = serde_json::from_str(json).unwrap();
        assert_eq!(resp.action, "continue");
    }

    #[test]
    fn intercept_response_deserialises_cancel() {
        let json = r#"{"type":"InterceptResponse","action":"cancel"}"#;
        let resp: InterceptResponse = serde_json::from_str(json).unwrap();
        assert_eq!(resp.action, "cancel");
    }

    #[test]
    fn round_trip_event() {
        let ev = InterceptEvent::new("Discord", "/Applications/Discord.app", "", "/");
        let json = serde_json::to_string(&ev).unwrap();
        let ev2: InterceptEvent = serde_json::from_str(&json).unwrap();
        assert_eq!(ev, ev2);
    }
}

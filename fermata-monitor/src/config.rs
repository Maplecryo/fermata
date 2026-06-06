//! Reads and watches `%APPDATA%\Fermata\fermata.json`.
//! Provides a thread-safe handle that other modules clone and poll.
//! Does not write config — that is the UI's responsibility.

use std::{
    path::{Path, PathBuf},
    sync::{Arc, RwLock},
};

use anyhow::{Context, Result};
use notify::{RecommendedWatcher, RecursiveMode, Watcher};
use serde::{Deserialize, Serialize};

// ── Constants ────────────────────────────────────────────────────────────────

const CONFIG_FILENAME: &str = "fermata.json";
const APP_DIR_NAME: &str = "Fermata";

// ── Types ────────────────────────────────────────────────────────────────────

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Config {
    #[serde(default = "default_delay")]
    pub delay_seconds: u32,

    #[serde(default)]
    pub require_journal: bool,

    #[serde(default)]
    pub apps: Vec<String>,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            delay_seconds: default_delay(),
            require_journal: false,
            apps: Vec::new(),
        }
    }
}

fn default_delay() -> u32 {
    30
}

/// Thread-safe shared config handle.
pub type SharedConfig = Arc<RwLock<Config>>;

// ── Public API ───────────────────────────────────────────────────────────────

/// Returns the path to the Fermata data directory, creating it if needed.
pub fn data_dir() -> Result<PathBuf> {
    let base = dirs::data_local_dir()
        .context("could not resolve %APPDATA%")?;
    let dir = base.join(APP_DIR_NAME);
    std::fs::create_dir_all(&dir)
        .with_context(|| format!("could not create data dir {dir:?}"))?;
    Ok(dir)
}

/// Loads config from disk, falling back to defaults if the file is missing or
/// unparseable. Never returns an error for a missing file.
pub fn load(path: &Path) -> Config {
    match std::fs::read_to_string(path) {
        Err(_) => {
            log::warn!("config file not found at {path:?}, using defaults");
            Config::default()
        }
        Ok(text) => serde_json::from_str(&text).unwrap_or_else(|e| {
            log::error!("config parse error: {e}; using defaults");
            Config::default()
        }),
    }
}

/// Starts a background file-system watcher that reloads config into `shared`
/// whenever `fermata.json` changes. Returns the watcher (must be kept alive).
pub fn watch(path: PathBuf, shared: SharedConfig) -> Result<RecommendedWatcher> {
    let path_clone = path.clone();
    let mut watcher = notify::recommended_watcher(move |res: notify::Result<notify::Event>| {
        if res.is_ok() {
            let new = load(&path_clone);
            if let Ok(mut guard) = shared.write() {
                *guard = new;
                log::info!("config reloaded");
            }
        }
    })?;

    // Watch the parent directory so we catch atomic saves (write + rename).
    if let Some(dir) = path.parent() {
        watcher.watch(dir, RecursiveMode::NonRecursive)?;
    }
    Ok(watcher)
}

/// Builds the canonical config path and returns `(path, initial_config)`.
pub fn init() -> Result<(PathBuf, Config)> {
    let dir = data_dir()?;
    let path = dir.join(CONFIG_FILENAME);
    let config = load(&path);
    Ok((path, config))
}

// ── Helpers ──────────────────────────────────────────────────────────────────

/// Case-insensitive check: is `exe_name` (just the filename) in the restricted list?
pub fn is_restricted(config: &Config, exe_name: &str) -> bool {
    let lower = exe_name.to_lowercase();
    config.apps.iter().any(|a| a.to_lowercase() == lower)
}

// ── Tests ────────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use tempfile::NamedTempFile;

    fn sample_config() -> Config {
        Config {
            delay_seconds: 20,
            require_journal: true,
            apps: vec!["steam.exe".into(), "Discord.EXE".into()],
        }
    }

    #[test]
    fn parses_valid_config() {
        let json = r#"{"delay_seconds":20,"require_journal":true,"apps":["steam.exe","Discord.EXE"]}"#;
        let mut f = NamedTempFile::new().unwrap();
        f.write_all(json.as_bytes()).unwrap();
        let c = load(f.path());
        assert_eq!(c, sample_config());
    }

    #[test]
    fn missing_file_gives_defaults() {
        let c = load(Path::new("/nonexistent/path/fermata.json"));
        assert_eq!(c, Config::default());
    }

    #[test]
    fn bad_json_gives_defaults() {
        let mut f = NamedTempFile::new().unwrap();
        f.write_all(b"not json {{{").unwrap();
        let c = load(f.path());
        assert_eq!(c, Config::default());
    }

    #[test]
    fn is_restricted_case_insensitive() {
        let cfg = sample_config();
        assert!(is_restricted(&cfg, "steam.exe"));
        assert!(is_restricted(&cfg, "STEAM.EXE"));
        assert!(is_restricted(&cfg, "discord.exe"));
        assert!(!is_restricted(&cfg, "notepad.exe"));
    }

    #[test]
    fn is_restricted_ignores_path_prefix() {
        // Callers must strip the path before calling — this test documents that.
        let cfg = Config {
            apps: vec!["steam.exe".into()],
            ..Default::default()
        };
        // Full path should NOT match (stripping is the monitor's job)
        assert!(!is_restricted(&cfg, r"C:\Program Files\Steam\steam.exe"));
        // Just the filename should match
        assert!(is_restricted(&cfg, "steam.exe"));
    }
}

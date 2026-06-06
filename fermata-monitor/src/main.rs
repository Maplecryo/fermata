//! fermata-monitor entry point. Initialises config, takes a startup process
//! snapshot (edge case 1), starts the file watcher, then runs the monitor loop.
//! All platform-specific logic is contained in the other modules.

mod config;
mod interceptor;
mod ipc;
mod monitor;

use std::{
    collections::HashSet,
    sync::{Arc, RwLock},
};

use anyhow::Result;

fn main() -> Result<()> {
    env_logger::Builder::from_env(
        env_logger::Env::default().default_filter_or("info"),
    )
    .init();

    log::info!("fermata-monitor starting");

    let (config_path, initial_config) = config::init()?;
    log::info!("config loaded from {config_path:?}");
    log::info!("restricted apps: {:?}", initial_config.apps);

    let shared = Arc::new(RwLock::new(initial_config));

    // Start file watcher — must be kept alive for the duration of the process.
    let _watcher = config::watch(config_path, Arc::clone(&shared))?;

    // Snapshot processes already running at startup (edge case 1).
    let already_running: HashSet<u32> = monitor::snapshot_processes()
        .unwrap_or_default()
        .into_iter()
        .map(|p| p.pid)
        .collect();
    log::info!("{} processes already running at startup", already_running.len());

    // Blocking monitor loop.
    monitor::run(shared, already_running);

    Ok(())
}

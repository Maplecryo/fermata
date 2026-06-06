# Claude Code Prompt: "Fermata" — Mindful App Launch Interceptor

---

## Context and Goal

You are building **Fermata**, a Windows desktop productivity tool that inserts a deliberate pause between a user and their most distracting applications. When the user launches a restricted app (e.g., `steam.exe`, `discord.exe`), Friction detects it, terminates the process before it can fully load, shows a reflection window with a countdown timer and optional journal prompt, then either relaunches the app (if the user chooses to continue) or cancels.

The purpose is behavioral: not to block access, but to interrupt the automatic, mindless reflex of opening a distraction. A determined user can always continue. The friction is the point.

Build this as a complete, working MVP. Implement everything described. Do not scaffold or stub — each step should be fully functional before moving to the next.

---

## Tech Stack

```
Background process monitor:   Rust (friction-monitor binary)
UI layer:                     Rust + Avalonia UI via .NET interop, OR C# Avalonia (your call on bridging)
                              → Preferred: friction-monitor in Rust, friction-ui as a separate C# Avalonia project
Database:                     SQLite
Config:                       JSON (serde_json in Rust, System.Text.Json in C#)
IPC:                          Windows Named Pipes (between monitor and UI)
Interception method:          Kill + Relaunch (Option A)
Target platform:              Windows 10/11 only for this build
```

> Design the architecture so that the interception layer is the only platform-specific component. The UI, database, config, and analytics logic should be portable to macOS in a future iteration.

---

## Project Structure

Create a top-level workspace structured as follows. Populate every file — no empty stubs.

```
friction/
├── Cargo.toml                    # Rust workspace
├── friction-monitor/             # Rust: background process monitor
│   ├── Cargo.toml
│   └── src/
│       ├── main.rs
│       ├── monitor.rs            # process polling loop
│       ├── interceptor.rs        # kill + relaunch logic
│       ├── ipc.rs                # named pipe client
│       └── config.rs             # shared config read/watch
├── friction-ui/                  # C# Avalonia: tray, reflection window, settings, history
│   ├── FrictionUI.csproj
│   ├── App.axaml / App.axaml.cs
│   ├── Views/
│   │   ├── ReflectionWindow.axaml + .axaml.cs
│   │   ├── SettingsWindow.axaml + .axaml.cs
│   │   └── HistoryWindow.axaml + .axaml.cs
│   ├── ViewModels/
│   │   ├── ReflectionViewModel.cs
│   │   ├── SettingsViewModel.cs
│   │   └── HistoryViewModel.cs
│   └── Services/
│       ├── IpcServer.cs
│       ├── DatabaseService.cs
│       └── ConfigService.cs
├── shared/
│   └── friction.schema.json      # canonical JSON schema for friction.json
├── config/
│   └── friction.default.json     # default config shipped with the app
└── README.md                     # build instructions, architecture notes
```

---

## Implementation Plan

Build in this exact order. Confirm each step compiles and runs before proceeding.

### Step 1 — Project Scaffolding

- Initialize the Rust workspace with `friction-monitor` as a member.
- Initialize the C# Avalonia project (`friction-ui`) in the same repo root.
- Create `%APPDATA%\Friction\` as the runtime data directory (create on first run).
- Implement the shared config schema (see Config section below) and a read/write implementation in both projects.
- Verify both projects build cleanly with no warnings.

### Step 2 — Process Monitor Core

Implement in `monitor.rs`:

- Poll running processes every **500ms** using the Windows API (`EnumProcesses` + `OpenProcess` + `GetModuleFileNameEx`, via the `winapi` crate).
- For each process, extract: full exe path, exe filename (lowercase), PID.
- Compare exe filename (case-insensitive) against the restricted list in config.
- When a match is found, pass the result to the interceptor.

Implement in `interceptor.rs`:

- **Before killing**, capture from the process: full exe path, command-line arguments (via `NtQueryInformationProcess` / `ReadProcessMemory` targeting the PEB, or WMI `Win32_Process.CommandLine` as fallback), working directory.
- Kill the process with `TerminateProcess`.
- Enforce a **3-second cooldown** per exe filename after a kill, tracked in a `HashMap<String, Instant>`. This prevents the monitor from re-intercepting the process it just relaunched.
- Send an `InterceptEvent` over the named pipe IPC channel and wait (blocking) for an `InterceptResponse`.
- If response is `"continue"`: relaunch using `CreateProcess` with the captured exe path, args, and working dir.
- If response is `"cancel"` or the pipe times out (delay_seconds + 30s): do nothing.

Write unit tests for:
- Exe filename matching (case-insensitive, with and without path prefix)
- Cooldown logic
- Config parsing

### Step 3 — IPC Channel

**Protocol:** JSON over Windows Named Pipe named `\\.\pipe\FrictionIPC`.

Define these message types (used by both projects):

```json
// Monitor → UI
{
  "type": "InterceptEvent",
  "app_name": "Steam",
  "exe_path": "C:\\Program Files (x86)\\Steam\\steam.exe",
  "args": "-silent",
  "working_dir": "C:\\Program Files (x86)\\Steam",
  "timestamp": "2025-05-30T14:22:00Z"
}

// UI → Monitor
{
  "type": "InterceptResponse",
  "action": "continue"   // or "cancel"
}
```

- `friction-ui` (C# `IpcServer.cs`) starts a named pipe **server** on launch, listens for `InterceptEvent` messages, raises an internal event/callback, then writes back the `InterceptResponse` after the user acts.
- `friction-monitor` (Rust `ipc.rs`) connects as a **client**, writes the event, and blocks waiting for the response with a timeout.
- Handle reconnection: if the UI is not yet running when the monitor sends an event, retry connecting every 500ms for up to 10 seconds, then fall back to cancel.

### Step 4 — Reflection Window UI

Design aesthetic: **warm parchment — light, contemplative, journaling-app feel**. The window should feel like being asked to pause and write in a notebook, not like hitting a firewall. The warmth and softness make the journal prompt feel natural rather than punitive.

Color palette — use these exact values, defined as named resources in `App.axaml`:

```xml
<!-- App.axaml resource dictionary -->
<Color x:Key="FrictionBg">#F4EFE4</Color>       <!-- warm cream background -->
<Color x:Key="FrictionSurface">#EBE4D6</Color>  <!-- slightly deeper, for inputs/cards -->
<Color x:Key="FrictionText">#2A2420</Color>      <!-- near-black warm brown for primary text -->
<Color x:Key="FrictionMuted">#9A8F84</Color>     <!-- taupe for labels and secondary text -->
<Color x:Key="FrictionAccent">#7D4E2D</Color>    <!-- terracotta/sienna for countdown + active states -->
<Color x:Key="FrictionBorder">#C5BBAA</Color>    <!-- warm sand for input and button borders -->
<Color x:Key="FrictionBtnBg">#EBE4D6</Color>     <!-- button resting state -->
```

Typography: embed **Lora** (serif, available via Google Fonts — bundle the .ttf in the Avalonia project as an embedded resource). Use Lora Regular for body text and the app name. Use Lora Bold for the countdown number. Fall back to Georgia if embedding fails. This gives the window a handwritten-notebook quality that pairs with the journaling prompt.

Window specs: always-on-top, centered on the primary monitor, fixed size (480×320px if no journal, 480×460px with journal), no title bar chrome (custom `WindowChrome`), thin `1px` warm border (`FrictionBorder`) around the entire window, corner radius 6px. Not shown in taskbar.

Layout (top to bottom):

```
┌─────────────────────────────────────┐  bg: #F4EFE4
│                                     │
│  You are opening                    │  ← 11px, muted #9A8F84, italic
│  Steam                              │  ← 26px, Lora Bold, #2A2420
│                                     │
│  Take a moment before continuing.   │  ← 13px, muted #9A8F84
│                                     │
│  ┌ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┐  │
│  │ What are you planning to do?  │  │  ← only if require_journal: true
│  │ surface bg #EBE4D6, 1px warm  │  │  ← border #C5BBAA, radius 4px
│  │ border, 3 lines tall          │  │
│  └ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┘  │
│                                     │
│              18                     │  ← 48px Lora Bold, terracotta #7D4E2D
│         seconds left                │  ← 10px muted, below number
│                                     │
│  [ Cancel ]          [ Continue ]   │  ← Continue disabled until 0 (+ journal)
│                                     │
└─────────────────────────────────────┘
```

Button styling:
- Both buttons: `FrictionBtnBg` background, `1px FrictionBorder`, radius 4px, `FrictionText` label, 13px Lora Regular.
- Continue (enabled): accent `FrictionAccent` border (`1.5px`), label color `FrictionAccent`.
- Continue (disabled): same as Cancel, opacity 0.4.
- Cancel hover: surface darkens slightly to `#DDD5C3`.
- Continue hover (enabled): bg shifts to `#F0E8DC`, border stays terracotta.

Behaviors:
- Countdown ticks every second. When it hits 0, it stops at `0` and the label below changes from "seconds left" to "ready when you are" in `FrictionAccent`.
- Continue button enables when: countdown = 0 AND (require_journal is false OR journal field has ≥ 10 non-whitespace characters).
- Cancel closes the window immediately and sends `"cancel"` over IPC.
- Continue sends `"continue"` over IPC, then closes.
- Both actions trigger a SQLite log write.
- Animate the countdown number: each tick, briefly scale the number up (108%) then back (1.0) over 180ms — a soft, breath-like pulse. In Avalonia, implement as a `ScaleTransform` animation triggered on the `CountdownSeconds` property change.

### Step 5 — SQLite Logging

Database location: `%APPDATA%\Friction\friction.db`

Schema:

```sql
CREATE TABLE IF NOT EXISTS launches (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp   TEXT NOT NULL,           -- ISO 8601
    application TEXT NOT NULL,           -- e.g. "steam.exe"
    exe_path    TEXT NOT NULL,
    outcome     TEXT NOT NULL,           -- "continued" | "cancelled" | "timeout"
    waited_ms   INTEGER NOT NULL         -- milliseconds user waited before acting
);

CREATE TABLE IF NOT EXISTS journal_entries (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    launch_id   INTEGER NOT NULL REFERENCES launches(id),
    timestamp   TEXT NOT NULL,
    entry       TEXT NOT NULL
);
```

- Create tables on first run if they don't exist.
- Log every interception immediately when the reflection window opens (outcome = pending), then update outcome when the user acts.
- `DatabaseService.cs` exposes: `LogLaunch()`, `UpdateOutcome()`, `InsertJournalEntry()`, `GetHistory(int limit)`, `GetWeeklySummary()`.

### Step 6 — System Tray

`friction-ui` must run as a **tray-only** application — no main window, no taskbar entry.

Tray icon states (use distinct icons or color overlays):
- Green dot: monitoring active
- Yellow dot: paused / reflection window open
- Red dot: monitor process not detected (poll for `friction-monitor.exe` every 5s)

Tray context menu:

```
● Friction is active
─────────────────
  Settings
  View History
─────────────────
  Exit
```

- Clicking the tray icon opens Settings.
- "Exit" closes `friction-ui`. (The monitor runs independently; note in UI that the monitor remains active.)

### Step 7 — Settings Window

Window: 520×440px, titled "Friction — Settings", standard Avalonia chrome. Apply the Warm Parchment palette: window background `FrictionBg` (`#F4EFE4`), input backgrounds `FrictionSurface` (`#EBE4D6`), all borders `FrictionBorder` (`#C5BBAA`). Typography: Lora Regular throughout, same embedded font as the reflection window.

Sections:

**Restricted Applications**
- Scrollable list of current restricted exe names. List rows alternate between `FrictionBg` and `FrictionSurface`. Selected row gets a `1.5px FrictionAccent` left border accent.
- Text input (`FrictionSurface` bg, `FrictionBorder` border, Lora 13px) + "Add" button (`FrictionAccent` text, `FrictionBorder` border). Validate: must end in `.exe`, must not be in the system blocklist (see Edge Cases), must not be a duplicate. Show inline error in `FrictionMuted` italic below the input, not a dialog.
- Select an item in the list → "Remove" button activates. Confirm with an inline "Are you sure?" state on the button (click once to arm, click again to confirm); armed state uses `FrictionAccent` border.

**Delay**
- Label: "Seconds before you can continue" in `FrictionMuted`.
- Slider: 10 to 120, step 5, default 30. Thumb color `FrictionAccent`. Show current value in `FrictionAccent` weight 600 next to slider.

**Journal**
- Checkbox: "Require a journal entry before continuing". Checked state uses `FrictionAccent` fill.

**Save**
- "Save Changes" button: full-width, `FrictionAccent` background, cream text `#F4EFE4`, Lora Regular 13px, radius 4px.
- Both `friction-monitor` and `friction-ui` watch the file with `FileSystemWatcher` / Rust's `notify` crate and reload without restart. Show a brief "Saved." confirmation in `FrictionMuted` italic beneath the button, fading out after 2 seconds.

### Step 8 — History / Analytics Window

Window: 700×520px, titled "Friction — History", standard chrome, launched from tray. Same palette: `FrictionBg` window background, Lora font.

**Summary bar** (top row, `FrictionSurface` background, `1px FrictionBorder` bottom border):
- Three stat tiles side by side: "This week", "Continued", "Cancelled"
- Stat number in 22px Lora Bold `FrictionText`; label in 11px `FrictionMuted`.
- "Continued" number in `FrictionAccent` to give it positive weight.

**Table** (main area):
- Header row: `FrictionSurface` bg, `FrictionMuted` 11px uppercase labels, `0.5px FrictionBorder` bottom border. Clicking headers sorts.
- Columns: Date/Time | Application | Waited | Outcome | Journal
- Default sort: newest first.
- Row hover: bg shifts to `FrictionSurface`.
- "Outcome" column: inline pill chip — "Continued" (`FrictionAccent` text on `#F0E6DC` bg), "Cancelled" (`#8A4040` text on `#F5E8E8` bg), "Timeout" (`FrictionMuted` text on `FrictionSurface` bg). All chips 11px, radius 3px, no borders.
- "Journal" column: truncated to 40 chars in `FrictionMuted` italic; clicking the cell expands it inline to full text.
- Paginate at 50 rows. "Load more" button at bottom center: `FrictionSurface` bg, `FrictionBorder` border, `FrictionMuted` text.

---

## Edge Cases — Implement All of These

Handle each of the following explicitly. Do not leave any as TODO.

1. **App already running at monitor start:** On startup, `friction-monitor` snapshots currently running processes and adds them to an "already running" set. Only intercept processes that appear *after* that snapshot. Do not kill already-open apps.

2. **Infinite loop / relaunch retrigger:** The 3-second cooldown per exe filename (implemented in Step 2) prevents a relaunched process from being immediately re-intercepted.

3. **Multiple simultaneous launches:** If an `InterceptEvent` arrives at `IpcServer` while a reflection window is already visible, queue the second event. Show it immediately after the first window closes.

4. **Launcher stub chains:** Some apps use a launcher that spawns the real process (e.g., Steam bootstrapper → `steam.exe`). Target by exe filename regardless of parent process. Both `steamwebhelper.exe` and `steam.exe` might appear — only intercept the one in the config.

5. **System process blocklist:** Reject any attempt to add these exe names to the restricted list. Show a clear inline error message if attempted:
   `explorer.exe`, `winlogon.exe`, `csrss.exe`, `lsass.exe`, `svchost.exe`, `taskmgr.exe`, `dwm.exe`, `friction-monitor.exe`, `friction-ui.exe`

6. **UI crash recovery:** If `friction-ui` crashes while `friction-monitor` is blocking on IPC, the pipe read will fail. Treat any IPC error as `"cancel"` — do not relaunch the intercepted app.

7. **Missing exe on relaunch:** Before calling `CreateProcess`, verify the captured exe path still exists. If not, log a `"timeout"` outcome and show a Windows tray notification: "Could not relaunch [app] — file not found."

8. **Journal minimum enforcement:** When `require_journal` is true, count non-whitespace characters in the journal field. The Continue button must remain disabled until both conditions are true: countdown = 0 AND non-whitespace char count ≥ 10.

9. **Config file missing:** If `friction.json` does not exist on startup, create it from `friction.default.json` with an empty restricted list and sensible defaults (delay: 30, require_journal: false).

---

## Config Schema

`%APPDATA%\Friction\friction.json`:

```json
{
  "delay_seconds": 30,
  "require_journal": false,
  "apps": [
    "steam.exe",
    "discord.exe"
  ]
}
```

Both processes reload this file on `FileSystemWatcher` / `notify` change events without restarting.

---

## Code Quality Requirements

Apply these standards throughout. Do not defer them to a cleanup pass.

- **Rust:** No `unwrap()` or `expect()` in production code paths. Use `anyhow` for error propagation in binaries, `thiserror` for library error types. All `unsafe` blocks must have a comment explaining why it is safe.
- **C#:** Follow MVVM strictly — no business logic in `.axaml.cs` files. ViewModels expose `ICommand` and `INotifyPropertyChanged`. Services are injected via constructor.
- **Constants:** All magic numbers (poll interval, cooldown duration, IPC timeout, journal minimum chars, system blocklist) are named constants at the top of their respective files, not inline literals.
- **Paths:** Never hardcode paths. Derive all paths from `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` in C# and `dirs::data_local_dir()` in Rust.
- **Module headers:** Each source file begins with a doc comment block (3–5 lines) describing what the file does and what it does not do.
- **Tests:** Write tests for — process matching logic, cooldown logic, config parsing/serialization, IPC message round-trip serialization, journal character counting, system blocklist enforcement.

---

## Deliverable Checklist

Before considering the build complete, verify:

- [ ] `friction-monitor` compiles with `cargo build --release`
- [ ] `friction-ui` compiles with `dotnet build`
- [ ] Launching a restricted app (e.g., notepad.exe added to config) triggers the reflection window
- [ ] Continue button is disabled during countdown; enables at 0
- [ ] Clicking Continue relaunches the intercepted app
- [ ] Clicking Cancel does not relaunch
- [ ] Both actions are logged to SQLite
- [ ] Journal text is saved when require_journal is true
- [ ] Settings changes persist across restarts
- [ ] History window shows logged sessions
- [ ] Tray icon is present and context menu works
- [ ] All 8 edge cases have visible handling (add notepad.exe and system process to settings to verify blocklist, etc.)
- [ ] `README.md` explains how to build, run, and test both projects

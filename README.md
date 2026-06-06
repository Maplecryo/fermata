# Fermata

A macOS and Windows app that intercepts restricted apps (Discord, Steam, etc.) before they fully load. It shows a reflection window with a countdown and optional journal prompt, then either relaunches the app or cancels it.

---

## How it works

1. You add apps to a block list in the settings
2. Next time you open a blocked app, Fermata immediately closes it
3. A window appears with a countdown and a journal prompt ("What are you planning to do?")
4. After the countdown, you can either **Continue** (the app relaunches) or **Cancel**
5. If you continue, the app is whitelisted for that session. Next time you open it after closing it, the cycle repeats

---

## Building on macOS

### Step 1 — Download the project

If you have Git:
```bash
git clone https://github.com/YOUR_USERNAME/fermata.git
cd fermata
```

Or click **Code → Download ZIP** on GitHub, unzip it, and open Terminal in the folder.

### Step 2 — Build

Run this single command in Terminal:

```bash
bash build-app.sh
```

This will:
- Automatically install **Rust** if it's missing (via rustup)
- Automatically install **.NET 10 SDK** if it's missing (via Microsoft's installer)
- Build both the monitor and the UI
- Produce **`Fermata.app`** in the project folder

> **First run only:** after installing .NET you may need to add it to your PATH. The script will print the exact lines to add to your `~/.zshrc` if this is needed. After adding them, run `source ~/.zshrc` and then `bash build-app.sh` again.

### Step 3 — Install

Drag **`Fermata.app`** to your **Applications** folder. Double-click it to run.

Fermata lives in your menu bar — there is no Dock icon.

**Logs** are written to `~/Library/Logs/Fermata/monitor.log`.

---

## Building on Windows

### Step 1 — Download the project

If you have Git:
```powershell
git clone https://github.com/YOUR_USERNAME/fermata.git
cd fermata
```

Or click **Code → Download ZIP** on GitHub and unzip it.

### Step 2 — Allow PowerShell scripts (one-time)

Open **PowerShell as your normal user** (not as Administrator) and run:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Press `Y` to confirm.

### Step 3 — Install prerequisites

In PowerShell, navigate to the project folder and run:

```powershell
.\install-prerequisites.ps1
```

This will automatically install **Rust** and **.NET 10 SDK** if they are missing.

> If the script says "reopen PowerShell and run again", close PowerShell and reopen it, then run the script a second time.

### Step 4 — Build

```powershell
.\build-app.ps1
```

This produces a **`Fermata-Windows\`** folder containing everything needed to run the app.

### Step 5 — Run

Double-click **`Fermata-Windows\Fermata.bat`**.

To make it easier to launch, right-click `Fermata.bat` → **Send to → Desktop (create shortcut)**.

Fermata appears in your system tray (bottom-right corner). There is no taskbar window.

---

## Configuration

Settings are managed through the tray icon → **Settings**. The config file is stored at:

| Platform | Path |
|----------|------|
| macOS    | `~/Library/Application Support/Fermata/fermata.json` |
| Windows  | `%APPDATA%\Fermata\fermata.json` |

### Example config

```json
{
  "apps": ["Discord", "Steam", "twitter"],
  "countdown_seconds": 30,
  "require_journal": true
}
```

- **`apps`** — list of app names to block (case-insensitive, partial match works)
- **`countdown_seconds`** — how long the countdown runs before Continue is enabled
- **`require_journal`** — whether the user must write something before continuing

---

## Project structure

```
fermata-monitor/   Rust process monitor (detects and kills restricted apps)
fermata-ui/        C# Avalonia UI (tray icon, reflection window, settings, history)
build-app.sh       macOS build script → produces Fermata.app
build-app.ps1      Windows build script → produces Fermata-Windows\
install-prerequisites.sh   macOS auto-installer for Rust + .NET 10
install-prerequisites.ps1  Windows auto-installer for Rust + .NET 10
```

---

## Uninstalling

**macOS:** Drag `Fermata.app` from Applications to Trash. Delete `~/Library/Application Support/Fermata/` to remove all data.

**Windows:** Delete the `Fermata-Windows\` folder. Delete `%APPDATA%\Fermata\` to remove all data.

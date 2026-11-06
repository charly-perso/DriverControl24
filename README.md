# DriverControl24

🇬🇧 English · 🇫🇷 [Version française](README.fr.md)

Open-source driver for the **Digidesign Control|24** (Avid) console, usable as a control surface for **REAPER** on Windows and macOS.

This repository contains:

- **the driver** (`DriverReaperControl24`): talks to the Control24 over Ethernet and drives REAPER (transport, faders, mute/solo, pan, sends, FX, jog wheel, displays…);
- **the REAPER plugin** (`ReaperC24Bridge`): loaded by REAPER, it starts the driver and acts as its gateway to the REAPER API;
- **the configuration app** (`DriverConfig.Web`): a small local web app to change the driver's settings without editing the `.ini` file by hand;
- the **macOS installers** (`.pkg`).

License: [GPL v3](LICENSE).

---

## Installation (users)

Installers are in the [`Setup`](Setup) folder:

| Folder | Content |
|---|---|
| [`Setup/osX64`](Setup/osX64) | macOS Intel (x64) installers. **Available.** |
| [`Setup/osARM`](Setup/osARM) | Reserved for macOS Apple Silicon (ARM). *Not available yet.* |
| [`Setup/Win`](Setup/Win) | Windows installer. *Not available yet.* |

### macOS (Intel x64)

Download the three files from [`Setup/osX64`](Setup/osX64) and install them **in this order** (double-click each one):

1. **`Install ChmodBPF.pkg`**: gives the driver access to the network capture devices (`/dev/bpf*`) without running as administrator. The Control24 communicates with raw Ethernet frames, and macOS restricts these devices to root by default. **Restart your Mac** (or log out and back in) afterwards.
2. **`DriverReaperControl24-osx-x64-<version>.pkg`**: the driver and the REAPER plugins. It installs into `~/Library/Application Support/REAPER/UserPlugins`: `reaper_c24bridge.dylib`, `reaper_c24events.dylib`, the `DriverReaperControl24` driver (.NET runtime included, nothing else to install) and `ReaperDriverControl24.ini` (created only if missing: your configuration is never overwritten).
3. **`DriverConfig.Web-osx-x64-<version>.pkg`** *(optional)*: the `Control24 Config` app, installed in `/Applications`, to edit the configuration.

Then start REAPER: the driver starts automatically. The packages are not signed: if macOS blocks one, right-click it → *Open*.

### Configuration

`ReaperDriverControl24.ini` (next to the plugins) contains, among other things:

| Key (`[Driver]` section) | Purpose |
|---|---|
| `Autostart` | Start the driver automatically when REAPER loads the plugin. |
| `NetworkInterface` | Network interface connected to the Control24. `TestWithoutC24` simulates a console (test mode, with an interactive terminal). |
| `VSDebug` | `True`: REAPER **does not launch** the driver; you start it from Visual Studio (see below). |

**Configuration app**: `Control24 Config` opens a local page (`http://127.0.0.1:5119`) to change these options and to edit the FX parameters shown on the console (order, names, visibility).
To add FX parameters: add FX in REAPER, then press the **EXT TRANS** button on the Control24. Parameters already in the file are kept; only new parameters are added.

---

## Developers: fork and live-debug

### Tools

- **Visual Studio 2022+ (recommended)** rather than VS Code: .NET debugging (attach, breakpoints, on-the-fly evaluation, Edit & Continue) is noticeably faster and more comfortable there. VS Code works for editing, but it isn't the best choice for debugging the driver.
- **.NET SDK 10**.
- NativeAOT for the plugin: the platform's C++ toolchain (Visual Studio *Desktop development with C++*, or Xcode Command Line Tools on Mac).
- CMake + a C++ compiler for `NativeReaperPluginDLL` (`reaper_c24events`).

### Getting started

1. Fork the repository on GitHub, then clone your fork.
2. Open `DriverControl24.sln` in Visual Studio.
3. Build the REAPER plugin: `dotnet publish ReaperC24Bridge -r win-x64 -c Release` (or `-r osx-x64` on Mac) and copy `reaper_c24bridge` into REAPER's `UserPlugins` folder (`%APPDATA%\REAPER\UserPlugins` or `~/Library/Application Support/REAPER/UserPlugins`). The commands are also in `ReaperC24Bridge/build command`.
4. Put `ReaperDriverControl24.ini` next to the plugin.

### Live-debugging the driver

1. In the `.ini`, set `Autostart=True` and **`VSDebug=True`**.
2. Start REAPER: the plugin loads and opens its communication pipes but **does not start** the driver.
3. In Visual Studio, set `DriverReaperControl24` as the startup project and run it (**F5**). It connects to the plugin's pipes. Set breakpoints, edit, rerun: no need to restart REAPER.
4. Without a physical console, use `NetworkInterface=TestWithoutC24` (an interactive console window opens; on macOS the plugin opens it in Terminal).

Note: with `VSDebug=False`, the driver is launched by REAPER and locks its files; close it before redeploying from Visual Studio. Also check the output folder of the `DriverReaperControl24` project: it may write directly into REAPER's `UserPlugins` folder.

### Building the macOS installers

```bash
./DriverReaperControl24/Setup/build-osx-x64.sh [version]   # driver + REAPER plugins
./DriverConfig.Web/Setup/build-osx-x64.sh [version]        # configuration app
```

Rebuilt installers: copy the resulting `.pkg` files from the `Setup/out` folders into `Setup/osX64`.

---

## Usage

All the driver's functions are described in the **[user manual](docs/USER-MANUAL.md)** ([version française](docs/MANUEL-UTILISATEUR.md)).

---

## Architecture

```
 Control24  ⇄  Ethernet (pcap)  ⇄  DriverReaperControl24  ⇄  pipes  ⇄  reaper_c24bridge  ⇄  REAPER API
                                          │                                   │
                                          └── ReaperDriverControl24.ini ──────┘
                                                      ▲
                                              DriverConfig.Web
```

| Project | Role |
|---|---|
| `DriverReaperControl24` | The driver (.NET process). Control24 protocol (`Control24.vb`), REAPER control logic (`C24ReaperController.vb`), REAPER access (`Reaper.vb`), entry point (`Main.vb`). |
| `ReaperC24Bridge` | REAPER plugin entry point (C#, NativeAOT → `reaper_c24bridge`). |
| `DriverControl24.Extension` | Plugin logic: REAPER API calls (`ReaperAPI.vb`), pipe servers, driver start/stop (`ReaperBridge.vb`). |
| `NativeReaperPluginDLL` | Small native C++ plugin (`reaper_c24events`) for REAPER events. |
| `DriverConfig` | Shared library: `.ini` reading/writing, session configuration, FX naming. |
| `DriverConfig.Web` | Configuration app (Blazor Server, listens on `127.0.0.1` only). |
| `DriverControl24` | WinForms application (Reacontrol24): network tools and console tests. |
| `Launcher` | Launcher. |
| `*.Tests` | Unit tests (MSTest). |

### Why VB.NET?

The driver is written in **VB.NET**, a deliberate choice: the language is very verbose, hence very readable, and I like that. If you prefer C#, there's no obstacle: VB.NET and C# compile to the same IL and use the same .NET class library, so asking an AI (or a converter) to convert everything to C# gives an equivalent result. The existing C# projects (`ReaperC24Bridge`, `DriverConfig.Web`) already interoperate with the VB projects without any issue.

---

## Contributing

Feature requests and bug fixes are all welcome:

- **An idea, a feature, a bug?** Open an **issue** in the repository's *Issues* tab (REAPER version, OS, steps to reproduce, a log excerpt if possible).
- **Want to fix or build it yourself?** Fork, create a branch, then open a **pull request**. Contributors are welcome: feel free to become a project contributor.

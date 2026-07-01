<div align="center">

<h1>⚡ Core Workbench</h1>

<p><strong>NULLS / WORKBENCH</strong> — a sleek, all-in-one Windows system toolkit.<br/>
Monitor your hardware, tame your startup, clean junk, and overlay your FPS — all under one roof.</p>

<p>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11-0B0813?style=for-the-badge&logo=windows11&logoColor=8B5CFF" />
  <img alt=".NET" src="https://img.shields.io/badge/.NET%209-WPF-7E57D6?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img alt="Language" src="https://img.shields.io/badge/C%23-512BD4?style=for-the-badge&logo=csharp&logoColor=white" />
  <img alt="License" src="https://img.shields.io/badge/personal%20project-💜-1A1133?style=for-the-badge" />
</p>

<img src="Images/Dashboard.png" alt="Core Workbench dashboard" width="860" />

</div>

---

## ✨ What is it?

**Core Workbench** is a personal "Swiss-army" desktop app that bundles the tools I reach for most —
hardware monitoring, drive health, a performance overlay, a cleaner, notes, desktop widgets, and more —
behind one clean, custom-themed interface with a frameless title bar

> Built for myself, kept tidy enough to share.

---

## 🧩 Features

### 🏠 Home

Dashboard greeting & quick-access, a full rich-text **Notes** editor (search, pin, export to RTF, print to PDF),
and Rainmeter-style **desktop widgets** (CPU · GPU · RAM · Clock · combined system bar with sparklines) in floating or desktop mode.

<div align="center">
  <img src="Images/Notes.png"   alt="Notes"   width="425" />
  <img src="Images/Widgets.png" alt="Widgets" width="425" />
</div>

### 📊 Monitor

Live CPU / GPU / RAM gauges with per-core CPU, clocks, power, temps, VRAM and fan; per-adapter **network** throughput;
CrystalDiskInfo-style **S.M.A.R.T. / NVMe** drive health with a 5-band scale; a mini **task manager**; and an
**MSI Afterburner-style overlay** with real per-game **FPS / frametime / 1% & 0.1% lows** (via in-process ETW — no injection),
toggled by a global hotkey (`Ctrl + Alt + O`).

<div align="center">
  <img src="Images/Performance.png" alt="Performance" width="425" />
  <img src="Images/Processes.png"   alt="Processes"   width="425" />
  <img src="Images/Overlay.png"     alt="Overlay"     width="860" />
</div>

### 🧹 Optimize

Sweep Windows temp folders and the recycle bin with the **Cleaner**, manage **Startup** entries
(enable / disable / remove), and uninstall programs from a full **Apps** manager with size detection, sort and filters.

<div align="center">
  <img src="Images/Cleaner.png" alt="Cleaner" width="283" />
  <img src="Images/Startup.png" alt="Startup" width="283" />
  <img src="Images/Apps.png"    alt="Apps"    width="283" />
</div>

### ⚙️ System

At-a-glance **System Info** (OS, CPU, RAM per-stick, GPU, motherboard/BIOS, storage), plus **Settings**
for start-with-Windows and tray behaviour — with a themed system tray showing live CPU/GPU/RAM.

<div align="center">
  <img src="Images/SystemInfo.png" alt="System Info" width="425" />
  <img src="Images/Settings.png"   alt="Settings"    width="425" />
</div>

---

## 🚀 Install

### Option A — Installer (recommended)
1. Download **`CoreWorkbench-Setup.msi`** from the [Releases](../../releases) page.
2. Run it. The app installs to `Program Files\Core Workbench` with Start Menu & Desktop shortcuts.
3. Launch **Core Workbench**.

> The build is **self-contained** — no .NET runtime required on the target machine.

### Option B — Build from source
```bash
git clone https://github.com/TheOriginalNULL/Core-Workbench.git
cd "Core Workbench"
dotnet build -c Release
dotnet run --project "Core Workbench/Core Workbench.csproj"
```

To produce the installer yourself:
```bash
# 1) publish a single self-contained exe
dotnet publish "Core Workbench/Core Workbench.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 2) build the MSI (WiX 5 — free, no OSMF fee)
dotnet tool install --global wix --version 5.0.2
wix build Installer/CoreWorkbench.wxs -arch x64 ^
  -d PublishDir="...\win-x64\publish\" ^
  -d IconFile="...\Assets\SecondaryLogo.ico" ^
  -o Installer\out\CoreWorkbench-Setup.msi
```

---

## 🛠️ Tech Stack

| Area | Tech |
|------|------|
| UI | WPF (.NET 9), custom `WindowChrome` title bar, MVVM-lite |
| Hardware sensors | [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) |
| Drive health | WMI (`MSFT_PhysicalDisk`, `MSFT_StorageReliabilityCounter`) + raw NVMe/ATA S.M.A.R.T. via `DeviceIoControl` |
| FPS overlay | ETW (`Microsoft.Diagnostics.Tracing.TraceEvent`) — DXGI present events, **no injection** |
| Tray | WinForms `NotifyIcon` with a themed dark renderer |
| Installer | WiX Toolset 5 → MSI |

---

## 📌 Notes

- 🔐 **Requires administrator** — temperature sensors and raw S.M.A.R.T. reads need elevation (UAC prompt on launch).
- ✍️ **Unsigned build** — SmartScreen / Smart App Control may warn, since there's no code-signing certificate. Expected for a personal build.
- 🎯 **FPS caveat** — the overlay reads DXGI present events, so it covers DirectX titles; Vulkan/OpenGL-only apps won't report frames.
- 🖥️ **Windows only.**

---

## 🗺️ Roadmap ideas

- [ ] Power plan / Game mode toggle
- [ ] Disk space analyzer
- [ ] Services manager
- [ ] Duplicate finder & secure shredder
- [ ] Vulkan / OpenGL FPS providers

---

<div align="center">
  <sub>Made with 💜 and too much free time · <strong>NULLS</strong></sub>
</div>

<div align="center">

# ⚡ AdaptiveBarPC

**A contextual, non-activating Touch Bar control surface and visual studio for Windows secondary touchscreens.**

[![MIT License](https://img.shields.io/badge/License-MIT-2ea44f?style=flat-square)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square&logo=windows)
[![CI](https://github.com/nguyentung12300/AdaptiveBarPC/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/nguyentung12300/AdaptiveBarPC/actions/workflows/ci.yml)
![Tests](https://img.shields.io/badge/tests-589%2F589%20passing-brightgreen?style=flat-square)
[![Latest Release](https://img.shields.io/github/v/release/nguyentung12300/AdaptiveBarPC?style=flat-square&label=Release&color=blue)](https://github.com/nguyentung12300/AdaptiveBarPC/releases)

[**Explore the Docs**](docs/ARCHITECTURE.md) · [**Download Latest ZIP**](https://github.com/nguyentung12300/AdaptiveBarPC/releases) · [**Report Bug**](https://github.com/nguyentung12300/AdaptiveBarPC/issues) · [**Request Feature**](https://github.com/nguyentung12300/AdaptiveBarPC/issues)

<br />

<img src="docs/assets/demo.png" alt="AdaptiveBarPC Studio Preview" width="96%" />

</div>

---

## ✨ Why AdaptiveBarPC?

Most secondary screen dashboards on Windows are passive sensor panels, while traditional macro decks require a network client or steal window focus when touched. **AdaptiveBarPC** turns any secondary touchscreen strip into a native, context-aware control bar.

| Feature | What It Does |
| :--- | :--- |
| 🎯 **Never Steals Focus** | Built on Win32 `WS_EX_NOACTIVATE` and custom mouse-activation filtering so taps, sliders, and long-presses never interrupt your active game or creative app. |
| 🔄 **Context-Aware Switching** | Automatically switches profiles in real time based on the active foreground process or executable path. All layouts remain pure JSON. |
| 🎨 **AdaptiveBar Studio** | Includes a full visual drag-and-drop designer with live canvas preview, 100-step undo/redo, schema validation, and shareable `.adaptivebar-theme.zip` packages. |
| 🔊 **Smart Audio Routing** | Auto-detects **VoiceMeeter** buses when available and gracefully falls back to **Windows Core Audio**, per-app mixer sessions, or in-memory state on headless systems. |
| 🔒 **100% Local & Offline** | Zero telemetry or cloud accounts. Bundled 3D Fluent Emoji assets, hardware sensors, and profiles run entirely offline with instant startup. |

---

## 🧩 Built-in Widgets & Actions

AdaptiveBarPC ships with **15 widget types** and **13 action types** out of the box:

- **Audio & Media:** Master Volume slider, Per-App Audio Mixer, VoiceMeeter Bus/Strip controls, Media Playback (`GSMTC` with album art & seek bar).
- **System & Hardware:** Live CPU / GPU / RAM temperature & load gauges (`LibreHardwareMonitor`), Clock, Network status.
- **Productivity & Input:** Context-aware Emoji & Sticker picker, Clipboard history bar, Multi-page navigation, Pinned persistent controls.
- **Triggers:** Keyboard shortcuts (`SendInput`), App launcher, Text/Macro injection, Long-press secondary actions, and opt-in Shell commands.

---

## 🚀 Quick Start

### Option 1: Download Prebuilt Release (Recommended)

1. Download `AdaptiveBarPC-v0.2.4-win-x64.zip` from the [**Releases**](https://github.com/nguyentung12300/AdaptiveBarPC/releases) page.
2. Extract the archive. Run `AdaptiveBarPC.exe` from the `AdaptiveBarPC/` folder for the touch bar, or `AdaptiveBarPC.Designer.exe` from `AdaptiveBarPC-Studio/` to open **AdaptiveBar Studio**.

### Option 2: Build from Source

**Requirements:** Windows 10 or 11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and a secondary touchscreen monitor for the full experience.

~~~powershell
git clone https://github.com/nguyentung12300/AdaptiveBarPC.git
cd AdaptiveBarPC
dotnet restore AdaptiveBarPC.sln

# Run the Touch Bar Surface
dotnet run --project src/AdaptiveBarPC.App/AdaptiveBarPC.App.csproj

# Run AdaptiveBar Studio (Visual Designer)
dotnet run --project src/AdaptiveBarPC.Designer/AdaptiveBarPC.Designer.csproj
~~~

---

## 🛠️ Customizing Profiles

Create profiles visually using **AdaptiveBar Studio**, or edit JSON files directly in `src/AdaptiveBarPC.App/Profiles`.

<details>
<summary><b>📄 View Minimal JSON Profile Example (Click to expand)</b></summary>

~~~json
{
  "id": "my-browser",
  "displayName": "My Browser",
  "priority": 50,
  "match": {
    "processNames": ["msedge"]
  },
  "pages": [
    {
      "id": "main",
      "displayName": "Main",
      "items": [
        {
          "id": "refresh",
          "type": "button",
          "label": "Refresh",
          "action": {
            "type": "key",
            "value": "CTRL+R"
          }
        }
      ]
    }
  ]
}
~~~

</details>

> 💡 Full schema details—including multi-page layouts, fixed items, animated GIF/MP4 backgrounds, and v0.1 compatibility—are documented in [**Profile Format & Examples**](docs/PROFILE_FORMAT.md).

---

## 📚 Documentation

| Document | Description |
| :--- | :--- |
| 🏛️ [**Architecture**](docs/ARCHITECTURE.md) | Layer separation, Win32 interop, and audio/sensor service lifecycles. |
| 📐 [**Profile Format**](docs/PROFILE_FORMAT.md) | Complete JSON schema reference, widget properties, and action triggers. |
| 🎨 [**Design System**](docs/DESIGN.md) | Visual tokens, typography, and layout specifications for AdaptiveBar Studio. |
| 🗺️ [**Roadmap**](docs/ROADMAP.md) | Completed milestones (v0.1 – v0.2) and upcoming community goals. |
| 🤝 [**Contributing**](CONTRIBUTING.md) | Build instructions, architectural invariants, and pull request guidelines. |
| 📜 [**Third-Party Notices**](THIRD-PARTY-NOTICES.md) | Attribution and licenses for bundled dependencies and assets. |

---

## 📄 License

Distributed under the [**MIT License**](LICENSE). Copyright © 2026 **Nguyen Quang Tung**.

See [**THIRD-PARTY-NOTICES.md**](THIRD-PARTY-NOTICES.md) for license terms of bundled third-party assets and libraries.

---

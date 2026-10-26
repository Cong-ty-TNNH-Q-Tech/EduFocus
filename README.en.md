<div align="center">

<img src="./assets/logo.png" alt="EduFocus Logo" width="180" />

# 🎓 EduFocus

**Responsible study monitoring — protecting your child's focus on the computer.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![Status](https://img.shields.io/badge/status-in%20development-blue)]()
[![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D6)]()
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](./CONTRIBUTING.md)

🇬🇧 **English** | **[Tiếng Việt](./README.md)**

</div>

---

## 🎯 The Problem

Students open their computers for online classes but easily get pulled into AI chatbots, social media, games, or entertainment videos. Parents lack a simple tool to:

- Keep the study environment focused without configuring a router or firewall.
- Know how long their child studied and whether they stayed focused.
- Turn protection on/off with a password, without the child removing it.

**EduFocus** is built for exactly that.

---

## ✨ Key Features

> This is the **planned MVP** scope, not a released build. Known limitations are listed below.

| Feature | Description |
|---|---|
| 🔐 **Parent PIN** | Only parents can enable/disable protection, edit the blocklist, view reports, or exit the app |
| 🚫 **Chatbot blocking** | Blocks target chatbot domains (ChatGPT, Claude, Gemini, Copilot, Perplexity, Character.AI, DeepSeek…) and configured apps |
| 📋 **Flexible blocklist** | Add custom domains/apps, temporary allowlists for permitted homework. URL/path rules require a browser extension (post-MVP) |
| 🕒 **Protection schedule** | Automatically enable/disable protection by weekly study hours |
| 📊 **Study session tracking** | Records study time, active apps/domains (best-effort), and observed blocked attempts |
| 👁️ **Attention estimate** | Uses the webcam to detect presence, screen-facing gaze, absence, or drowsiness |
| 🖥️ **Online class detection** | Separate tracking for Google Meet, Microsoft Teams, and Zoom |
| 🔒 **Local-first** | No cloud, no account, data stays on the device |
| 🛡️ **Basic tamper resistance** | Tray hiding, PIN-protected exit, auto-restart (30–60s), crash recovery |
| 📈 **Parent reports** | Daily/weekly dashboard, timeline, distractions, blocked attempts, CSV export |

---

## 🔒 Privacy First

EduFocus is designed **not to be spyware**:

- **No photos, videos, or audio are stored.** Webcam frames are processed in memory only to derive a state.
- **No page content, messages, or clipboard are captured.**
- **No study data is sent to the cloud.** Everything lives in a local SQLite database; if update checks are enabled, only version/OS is sent and the server can still see the IP.
- **No identity recognition**, no face embeddings, no emotion analysis.
- Reports always show **camera coverage** and **confidence** so parents don't misread results.
- The app should be disclosed openly to the student; covert monitoring is discouraged.

> **Limitation:** A webcam is not an infrared eye tracker. "Focus" is an estimate, not absolute proof. When Google Meet/Teams is using the camera, the app may not be able to observe if the machine does not support Multi-App Camera.
>
> **Blocking:** Hosts blocks domains but does not log HTTPS. The block page only appears over HTTP; HTTPS usually shows a connection/SSL error. Blocked-attempt counts are therefore **best-effort**, not absolute totals.

---

## 🧭 How It Works

```text
Parent creates a PIN
        ↓
Protection ON (manual or scheduled)
        ↓
Blocks chatbot domains + disallowed apps
        ↓
Records study sessions, apps/domains (best-effort), and attention states
        ↓
Parent reviews reports; student sees a block page over HTTP, HTTPS shows an error
        ↓
Protection turns off with PIN or when the schedule window ends
```

### Two modes

| Mode | For | Permissions |
|---|---|---|
| **Parent Mode** | Parents | Full configuration, reports, protection control |
| **Student Mode** | Students | View status, study time, block page; no configuration changes |

---

## 🖥️ Platform

- ✅ **Windows 11 23H2+** (64-bit) — primary platform
- ⚠️ Windows 10 22H2: may run, but Windows 10 is out of official support
- 🔜 macOS — not supported
- 🌐 Chrome, Edge, Firefox for domain blocking via hosts; block page over HTTP only
- 🎥 Built-in or external webcam (optional, for attention)

---

## 🏗️ Tech Stack

| Layer | Technology |
|---|---|
| **UI** | WPF .NET 10 LTS (target; code still targets `net9.0` and must be upgraded), MaterialDesignInXAML, CommunityToolkit.Mvvm |
| **Architecture** | Clean Architecture + Ports & Adapters |
| **Database** | SQLite + EF Core 10 (target; currently EF Core 9) |
| **Blocking** | Windows hosts file (exact domains) + local HTTP block page |
| **Activity** | Win32 foreground window, WMI, UI Automation |
| **Attention** | OpenCvSharp4 + ONNX Runtime (MediaPipe Face/Iris) |
| **Security** | PBKDF2-SHA256; no other secrets stored |
| **Installer** | WiX Toolset v4 (MSI) |
| **CI/CD** | GitHub Actions |

---

## 🚀 Install & Run

### Release

> EduFocus is being migrated from the predecessor project **Don't Be Lazy**. The MVP release is planned after Phase 7; features described here are not released yet.

Once released, download from [Releases](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/releases).

### Run from source

**Current requirements:** .NET 10 SDK (tests target `net10.0`; the WPF app is upgraded in Phase 1), Windows 11 23H2+ 64-bit (Windows 10 22H2 best-effort).

```bash
git clone https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus.git
cd EduFocus

dotnet restore DontBeLazy.slnx
dotnet run --project src/DontBeLazy.WPF/DontBeLazy.WPF.csproj
```

> **Note:** The codebase still uses the `DontBeLazy.*` project names. Renaming to `EduFocus.*` happens in Phase 1, and these commands will be updated accordingly.

Run tests:

```bash
dotnet test src/DontBeLazy.Domain.Tests/DontBeLazy.Domain.Tests.csproj
```

> The WPF app still targets `net9.0` (support ends November 10, 2026) while tests target `net10.0`; Phase 1 will move the entire solution to .NET 10 LTS and EF Core 10 before release.

---

## 📚 Documentation

- [📄 BA Document](./docs/ba_document.md) — Requirements, features, rules, NFRs (Vietnamese)
- [📋 Use Cases](./docs/use_cases.md) — Detailed flows UC01–UC14 (Vietnamese)
- [🏛️ System Architecture](./docs/system_architecture.md) — Architecture and design decisions (Vietnamese)
- [🗄️ Database Schema](./docs/database_schema.md) — SQLite schema, indexes, retention (Vietnamese)
- [📝 Changelog](./CHANGELOG.md)
- [🤝 Contributing](./CONTRIBUTING.md)
- [🔒 Security](./SECURITY.md)

---

## 🗺️ Roadmap

- [x] Re-define product and scope
- [x] Rewrite BA, Use Cases, Architecture, Database Schema
- [ ] Phase 0: Camera + blocking + tamper-resistance spikes
- [ ] Phase 1: Clean up legacy code, rename to EduFocus, new schema
- [ ] Phase 2: PIN, Parent Mode, protection toggle
- [ ] Phase 3: Chatbot blocking engine + block page
- [ ] Phase 4: Study sessions + activity tracking
- [ ] Phase 5: Attention monitoring
- [ ] Phase 6: Dashboard, reports, settings
- [ ] Phase 7: Hardening, installer, CI, MVP release

---

## ⚠️ Known Limitations

- The PIN is not a security boundary if the student shares the Windows account or has Administrator rights.
- The hosts file can be bypassed via DoH/DoT, VPN, or a proxy.
- Hosts does not log HTTPS access; blocked-attempt counts are best-effort only.
- The block page only appears over HTTP; HTTPS usually shows a connection/SSL error.
- App blocking may close/kill a process and lose unsaved data; EduFocus sends `WM_CLOSE` before killing.
- URL/path, wildcard subdomains, and AI sidebars are not in the MVP; they require a browser extension.
- Detailed reports are limited to 30 days; long-range data uses session summaries (365 days).
- Webcam-based attention is an estimate and must not be treated as certain.
- Google Meet/Teams may hold the camera on machines without Multi-App Camera support.
- New or embedded chatbots cannot all be detected.
- The process watcher relies on process name/path; renamed or portable apps may evade detection.
- The app cannot resist a user with SYSTEM/admin privileges; Windows shutdown/logoff remains a bypass.
- A scheduled-task watchdog may take 30–60 seconds to restart; uninstall PIN is best-effort.

EduFocus aims to **support study habits**, not to enable covert surveillance or exam cheating.

---

## 🤝 Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](./CONTRIBUTING.md) before opening a Pull Request.

---

## 📜 License

Distributed under the [MIT License](./LICENSE).

© 2026 ThanhTrunggDEV. EduFocus is built on the MIT-licensed Don't Be Lazy project.

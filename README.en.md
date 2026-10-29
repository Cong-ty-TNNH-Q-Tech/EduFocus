<div align="center">

# 🎓 EduFocus

**Responsible study monitoring — protecting your child's focus on the computer.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![CI](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/actions/workflows/ci.yml/badge.svg)](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/actions/workflows/ci.yml)
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
| 📋 **Flexible blocklist** | Add custom domains/apps, temporary allowlists for permitted homework. Precise URL/path rules through the Chrome/Edge extension |
| 🕒 **Protection schedule** | Automatically enable/disable protection by weekly study hours |
| 📊 **Study session tracking** | Records study time, active apps/domains (best-effort), and observed blocked attempts |
| 👁️ **Attention estimate** | Uses the webcam to detect presence, screen-facing gaze, absence, or drowsiness |
| 🖥️ **Online class detection** | Separate tracking for Google Meet, Microsoft Teams, and Zoom |
| 🔒 **Local-first** | No cloud, no account, data stays on the device |
| 🛡️ **Basic tamper resistance** | Tray hiding, PIN-protected exit, auto-restart (~60s), crash recovery |
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

**Transparency records:**

- [Privacy Notice](./docs/privacy-notice.md) — data, purposes, storage, retention, parent/student
  rights and contact channel. It is shown in the installer and can be reopened from onboarding or
  Parent Mode before monitoring is enabled (Vietnamese).
- [DPIA](./docs/dpia.md) — data map, risk assessment, data-request process (acknowledged within
  72 business hours, completed within 30 days) and incident response (Vietnamese).
- [Third-party notices](./THIRD-PARTY-NOTICES.md) — license inventory for every library shipped in
  the MSI/ZIP.

**Deleting data:** Parent Mode → "Dữ liệu & quyền riêng tư" → delete all observation data with the
PIN; the uninstaller can also remove `%ProgramData%\EduFocus`. Details and response deadlines are
in [privacy notice §10](./docs/privacy-notice.md#10-yêu-cầu-xóa-dữ-liệu-và-thời-hạn-phản-hồi)
(Vietnamese).

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
- 🌐 Chrome/Edge/Firefox for domain blocking via hosts; Chrome/Edge 116+ extension adds URL/path blocking; block page over HTTP only
- 🎥 Built-in or external webcam (optional, for attention)

---

## 🏗️ Tech Stack

| Layer | Technology |
|---|---|
| **UI** | WPF .NET 10 LTS, CommunityToolkit.Mvvm (new UI in Phase 6) |
| **Architecture** | Clean Architecture + Ports & Adapters |
| **Database** | SQLite + EF Core 10 |
| **Blocking** | Windows hosts file (exact domains) + local HTTP block page + MV3 extension (URL/path) |
| **Activity** | Win32 foreground window, WMI, UI Automation |
| **Attention** | OpenCvSharp4 + ONNX Runtime (MediaPipe Face/Iris) |
| **Security** | PBKDF2-SHA256; no other secrets stored |
| **Installer** | WiX Toolset v4 (MSI) |
| **CI/CD** | GitHub Actions |

---

## 🚀 Install & Run

### Release

> EduFocus is being migrated from the predecessor project **Don't Be Lazy**. The MVP release is planned after Phase 7; features described here are not released yet.

Once released, download from [Releases](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/releases). Each release ships an MSI, a portable ZIP, a browser extension ZIP, an install guide and `SHA256SUMS.txt`; verify SHA-256 (and the code signature when present) before installing. Requirements: Windows 10 22H2/Windows 11 x64; the MVP requires Administrator. See [docs/installer.md](./docs/installer.md) and [docs/release-process.md](./docs/release-process.md) for details.

### Run from source

**Current requirements:** .NET 10 SDK, Windows 11 23H2+ 64-bit (Windows 10 22H2 best-effort).

```bash
git clone https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus.git
cd EduFocus

dotnet restore EduFocus.slnx
dotnet run --project src/EduFocus.WPF/EduFocus.WPF.csproj
```

Run tests:

```bash
dotnet test EduFocus.slnx --configuration Release
```

> The entire solution targets .NET 10 LTS and EF Core 10; Windows 11 23H2+ is the primary platform, Windows 10 22H2 is best-effort.
>
> The 1.x codebase has been removed (issue #7). The app currently starts into a minimal
> foundation shell; MVP features will land phase by phase. See [legacy-cleanup.md](./docs/legacy-cleanup.md).

---

## 📚 Documentation

- [📄 BA Document](./docs/ba_document.md) — Requirements, features, rules, NFRs (Vietnamese)
- [📋 Use Cases](./docs/use_cases.md) — Detailed flows UC01–UC14 (Vietnamese)
- [🏛️ System Architecture](./docs/system_architecture.md) — Architecture and design decisions (Vietnamese)
- [🗄️ Database Schema](./docs/database_schema.md) — SQLite schema, indexes, retention (Vietnamese)
- [🧩 Domain Invariants](./docs/domain-invariants.md) — Entity invariants and domain/DB/use-case boundaries (Vietnamese)
- [📝 Changelog](./CHANGELOG.md)
- [🤝 Contributing](./CONTRIBUTING.md)
- [🔒 Security](./SECURITY.md)
- [🌐 Browser Extension](./docs/browser-extension.md) — URL/path blocking, permissions, packaging and removal (Vietnamese)
- [🔏 Privacy Notice](./docs/privacy-notice.md) — current privacy notice (Vietnamese)
- [📊 DPIA](./docs/dpia.md) — data protection impact assessment (Vietnamese)
- [⚖️ Third-Party Notices](./THIRD-PARTY-NOTICES.md) — third-party license inventory

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

## 🔍 Logs & troubleshooting

- Technical logs: `%LOCALAPPDATA%\EduFocus\logs\edufocus-YYYYMMDD.log` — at most 10 MB per file, kept for 14 days.
- Every line has a UTC timestamp, level, category and correlation id; PINs, recovery codes, tokens and full URLs are redacted before writing.
- When reporting a bug, attach the log files around the failure plus the EduFocus version, Windows version and reproduction steps. Never attach the database or webcam images/video.

---

## ⚠️ Known Limitations

- The PIN is not a security boundary if the student shares the Windows account or has Administrator rights. The data-folder ACL only separates access when parent and student use separate Windows accounts; the PIN protects the UI, not the database file.
- The hosts file can be bypassed via DoH/DoT, VPN, or a proxy.
- Hosts does not log HTTPS access; blocked-attempt counts are best-effort only.
- The block page only appears over HTTP; HTTPS usually shows a connection/SSL error.
- App blocking may close/kill a process and lose unsaved data; EduFocus sends `WM_CLOSE` before killing.
- The Chrome/Edge extension blocks top-level URL/path navigations; Firefox, iframes/subresources and
  wildcard subdomains are not covered. A user with administrator rights can disable the extension.
- Detailed reports are limited to 30 days; long-range data uses session summaries (365 days).
- Webcam-based attention is an estimate and must not be treated as certain.
- Google Meet/Teams may hold the camera on machines without Multi-App Camera support.
- New or embedded chatbots cannot all be detected.
- The process watcher relies on process name/path; renamed or portable apps may evade detection.
- The app cannot resist a user with SYSTEM/admin privileges; Windows shutdown/logoff remains a bypass.
- A resident watchdog detects kills/hangs and restarts the app within about 30 seconds; if the watchdog itself is killed, a scheduled task restores it within about 60 seconds. Uninstall PIN is best-effort.

EduFocus aims to **support study habits**, not to enable covert surveillance or exam cheating.

---

## 🤝 Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](./CONTRIBUTING.md) before opening a Pull Request.

---

## 📜 License

Distributed under the [MIT License](./LICENSE). The licenses of bundled third-party libraries are
listed in [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).

© 2026 ThanhTrunggDEV. EduFocus is built on the MIT-licensed Don't Be Lazy project.

# 🤝 Đóng góp cho EduFocus

Cảm ơn bạn đã quan tâm đến EduFocus! Mọi đóng góp đều được chào đón — báo lỗi, đề xuất tính năng, cải thiện tài liệu hoặc gửi Pull Request.

Trước khi bắt đầu, vui lòng đọc:

- [README](./README.md)
- [BA Document](./docs/ba_document.md)
- [System Architecture](./docs/system_architecture.md)
- [Code of Conduct](./CODE_OF_CONDUCT.md)
- [Security Policy](./SECURITY.md)

> **Trạng thái dự án:** EduFocus đang chuyển đổi từ dự án tiền nhiệm **Don't Be Lazy**. Codebase đã dùng tên project `EduFocus.*` từ Phase 1 và code 1.x đã được xóa ở issue #7 ([inventory](./docs/legacy-cleanup.md)).

---

## 📌 Quy trình đóng góp

### 1. Fork & Clone

```bash
git clone https://github.com/<your-username>/EduFocus.git
cd EduFocus
```

### 2. Tạo branch mới

Không làm việc trực tiếp trên `master`:

```bash
git checkout -b feat/ten-tinh-nang
# hoặc
git checkout -b fix/mo-ta-loi
```

### 3. Cài đặt môi trường

**Yêu cầu**

- Windows 11 23H2+ (64-bit) — chính; Windows 10 22H2 chỉ best-effort
- .NET 10 SDK (toàn bộ project target `net10.0`/`net10.0-windows`)
- Visual Studio 2022 (hoặc Rider) với workload **.NET desktop development**
- SQLite tooling (tùy chọn, để xem DB)

```bash
dotnet restore EduFocus.slnx
dotnet build EduFocus.slnx
```

Chạy app:

```bash
dotnet run --project src/EduFocus.WPF/EduFocus.WPF.csproj
```

> Một số tính năng (ghi hosts, kill process) cần chạy **as Administrator**. Khi debug, hãy chạy Visual Studio với quyền admin nếu cần kiểm thử blocking.

Chạy test:

```bash
dotnet test EduFocus.slnx --configuration Release
```

> Toàn bộ solution target .NET 10 LTS và EF Core 10. Chạy `dotnet tool restore` để cài đúng `dotnet-ef` 10.0.14 trước khi làm việc với migration.

### CI trên GitHub Actions

[`.github/workflows/ci.yml`](./.github/workflows/ci.yml) chạy trên mọi PR và push vào `master`, job **EduFocus — Build & Test**:

```bash
dotnet tool restore
dotnet restore EduFocus.slnx
dotnet build EduFocus.slnx --configuration Release --no-restore
dotnet test EduFocus.slnx --configuration Release --no-build --logger trx
dotnet publish src/EduFocus.WPF/EduFocus.WPF.csproj --configuration Release --output artifacts/publish
```

- NuGet được cache theo `**/*.csproj` và `.config/dotnet-tools.json`; không cache DB, log hay secret.
- Kết quả test (TRX) và bản publish framework-dependent được upload làm artifact `edufocus-test-results` / `edufocus-ci-publish`.
- Repo **chưa bật branch protection** cho `master`. Maintainer nên bật required status check **EduFocus — Build & Test** (Settings → Branches) để PR fail CI không merge được.
- Log CI chỉ chứa thông tin build/test; không đặt token, PIN hay connection string trong workflow.

Release chạy bằng [`.github/workflows/release.yml`](./.github/workflows/release.yml) khi push tag `v*.*.*`:
build Release, chạy toàn bộ test, publish self-contained, ký exe/MSI bằng chứng chỉ trong
GitHub Secrets (nếu có), tạo MSI/ZIP + `SHA256SUMS.txt`, rồi công bố GitHub Release với
release notes lấy từ `CHANGELOG.md`. Có thể chạy **dry run** trên branch thử nghiệm bằng
`workflow_dispatch` với `dry_run = true` — artifact được upload nhưng không tạo release.
Secret chỉ được truyền vào step ký số, không được echo hay đính kèm artifact; job build chạy
với `contents: read`, chỉ job công bố có `contents: write`. Chi tiết versioning, ký số và
checklist phát hành: [docs/release-process.md](./docs/release-process.md).

### 4. Commit

Tuân thủ **Conventional Commits**:

| Prefix | Ý nghĩa |
|---|---|
| `feat:` | Thêm tính năng |
| `fix:` | Sửa lỗi |
| `docs:` | Tài liệu |
| `refactor:` | Tái cấu trúc, không đổi hành vi |
| `perf:` | Cải thiện hiệu năng |
| `test:` | Thêm/cập nhật test |
| `chore:` | Config, dependency, CI |
| `security:` | Vá bảo mật |

Ví dụ:

```bash
git commit -m "feat(protection): thêm allowlist tạm thời cho domain bài tập"
```

### 5. Push & Pull Request

```bash
git push origin feat/ten-tinh-nang
```

Mở PR về `master`. Mô tả rõ:

- Vấn đề / mục tiêu.
- Cách giải quyết.
- Ảnh hưởng đến dữ liệu, quyền riêng tư hoặc quyền hệ thống.
- Cách đã kiểm thử.
- Ảnh chụp UI nếu có thay đổi giao diện.

---

## 🧱 Quy tắc kiến trúc

EduFocus theo **Clean Architecture + Ports & Adapters**. Vui lòng giữ đúng hướng phụ thuộc:

```text
WPF → UseCases → Ports ← Infrastructure
                    ↑
                 Domain
```

- `Domain` không tham chiếu project khác, không dùng WPF/SQLite/Win32.
- `Ports` chỉ chứa interface + DTO, không chứa implementation.
- `UseCases` chỉ gọi qua Ports, không gọi trực tiếp `AppDbContext` hay P/Invoke.
- `Infrastructure` triển khai Ports; không chứa business rule.
- `WPF` chỉ gọi Inbound Ports; ViewModel không truy cập DB/Win32 trực tiếp.

**Khi thêm tính năng mới**

1. Xác định entity/value object trong `Domain`.
2. Định nghĩa Port (nếu cần).
3. Viết UseCase + unit test.
4. Triển khai adapter ở `Infrastructure`/`SqliteDataAccess`.
5. Đăng ký DI và làm UI.
6. Cập nhật docs/CHANGELOG nếu hành vi thay đổi.

---

## 🔒 Quy tắc quyền riêng tư & an toàn

Mọi PR có tương tác với webcam, dữ liệu học sinh hoặc quyền hệ thống phải tuân thủ:

- **Không lưu ảnh, video, audio, frame, face embedding.**
- **Không gửi dữ liệu ra network** (trừ update check đã được duyệt).
- **Không log PIN, recovery code, token, URL đầy đủ có query nhạy cảm.**
- **Log qua `IAppLogger` kèm category; mọi dữ liệu bên ngoài (message, exception) phải đi qua `LogSanitizer` và không ghi toàn bộ window title/nội dung trang.**
- **Không thêm telemetry/analytics** khi chưa có quyết định rõ ràng.
- **Không thêm hành vi giám sát bí mật** (keylogger, screenshot, clipboard, đọc tin nhắn).
- Thay đổi hosts phải giữ nguyên các dòng không thuộc EduFocus và có cơ chế self-heal; chỉ chặn domain exact trong MVP.
- Block page phải bind loopback, là HTML tĩnh, escape output và không tải tài nguyên ngoài.
- Không kill process hệ thống hoặc process của EduFocus.
- Không hứa hẹn log đầy đủ HTTPS hoặc chặn URL/path khi chưa có browser extension.
- Mọi thay đổi quyền riêng tư phải cập nhật `docs/ba_document.md`, `SECURITY.md` và `CHANGELOG.md`.

PR vi phạm các quy tắc trên sẽ bị từ chối.

---

## 🧪 Kiểm thử

### Bắt buộc

- [ ] `dotnet build` thành công.
- [ ] Unit test liên quan pass.
- [ ] Không có warning mới nghiêm trọng.
- [ ] Đã test thủ công luồng chính.

### Nên có

- Test cho rule matching, PIN lockout, retention, session aggregation.
- Test SQLite repository khi đổi schema.
- Test blocking trên ít nhất Chrome/Edge; phân biệt HTTP block page và HTTPS báo lỗi.
- Test khi không có quyền admin (phải báo lỗi rõ, không crash).
- Test session crash/recovery, shared PIN/recovery lockout và coverage khi camera bận.

### Không commit

- File DB (`*.db`, `*.db-shm`, `*.db-wal`).
- Log, ảnh chụp webcam, dữ liệu cá nhân.
- `bin/`, `obj/`, `.vs/`, file build.
- API key, certificate, secret.

---

## 🐛 Báo cáo lỗi

Mở Issue với:

- [ ] Phiên bản EduFocus / commit.
- [ ] Windows version + kiến trúc.
- [ ] Có chạy as Administrator hay không.
- [ ] Các bước tái hiện.
- [ ] Kết quả thực tế vs kỳ vọng.
- [ ] Log liên quan (đã kiểm tra không chứa dữ liệu nhạy cảm).
- [ ] Ảnh chụp màn hình nếu lỗi UI.

**Không** đính kèm ảnh/video webcam của trẻ hoặc dữ liệu cá nhân.

---

## 💡 Đề xuất tính năng

Mở Issue với tiêu đề `[Feature Request]:` và mô tả:

- Vấn đề cần giải quyết.
- Đối tượng hưởng lợi (phụ huynh/học sinh).
- Giải pháp đề xuất.
- Tác động đến quyền riêng tư và quyền hệ thống.
- Giải pháp thay thế đã cân nhắc.

Tính năng phải phù hợp với định hướng **hỗ trợ học tập**, không phải giám sát bí mật hay chống gian lận thi cử.

---

## 📋 Checklist trước khi gửi PR

- [ ] Branch bắt đầu từ `master` mới nhất.
- [ ] Commit message theo Conventional Commits.
- [ ] Code build và test pass.
- [ ] Không có secret/file dữ liệu bị commit.
- [ ] Đã cập nhật test cho thay đổi logic.
- [ ] Đã cập nhật docs/CHANGELOG nếu cần.
- [ ] PR mô tả rõ mục đích và cách kiểm thử.
- [ ] Tuân thủ quy tắc quyền riêng tư ở trên.

---

## 📜 Giấy phép đóng góp

Khi gửi PR, bạn đồng ý rằng đóng góp của bạn được cấp phép theo [MIT License](./LICENSE) của dự án.

---

Cảm ơn vì đã giúp EduFocus tốt hơn! 🎓

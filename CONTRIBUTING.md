# 🤝 Đóng góp cho EduFocus

Cảm ơn bạn đã quan tâm đến EduFocus! Mọi đóng góp đều được chào đón — báo lỗi, đề xuất tính năng, cải thiện tài liệu hoặc gửi Pull Request.

Trước khi bắt đầu, vui lòng đọc:

- [README](./README.md)
- [BA Document](./docs/ba_document.md)
- [System Architecture](./docs/system_architecture.md)
- [Code of Conduct](./CODE_OF_CONDUCT.md)
- [Security Policy](./SECURITY.md)

> **Trạng thái dự án:** EduFocus đang chuyển đổi từ dự án tiền nhiệm **Don't Be Lazy**. Project, namespace và assembly đã dùng tiền tố `EduFocus.*`.

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
- .NET 10 SDK (all solution projects target .NET 10)
- Visual Studio 2022 (hoặc Rider) với workload **.NET desktop development**
- SQLite tooling (tùy chọn, để xem DB)

```bash
dotnet tool restore
dotnet restore EduFocus.slnx
dotnet build EduFocus.slnx --configuration Release
dotnet test EduFocus.slnx --configuration Release
```

Chạy app:

```bash
dotnet run --project src/EduFocus.WPF/EduFocus.WPF.csproj
```

> App yêu cầu Administrator approval để thiết lập ACL cho database, backup và log trong `%ProgramData%\EduFocus`.

CI GitHub Actions dùng Windows runner và cùng chuỗi lệnh Release ở trên. Nó tạo artifact WPF `EduFocus-CI-*` lưu 7 ngày; cache chỉ chứa NuGet packages, không chứa database/log.

Khi thay đổi CI, giữ runner Windows, SDK .NET 10 và test toàn solution (`EduFocus.slnx`) để integration tests SQLite cũng chạy.

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
4. Triển khai adapter ở `Infrastructure` hoặc `Infrastructure.Persistence`.
5. Đăng ký DI và làm UI.
6. Cập nhật docs/CHANGELOG nếu hành vi thay đổi.

---

## 🔒 Quy tắc quyền riêng tư & an toàn

Mọi PR có tương tác với webcam, dữ liệu học sinh hoặc quyền hệ thống phải tuân thủ:

- **Không lưu ảnh, video, audio, frame, face embedding.**
- **Không gửi dữ liệu ra network** (trừ update check đã được duyệt).
- **Không log PIN, recovery code, token, URL đầy đủ có query nhạy cảm.**
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

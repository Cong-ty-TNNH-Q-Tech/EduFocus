# Hosts blocking spike

Prototype Phase 0 cho issue [#2](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/2): kiểm chứng chặn website bằng Windows hosts + block page HTTP loopback.

## Phạm vi

- Soạn block section trong hosts file với marker riêng, chỉ dùng IPv4 `127.0.0.1`, idempotent.
- Backup + journal trước khi sửa; restore đúng byte gốc; self-heal marker mồ côi hoặc crash giữa chừng.
- Block page HTTP trên `127.0.0.1:80`: kiểm tra Host header, escape HTML, không tải tài nguyên ngoài.
- Đo hành vi thực tế trên Chrome, Edge, Firefox (HTTP và HTTPS), kèm request log và User-Agent.
- Kiểm tra port 80 bị chiếm, DNS flush và kết quả resolve.

Không nằm trong phạm vi: wildcard/URL/path, browser extension, DoH/VPN bypass, chặn HTTPS.

## Vị trí trong kiến trúc

- `tools/HostsBlockingSpike/`: harness dùng một lần, **không** nằm trong `EduFocus.slnx`.
- Đề xuất Phase 3: chuyển logic an toàn sang `HostsFileBlocker` (Infrastructure, implement port `IWebsiteBlocker`) và `BlockPageServer`; UseCase không thao tác file trực tiếp.
- Kết quả đo và kết luận: [`docs/spikes/hosts-blocking-evaluation.md`](../../docs/spikes/hosts-blocking-evaluation.md).

## Cảnh báo an toàn

- Mặc định công cụ **không** chạm vào hosts thật. Chỉ self-test trong thư mục tạm và block page trên cổng tạm.
- `--apply-hosts` sửa hosts thật và **bắt buộc chạy elevated**. Trước khi sửa, công cụ tạo backup + journal dưới `%ProgramData%\EduFocus\spike-backup`.
- Block section chỉ nằm giữa `### EDUFOCUS BLOCK START ###` và `### EDUFOCUS BLOCK END ###`; các dòng khác không bị sửa.
- Restore chạy trong `finally` và có xử lý `Ctrl+C`/process exit. Nếu tiến trình bị kill cứng, lần chạy sau sẽ tự recovery từ journal hoặc xóa marker mồ côi.
- Nếu hosts file bị ứng dụng khác sửa trong lúc chạy, restore chỉ xóa block EduFocus và giữ chỉnh sửa bên ngoài.
- Chỉ dùng domain test (`example.com`, `www.example.com`) trên máy có kết nối mạng bình thường; không dùng domain thật của người dùng.

## Yêu cầu

- Windows 10/11, .NET SDK 9 hoặc mới hơn.
- Chrome/Edge/Firefox cài ở đường dẫn chuẩn (không bắt buộc; thiếu trình duyệt nào thì bỏ qua trình duyệt đó).
- Quyền administrator cho `--apply-hosts`; cổng 80 phải trống để kiểm tra block page thật.

## Chạy

Từ thư mục gốc repo:

```powershell
# Sandbox + loopback, không sửa hosts (an toàn, không cần admin)
dotnet run --project tools/HostsBlockingSpike -c Release

# Kiểm chứng hosts thật + trình duyệt (mở PowerShell as Administrator)
dotnet run --project tools/HostsBlockingSpike -c Release -- --apply-hosts

# Chỉ định domain/cổng khác
dotnet run --project tools/HostsBlockingSpike -c Release -- --apply-hosts `
  --domains example.com,www.example.com --port 80
```

Kết quả ghi vào `docs/spikes/results/`:

- `hosts-<timestamp>.md`: báo cáo đọc được.
- `hosts-<timestamp>.json`: toàn bộ dữ liệu thô.
- `hosts-<timestamp>.log`: log tiến trình (gitignored).
- `firefox-http-*.png`: ảnh chụp block page từ Firefox.

## Tham số

| Tham số | Mặc định | Ghi chú |
|---|---|---|
| `--apply-hosts` | tắt | Sửa hosts thật; yêu cầu elevated |
| `--hosts` | `%SystemRoot%\System32\drivers\etc\hosts` | Đường dẫn hosts file |
| `--backup-dir` | `%ProgramData%\EduFocus\spike-backup` | Nơi lưu backup + journal |
| `--output-dir` | `docs/spikes/results` | Thư mục report |
| `--domains` | `example.com,www.example.com` | Danh sách domain cách nhau dấu phẩy |
| `--port` | `80` | Cổng block page |
| `--browser-timeout-seconds` | `45` | Timeout mỗi lần chạy trình duyệt (Firefox HTTPS bị giới hạn 20 giây) |
| `--skip-browsers` | tắt | Bỏ ma trận trình duyệt |
| `--skip-self-tests` | tắt | Bỏ self-test sandbox |

## Cấu trúc

- `Hosts/HostsFileManager.cs`: apply/restore/recovery, marker, backup, journal, retry khi file bị khóa.
- `Hosts/HostsSelfTests.cs`: 7 test sandbox (idempotency, restore exact, external edit, orphan marker, crash journal, domain invalid).
- `BlockPage/BlockPageServer.cs`: TCP listener loopback, parse request, Host header normalization, HTML tĩnh.
- `Probes/NetworkProbe.cs`: flush DNS, resolve, HTTP/HTTPS direct probe, phát hiện proxy.
- `Probes/BrowserProbe.cs`: tìm Chrome/Edge/Firefox, chạy headless, chụp screenshot Firefox, gán request theo UA.
- `Reporting/`: model report + xuất Markdown/JSON/log.

## Giới hạn đã biết

- Hosts chỉ chặn domain exact; không phân biệt path/URL.
- Block page chỉ phục vụ HTTP; HTTPS báo lỗi kết nối/SSL và không được ghi nhận.
- DoH/DoT, VPN, proxy hoặc browser extension có thể bypass hosts.
- Người dùng admin có thể sửa hosts hoặc dừng tiến trình.
- Chrome/Edge headless trong phiên elevated có thể không trả DOM; bằng chứng dựa trên request log + UA.
- Firefox trả prompt trước khi ghi screenshot; harness chờ file xuất hiện để tránh gán nhầm request.
- Chỉ kiểm chứng IPv4 loopback; IPv6/AAAA chưa được đánh giá.

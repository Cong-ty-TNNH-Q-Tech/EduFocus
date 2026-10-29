# Đánh giá hosts file & block page Phase 0

- **Issue:** [#2 - Spike: Kiểm chứng chặn web bằng hosts file + block page](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/2)
- **Ngày đo:** 2026-10-29
- **Trạng thái:** Spike hoàn tất; đề xuất chờ review trước khi chốt adapter Phase 3
- **Phạm vi:** chặn domain exact bằng Windows hosts + block page HTTP loopback; an toàn apply/restore; hành vi trên Chrome, Edge, Firefox; giới hạn HTTPS và khi cổng 80 bị chiếm.

## 1. Kết luận

1. **Khả thi trên cả ba trình duyệt.** Hosts trỏ domain về `127.0.0.1` + block page trên cổng 80 chặn được HTTP trên Chrome 156, Edge 156 và Firefox 158. Chrome/Edge được xác nhận qua request log kèm User-Agent; Firefox được xác nhận qua ảnh chụp block page và request log.
2. **Apply/restore an toàn.** 7/7 self-test sandbox đạt; apply idempotent; restore trả hosts về đúng từng byte (`CAA4AA32…` trước và sau trùng nhau); chỉnh sửa bên ngoài marker được giữ lại; journal + orphan self-heal xử lý được crash giữa chừng.
3. **Cổng 80 bị chiếm không làm hỏng protection.** Bind thất bại với `SocketException`, hosts vẫn được áp dụng, và server bind lại thành công ngay khi cổng được nhả. Vì vậy block page phải được coi là thành phần "best-effort": protection vẫn ON nhưng UI phải cảnh báo block page không khả dụng.
4. **HTTPS không hiển thị block page.** Direct probe nhận `HttpRequestException` sau ~2 giây; Firefox HTTPS treo tới timeout 20 giây. Hosts không tạo log cho HTTPS, nên không thể đếm đầy đủ blocked attempts. Giới hạn này phải hiển thị trong UI và tài liệu.
5. **Chặn theo domain exact, không theo path.** Mọi path/query của domain bị chặn đều nhận block page HTTP 200. `Host` header có port hoặc trailing dot vẫn khớp; host không nằm trong danh sách nhận 404. HTML escape đúng và trang không tải tài nguyên ngoài.
6. **Đề xuất Phase 3:** tách `HostsFileBlocker` (port + adapter) và `BlockPageServer` như kiến trúc hiện tại; backup/journal dưới `%ProgramData%\EduFocus`; self-heal lúc khởi động; khởi động block page trước khi áp hosts; trạng thái cảnh báo khi cổng 80 bận hoặc khi HTTPS/DoH/proxy có thể bypass.

## 2. Cách đo

### 2.1. Môi trường

| Hạng mục | Giá trị |
|---|---|
| OS | Windows 10.0.19045 (x64) |
| Runtime | .NET 9.0.15 |
| Quyền chạy | Administrator (UAC elevated) |
| Chrome | 156.0.8078.73 |
| Edge | 156.0.4314.50 |
| Firefox | 158.0.1 |
| Proxy hệ thống | Không (`usesProxy=False`) |
| Hosts file | `C:\Windows\System32\drivers\etc\hosts` |
| Backup/journal | `C:\ProgramData\EduFocus\spike-backup` |
| Domain thử | `example.com`, `www.example.com` |
| Cổng block page | `127.0.0.1:80` |

### 2.2. Cấu hình

- Hosts block gồm 4 dòng (2 domain + 2 marker) dưới dạng IPv4, đặt giữa marker `### EDUFOCUS BLOCK START/END ###`:

  ```text
  ### EDUFOCUS BLOCK START ###
  127.0.0.1 example.com
  127.0.0.1 www.example.com
  ### EDUFOCUS BLOCK END ###
  ```
- Trước khi sửa hosts: kiểm tra recovery, lưu SHA-256 gốc, tạo backup + journal.
- Sau khi sửa: flush DNS (`ipconfig /flushdns`), resolve lại domain, kiểm tra xung đột cổng 80, khởi động block page server, chạy probe trực tiếp rồi mới chạy trình duyệt.
- Trình duyệt chạy headless: Chrome/Edge dùng `--headless=new --dump-dom`; Firefox dùng `--screenshot`. Mỗi lần chạy cách nhau 2,5 giây; request được gán cho trình duyệt theo User-Agent.
- Firefox trả prompt ngay trong khi tiến trình nền còn tải trang, nên harness chờ file screenshot xuất hiện (tối đa 15 giây) trước khi chuyển sang lần chạy kế tiếp.
- Restore nằm trong `finally`, kể cả khi có lỗi; sau restore kiểm tra lại SHA-256 và probe HTTP.

### 2.3. Các lần chạy

| Run | Chế độ | Mục đích | File |
|---|---|---|---|
| Sandbox + loopback | Không sửa hosts | Kiểm tra self-test và block page trên cổng tạm | `results/hosts-20261029-193836.md` / `.json` |
| Chính | Elevated, hosts thật, 3 trình duyệt | Kết quả dùng cho đánh giá | `results/hosts-20261029-193548.md` / `.json` |

Các run 19:27–19:33 được dùng để sửa lỗi harness (thứ tự start server, gán request theo User-Agent, Firefox ghi screenshot bất đồng bộ) và không dùng làm kết quả. Run chính chạy lại từ đầu sau khi sửa.

## 3. Kết quả

### 3.1. Self-test hosts (sandbox)

| Test | Kết quả | Bằng chứng |
|---|---|---|
| Apply lần đầu + idempotency | pass | Lần 2 `changed=False`, nội dung giống hệt |
| Restore đúng từng byte | pass | `bytesEqual=True` |
| Restore khi file gốc không có newline cuối | pass | `bytesEqual=True` |
| Giữ chỉnh sửa bên ngoài marker | pass | `exact=False`, `editKept=True` |
| Self-heal marker mồ côi | pass | Marker bị xóa, không cần journal |
| Recovery sau crash bằng journal | pass | `fromJournal=True`, bytes khớp, journal bị xóa |
| Domain không hợp lệ bị từ chối | pass | 5/5 bị từ chối, hosts không đổi |

### 3.2. Block page loopback

| Kiểm tra | Kết quả |
|---|---|
| Host bị chặn (`example.com`) | HTTP 200, có marker |
| Host kèm port (`example.com:1234`) | HTTP 200, có marker |
| Host có trailing dot (`example.com.`) | HTTP 200, có marker |
| Path/query bất kỳ (`/some/path?q=1`) | HTTP 200, có marker |
| Biến thể `www` | HTTP 200, có marker |
| Host không bị chặn | HTTP 404, không có marker |
| HTML escape (`<script>…`) | Escape đúng, không tài nguyên ngoài |

Tổng: 7/7 đạt. Thời gian phản hồi loopback 0–34 ms.

### 3.3. Hosts thật: apply / restore

| Hạng mục | Giá trị |
|---|---|
| Block lines | 4 |
| SHA-256 trước | `CAA4AA3216BDC45355940D65B34E2869F69331AB4FB35AC112CD3BC24C483024` |
| SHA-256 sau apply | `BA6E77E074C2FBAF3BE6505DC77C69308E744F8713B54FA2FDDD78FE836A45EB` |
| SHA-256 sau restore | `CAA4AA3216BDC45355940D65B34E2869F69331AB4FB35AC112CD3BC24C483024` |
| Restore xóa block | True |
| Restore exact | True |
| Hosts sạch sau khi kết thúc | Có (không còn marker EduFocus) |

Backup của run chính: `C:\ProgramData\EduFocus\spike-backup\hosts-20261029-123548-399.bak` (giữ lại ngoài repo; journal đã xóa sau restore).

### 3.4. Cổng 80 bị chiếm

| Hạng mục | Kết quả |
|---|---|
| Phát hiện xung đột | Có |
| Lỗi | `SocketException`: "Only one usage of each socket address …" |
| Bind lại sau khi nhả cổng | Thành công |
| Ảnh hưởng hosts | Không; hosts vẫn áp dụng và restore bình thường |

### 3.5. DNS và probe trực tiếp

| URL | Mode | Kết quả | Block page | Ghi chú |
|---|---|---|---|---|
| `http://example.com/` | GET direct | 200 | Có | 1 ms |
| `http://example.com/some/path?q=1` | GET direct | 200 | Có | Path không được enforce riêng |
| `https://example.com/` | HTTPS | Lỗi `HttpRequestException` | Không | ~2,0 giây |
| `http://example.com/` sau restore | GET direct | 200 | Không | Trang thật truy cập lại được |

DNS sau flush: `example.com` và `www.example.com` đều resolve về `127.0.0.1` (5 ms và 0 ms). Flush DNS mất 42 ms.

### 3.6. Trình duyệt

| Trình duyệt | Mode | Exit | Timeout | DOM marker | Server hit | UA khớp | Thời gian |
|---|---|---|---|---|---|---|---|
| Chrome | http | 0 | Không | False | Có | `HeadlessChrome/156.0.0.0` | 133 ms |
| Chrome | https | 0 | Không | False | Không | - | 75 ms |
| Edge | http | 0 | Không | False | Có | `HeadlessChrome/156.0.0.0 … Edg/156.0.0.0` | 102 ms |
| Edge | https | 0 | Không | False | Không | - | 61 ms |
| Firefox | http | 0 | Không | n/a | Có | `Firefox/158.0` | 3.164 ms (chờ screenshot) |
| Firefox | https | -1 | Có (20 s) | n/a | Không | - | 20.052 ms |

Server nhận request theo thứ tự: Chrome `GET /` + `/favicon.ico` lúc 12:35:51 UTC; Edge tương tự lúc 12:35:56 UTC; Firefox 2 request `GET /` lúc 12:36:03 UTC.

**Bằng chứng hình ảnh:** `results/firefox-http-f88c09e9c9cf4d568abc0615ee1d8de1.png` (1280×720, 18.346 bytes, SHA-256 `0C67508C…`). Ảnh hiển thị đúng trang chặn EduFocus với tên miền `example.com`. Hai ảnh Firefox từ các run trước có cùng SHA-256, cho thấy kết quả render ổn định.

Chrome/Edge headless trong phiên elevated không trả DOM (exit 0 quá nhanh), nên cột `DOM marker` không dùng làm bằng chứng. Kết luận dựa trên request log + User-Agent và block page đã kiểm chứng bằng loopback/screenshot.

### 3.7. Kiểm tra an toàn sau run

- Hosts SHA-256 sau restore trùng khớp trước khi chạy.
- Không còn marker EduFocus trong hosts.
- Không có proxy hệ thống can thiệp.
- Block page không tham chiếu tài nguyên ngoài; host header được HTML-encode.
- Trang chặn không ghi log URL đầy đủ; log server chỉ giữ host/path/UA phục vụ spike.

## 4. Giới hạn và rủi ro

- **Chỉ domain exact:** không hỗ trợ wildcard/suffix, URL hay path. Mọi path của domain bị chặn đều hiển thị cùng một trang.
- **Chỉ HTTP:** HTTPS không hiển thị block page mà báo lỗi kết nối/SSL; Firefox có thể treo tới timeout. Không thể ghi nhận đầy đủ truy cập HTTPS.
- **Bypass:** DoH/DoT, VPN, proxy, đổi DNS hoặc browser extension có thể bỏ qua hosts. Chưa kiểm chứng các đường bypass này trong spike.
- **Quyền admin:** người dùng admin có thể sửa hosts hoặc tắt tiến trình. Self-heal/journal chỉ giảm thiểu, không chống admin.
- **IPv6 chưa kiểm chứng:** block entry chỉ dùng IPv4 `127.0.0.1` và server chỉ bind loopback IPv4. Nếu môi trường ưu tiên AAAA, cần bổ sung `::1` và listener dual-stack (đề xuất kiểm tra ở Phase 3).
- **Giới hạn harness:** chỉ chạy headless trên một máy; Chrome/Edge không trả DOM trong phiên elevated; Firefox ghi screenshot bất đồng bộ. Chưa kiểm chứng cửa sổ trình duyệt thật, profile có extension hoặc chế độ người dùng thường.
- **Cổng 80 dùng chung:** ứng dụng khác chiếm cổng 80 sẽ làm block page không khả dụng dù hosts vẫn chặn. Cần trạng thái cảnh báo rõ trong UI.

## 5. Đề xuất cho Phase 3

### 5.1. Cấu trúc adapter

```csharp
public interface IWebsiteBlocker
{
    Task<BlockApplyResult> ApplyAsync(IReadOnlyCollection<string> domains, CancellationToken ct);
    Task<BlockRestoreResult> RestoreAsync(CancellationToken ct);
    Task<BlockRecoveryResult> SelfHealAsync(CancellationToken ct);
}
```

- `HostsFileBlocker` (Infrastructure) triển khai port trên; không đưa logic file/hosts vào UseCase.
- `BlockPageServer` là thành phần riêng, bind `127.0.0.1:80`, chỉ phục vụ HTTP, trả HTML tĩnh; trạng thái `Unavailable` khi cổng bận.
- `ProtectionEngine` điều phối: start block page → apply hosts → flush DNS; rollback hosts nếu apply lỗi; giữ protection ON nếu chỉ block page lỗi.
- Backup + journal dưới `%ProgramData%\EduFocus`; self-heal lúc khởi động và trước mỗi lần apply.

### 5.2. Quy tắc bắt buộc

- Chỉ sửa phần giữa marker; giữ nguyên encoding, newline và các dòng khác.
- Chuẩn hóa domain: lowercase, bỏ trailing dot, validate; thêm biến thể `www.` khi phù hợp.
- Retry khi file bị antivirus khóa; báo lỗi rõ nếu không ghi được.
- Restore phải trả lại đúng byte gốc nếu không có chỉnh sửa bên ngoài; nếu có, chỉ xóa block và giữ chỉnh sửa.
- Flush DNS sau mỗi thay đổi.

### 5.3. UI và tài liệu

- Banner thường trực trong màn hình Protection: "Chặn web bằng hosts chỉ áp dụng HTTP và domain exact; HTTPS, VPN, proxy hoặc DoH có thể bỏ qua và không được ghi nhận đầy đủ."
- Cảnh báo riêng khi cổng 80 bị chiếm: "Hosts đang chặn nhưng trang chặn không hiển thị."
- Không quảng cáo chặn web "tuyệt đối"; giữ các giới hạn trong `docs/system_architecture.md` §6.4 và `docs/ba_document.md` §10.3.

### 5.4. Kiểm thử

- Self-test sandbox (idempotency, restore exact, external edit, orphan marker, crash journal, domain invalid) chạy trong CI, không cần elevated.
- Loopback block page test với Host header biến thể chạy trong CI.
- Ma trận trình duyệt + hosts thật chỉ chạy thủ công trên máy elevated, ghi report vào `docs/spikes/results/`.

## 6. Artifact

- Report chính: `results/hosts-20261029-193548.md`
- JSON: `results/hosts-20261029-193548.json`
- Log: `results/hosts-20261029-193548.log` (gitignored)
- Sandbox: `results/hosts-20261029-193836.md` / `.json`
- Screenshot Firefox: `results/firefox-http-f88c09e9c9cf4d568abc0615ee1d8de1.png`
- Công cụ: `tools/HostsBlockingSpike/` (README kèm hướng dẫn và cảnh báo an toàn)

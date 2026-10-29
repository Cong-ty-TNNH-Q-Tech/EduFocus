# Đánh giá watchdog & chống tắt Phase 0

- **Issue:** [#3 - Spike: Đánh giá watchdog và khả năng chống tắt](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/3)
- **Ngày đo:** 2026-10-29
- **Trạng thái:** Spike hoàn tất; đề xuất chờ review trước khi triển khai Phase 7
- **Phạm vi:** so sánh cơ chế watchdog, prototype restart có kiểm soát, chống trùng instance, backoff, dừng khi uninstall, phát hiện treo, đo các đường bypass; đánh giá định tính Windows Service.

## 1. Kết luận

1. **Scheduled task là lựa chọn phù hợp cho MVP.** Mô phỏng chu kỳ 60 giây phát hiện target chết và restart trong một chu kỳ (60,2 giây). Không cần thêm tiến trình watchdog thường trú trong MVP: ít thành phần bị kill hơn, không cần IPC, phù hợp với mô hình "một app elevated + scheduled task" ở `docs/system_architecture.md` §5.1.
2. **Watchdog process riêng phản ứng nhanh nhưng có điểm yếu rõ.** Restart sau kill chỉ 1,4–4,4 giây (gồm backoff), phát hiện và restart target treo sau ~4,3 giây; nhưng khi kill riêng watchdog, target vẫn chạy và **không có gì restart target sau đó** (đo được). Nếu dùng watchdog process, bắt buộc phải có lớp ngoài (scheduled task/service) để giám sát chính watchdog.
3. **Chống trùng instance và backoff hoạt động đúng.** Instance watchdog/target thứ hai thoát với mã 2/4, không ảnh hưởng instance gốc. Backoff tăng dần 1000 → 2000 → 4000 ms; restart budget dừng ở mã 3 và ghi `give_up`, tránh vòng lặp vô hạn khi app crash lặp lại.
4. **Clean exit và uninstall không bị restart.** Target thoát êm khiến watchdog dừng trong 446 ms, không restart. Dừng watchdog trong thời gian backoff cũng không tạo target mới (exit 0). Đây là hành vi bắt buộc của UC12/UC14.
5. **Phát hiện treo cần heartbeat, không chỉ kiểm tra process còn sống.** Target bị "đóng băng" vẫn giữ process; watchdog chỉ phát hiện nhờ heartbeat cũ hơn ngưỡng, sau đó kill và restart.
6. **Không thể chống admin/SYSTEM, logoff/shutdown hoặc task bị disable/xóa.** Đây là các đường bypass được chấp nhận và phải công bố trong `SECURITY.md`; không được quảng cáo "không thể tắt".
7. **Đề xuất Phase 7:** dùng scheduled task kiểm tra mỗi 1 phút + auto-start khi đăng nhập; lưu trạng thái protection/restart budget bền vững; ghi audit `WatchdogRestart`/`WatchdogGaveUp`; giữ single-instance mutex trong app. Windows Service + Session Agent để Post-MVP.

## 2. Cách đo

### 2.1. Môi trường

| Hạng mục | Giá trị |
|---|---|
| OS | Windows 10.0.19045 (x64) |
| Runtime | .NET 9.0.15 |
| User | Admin; **không elevated** |
| Thời gian chạy | 2026-10-29 19:59:36 – 20:01:06 +07:00 |
| Work root | `%TEMP%\edufocus-watchdog-spike\20261029-195936` |
| Scheduled task thật | Không đăng ký; phase tương ứng là mô phỏng |

Run chính: `docs/spikes/results/watchdog-20261029-195936.{md,json}`. Các run 19:45–19:53 dùng để sửa lỗi harness (kill cả cây tiến trình làm chết target, thứ tự ghi audit clean exit) và không dùng làm kết quả.

### 2.2. Cấu hình

| Tham số | Giá trị |
|---|---|
| Poll watchdog | 200 ms |
| Heartbeat target | 300 ms |
| Ngưỡng heartbeat cũ | 3 giây |
| Backoff | 1000 ms → tối đa 4000 ms |
| Max restart / cửa sổ | 5 / 600 giây |
| Phase budget | 2 restart, backoff cơ sở 500 ms |
| Mô phỏng scheduled task | 60 giây/lần |

### 2.3. Cách chạy

- `target` giả lập: ghi `target.pid` (PID + start time), heartbeat mỗi 300 ms, nhận `stop-target.flag` để thoát êm, `freeze-once.flag` để ngừng heartbeat nhưng giữ process.
- `watchdog`: kiểm tra process theo PID + start time (tránh PID reuse), kiểm tra heartbeat, restart với backoff, đọc `stop-watchdog.flag`/`ProtectionEnabled`, dừng khi clean exit.
- `task-check-loop`: mô phỏng scheduled task bằng vòng lặp kiểm tra cùng logic, chu kỳ 60 giây.
- Mọi tiến trình con được redirect stdout/stderr và kill theo work dir để tránh treo pipe/leftover.
- Phase watchdog kill dùng `Kill(entireProcessTree: false)` để mô phỏng End Task của người dùng (không kill kèm target).

## 3. Kết quả

### 3.1. Tổng hợp phase

| Phase | Kết quả | Số liệu chính |
|---|---|---|
| restart + duplicate | pass | Trùng watchdog exit 2; trùng target exit 4; kill → restart 1356 / 2276 / 4312 ms; backoff tăng dần; dừng watchdog → target dừng |
| hang detection | pass | Freeze → target mới 4254 ms; 1 `hang_detected`; 1 restart |
| clean exit | pass | Watchdog exit 0 sau 446 ms; không restart; target đã dừng |
| uninstall stop | pass | Watchdog exit 0; không target mới; 0 restart |
| restart budget | pass | Kill → restart 783 / 1255 ms; exit 3; 1 `give_up`; không target sau đó |
| watchdog kill (bypass) | pass | Watchdog chết; target vẫn sống; kill target sau đó → **không restart** |
| scheduled task (simulation) | pass | Kill → task restart 60.169 ms; 2 `task_restart` |

### 3.2. Chi tiết

**Restart + duplicate.** Sau mỗi lần kill target, watchdog phát hiện trong vòng 200 ms và restart sau backoff. Instance watchdog thứ hai không chiếm được mutex và thoát mã 2; instance target thứ hai thoát mã 4; target gốc vẫn sống. Khi đặt `stop-watchdog.flag`, watchdog dừng và target cũng dừng.

**Hang detection.** Sau khi target ngừng heartbeat, watchdog phát hiện heartbeat cũ hơn 3 giây, kill target treo và restart. Tổng thời gian từ freeze đến target mới 4254 ms (gồm ngưỡng 3 giây + backoff 1 giây).

**Clean exit.** Target nhận `stop-target.flag`, ghi `clean-exit.flag` và thoát 0. Watchdog nhận diện clean exit, dừng trong 446 ms, không ghi `target_exit_unexpected`, không restart.

**Uninstall stop.** Kill target trước, sau đó đặt `stop-watchdog.flag`. Watchdog đang trong backoff kiểm tra flag mỗi 100 ms, dừng ngay, exit 0, không tạo target mới.

**Restart budget.** Với `--max-restarts 2`, sau 2 lần restart watchdog ghi `give_up` và thoát mã 3 thay vì restart vô hạn. Khi app crash liên tục (DB hỏng, thiếu quyền), đây là hành vi an toàn để tránh loop và bảo vệ pin/CPU.

**Watchdog kill (bypass).** Kill riêng watchdog: target vẫn chạy bình thường (heartbeat tiếp tục), nhưng sau khi target bị kill thì không có tiến trình nào restart nó. Đây là lý do MVP cần scheduled task làm lớp ngoài (hoặc dùng task-only).

**Scheduled task (simulation).** Vòng lặp 60 giây phát hiện target chết và restart trong 60.169 ms. Đây là mô phỏng cùng logic; Task Scheduler thật có thể trễ hơn khi máy tải cao hoặc sleep.

### 3.3. So sánh cơ chế

| Cơ chế | Độ phức tạp | Quyền | Độ tin cậy | Gỡ cài đặt | Ghi chú |
|---|---|---|---|---|---|
| Scheduled Task (poll 60 giây) | Thấp – trung bình | Task user (không admin) hoặc SYSTEM (admin) | Trung bình | Xóa task | Trễ 0–60 giây; Task Scheduler repetition tối thiểu 1 phút; task có thể bị disable/xóa; không cần tiến trình thường trú |
| Watchdog process riêng | Thấp | Cùng mức app (MVP: elevated) | Trung bình – cao trong user session | Stop flag + kill | Phát hiện < 1 giây; backoff, chống trùng; bị kill cùng app nếu không có lớp ngoài |
| Auto-start (Run/Startup/Task at logon) | Rất thấp | User | Thấp | Xóa entry | Chỉ khởi động khi đăng nhập; không phát hiện kill; user có thể tắt trong Task Manager |
| Windows Service | Cao | SYSTEM/admin khi cài | Cao | Uninstall service | SCM recovery; không truy cập webcam/foreground; cần IPC, installer và ACL; Post-MVP |

### 3.4. Phân tích bypass

| Đường bypass | Kết quả | Đối sách / ghi chú |
|---|---|---|
| Kill app | Scheduled task restart trong ≤ 1 chu kỳ; watchdog process restart trong ~1–4 giây | Hành vi chính |
| Kill watchdog process | Target vẫn chạy; sau khi target chết **không restart** | Phải có scheduled task/service làm lớp ngoài |
| Kill cả app và watchdog | Nếu **chỉ có watchdog process** (không có task): không restart đến lần đăng nhập sau. Với task-only (MVP), task vẫn restart app ở chu kỳ kế tiếp | Prototype không có lớp ngoài; MVP dùng task làm lớp ngoài |
| Disable/xóa scheduled task | Không restart | Cần quyền admin/quyền sửa task; chấp nhận, ghi audit nếu phát hiện |
| Logoff / shutdown | App dừng trong phiên hiện tại; không ghi clean exit, `ProtectionEnabled` giữ nguyên để lần đăng nhập sau auto-start phục hồi bảo vệ | Bypass được chấp nhận; không ghi clean flag khi shutdown |
| Admin/SYSTEM | Có thể kill, gỡ, sửa hosts, xóa task | Giới hạn đã biết, công bố trong SECURITY.md |
| Sửa/xóa heartbeat/state | Có thể gây restart sai hoặc bỏ qua restart | ACL `%ProgramData%\EduFocus` là bắt buộc ở Phase 7+ |

## 4. Khuyến nghị cho Phase 7

### 4.1. Kiến trúc MVP

- **Scheduled task health-check mỗi 1 phút**, đăng ký trong lúc cài đặt (cần admin một lần) và cấu hình **Run with highest privileges** dưới tài khoản phụ huynh để có thể khởi động app elevated mà không hiện UAC mỗi lần.
- **Auto-start khi đăng nhập** (task at logon hoặc Startup) để phục hồi sau reboot.
- **Không thêm watchdog process thường trú trong MVP.** Nếu vẫn muốn restart nhanh, watchdog process phải được scheduled task giám sát; điều này làm tăng số thành phần và độ phức tạp mà chưa có bằng chứng cần thiết cho MVP.
- Giữ **single-instance mutex** toàn cục trong app; instance thứ hai thoát ngay và ghi audit.

### 4.2. Logic task-check

1. Đọc `ProtectionEnabled`, `LastCleanShutdownAtUtc`, clean-exit flag và trạng thái app.
2. Nếu Protection OFF hoặc có clean exit hợp lệ → không làm gì.
3. Nếu app còn sống nhưng heartbeat cũ hơn 2–3 lần chu kỳ → coi là treo; gửi tín hiệu đóng êm (nếu có IPC), chờ ngắn rồi kill.
4. Nếu app không chạy → kiểm tra restart budget **lưu bền vững** (ví dụ 5 lần/10 phút); trong budget thì start app, vượt budget thì ghi `WatchdogGaveUp` và dừng thử đến lần đăng nhập sau.
5. Start app ở chế độ ẩn xuống tray; app tự áp dụng lại protection nếu trạng thái trước là ON.
6. Khi uninstall: đặt `ProtectionEnabled = false`, xóa scheduled task, dừng app, không ghi clean exit giả.

### 4.3. Audit và trạng thái

- Sự kiện tối thiểu:
  - `WatchdogRestart`: thời điểm phát hiện, nguồn (Task/Process), PID cũ, lý do (`UnexpectedExit`/`Hang`/`Unknown`), số lần thử, backoff ms, snapshot `ProtectionEnabled`.
  - `WatchdogGaveUp`: lý do, số lần restart trong cửa sổ, thời điểm reset dự kiến.
  - `WatchdogCleanExit`: thời điểm, nguồn (PIN exit).
  - `WatchdogStop`: uninstall/protection off.
- Không ghi window title, URL hay dữ liệu người dùng trong audit watchdog.
- State phải nằm trong `%ProgramData%\EduFocus` với ACL Administrators/SYSTEM; task chạy elevated nên đọc/ghi được restart budget và audit, còn app elevated ghi trạng thái protection.

### 4.4. Ngưỡng đề xuất

| Tham số | MVP | Ghi chú |
|---|---|---|
| Chu kỳ task | 60 giây | Tối thiểu của Task Scheduler repetition |
| Heartbeat app | 15–30 giây | Ghi file/PID; task coi là treo khi quá 2–3 chu kỳ |
| Graceful stop | 3–5 giây | Trước khi kill target treo |
| Restart budget | 5 lần / 10 phút | Lưu bền vững; reset sau 10 phút ổn định |
| Backoff | 5–10 giây giữa các lần restart | Với task 1 phút, backoff chủ yếu do nhịp task |

### 4.5. Production (Post-MVP)

- Windows Service (SYSTEM) làm lớp enforcement và restart; Session Agent trong user session cho webcam/foreground; IPC qua named pipe.
- SCM recovery + service ACL + process ACL để chống kill; vẫn không chống được admin/SYSTEM.
- Không đặt camera/foreground trong service (Session 0).

## 5. Giới hạn của spike

- Phase scheduled task chỉ mô phỏng; chưa đăng ký Task Scheduler thật (môi trường không cho phép thay đổi quản trị). Chưa đo độ trễ thật khi máy tải cao, sleep/hibernate hoặc task bị policy chặn.
- Chạy **không elevated**; chưa kiểm chứng khác biệt integrity level giữa app elevated và học sinh standard user, chưa thử kill bằng Task Manager dưới tài khoản học sinh.
- Chưa thử Windows shutdown/logoff, safe mode, domain policy, EDR/antivirus.
- Heartbeat là file trên cùng ổ đĩa; disk full, lock hoặc antivirus có thể làm phát hiện sai/trễ.
- PID reuse được giảm thiểu bằng so khớp start time (dung sai 2 giây) nhưng chưa test trên máy chạy dài ngày.
- Task-check trong prototype chưa áp dụng restart budget; đây là yêu cầu bắt buộc của Phase 7.
- Chưa đo chi phí CPU/RAM của watchdog process; với poll 200 ms, chi phí dự kiến thấp nhưng cần đo lại trong app thật.
- Chưa đánh giá khả năng học sinh xóa/sửa scheduled task dưới tài khoản standard user; cần test ở Phase 7.

## 6. Artifact

- Báo cáo thô: `docs/spikes/results/watchdog-20261029-195936.md` / `.json`.
- Audit JSONL của từng phase nằm trong work root `%TEMP%\edufocus-watchdog-spike\20261029-195936` (đã xóa sau run PASS; dùng `--keep-work-dir` để giữ lại).
- Tool: `tools/WatchdogSpike/`.

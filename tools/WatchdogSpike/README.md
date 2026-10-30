# Watchdog spike

Prototype Phase 0 cho issue [#3](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/3): đánh giá cơ chế watchdog và khả năng chống tắt ứng dụng trên Windows.

## Phạm vi

- Ứng dụng giả lập (`target`) có heartbeat, clean-exit flag và chế độ treo để thử nghiệm.
- Watchdog process riêng: phát hiện target chết hoặc treo, restart có backoff, chặn trùng instance, giới hạn số lần restart, dừng khi uninstall/protection off.
- Mô phỏng scheduled task: kiểm tra định kỳ, phát hiện target chết và restart trong một chu kỳ.
- Đo các đường bypass: kill watchdog, kill target, kill cả hai, dừng trong backoff.

Không nằm trong phạm vi: đăng ký scheduled task thật, Windows Service, driver kernel, process ACL, chống admin/SYSTEM. Các phần này chỉ được đánh giá định tính trong `docs/spikes/watchdog-evaluation.md`.

## Vị trí trong kiến trúc

- Phase 7 có thể tái dùng logic trong `tools/WatchdogSpike/` làm tham chiếu cho watchdog scheduled task.
- Tool nằm ngoài `EduFocus.slnx`, chỉ chạy thủ công; không ảnh hưởng CI.
- Không có mã nào trong `src/` phụ thuộc vào tool này.

## An toàn

- Chỉ ghi file trong `%TEMP%\edufocus-watchdog-spike\<run-id>` (hoặc `--work-dir` do bạn chỉ định).
- Không sửa hosts, registry, scheduled task, service hay cấu hình hệ thống.
- Chỉ kill tiến trình do chính nó khởi động, nhận diện bằng PID **và** thời điểm bắt đầu để tránh PID reuse.
- Báo cáo chỉ chứa PID, timestamp, sự kiện và số liệu; không đọc dữ liệu người dùng.

## Yêu cầu

- Windows 10/11, .NET SDK 9 hoặc mới hơn.

## Chạy

Từ thư mục gốc repo:

```powershell
# Chạy toàn bộ ma trận (7 phase, khoảng 90 giây do mô phỏng task 60 giây)
dotnet run --project tools/WatchdogSpike -c Release -- scenario --output-dir docs/spikes/results

# Bỏ phase mô phỏng scheduled task
dotnet run --project tools/WatchdogSpike -c Release -- scenario --skip-task-simulation

# Giữ thư mục trạng thái và audit JSONL để điều tra
dotnet run --project tools/WatchdogSpike -c Release -- scenario --keep-work-dir

# Tùy chỉnh tham số
dotnet run --project tools/WatchdogSpike -c Release -- scenario `
  --poll-ms 200 --restart-base-ms 1000 --restart-max-ms 4000 `
  --max-restarts 5 --reset-seconds 600 --stale-seconds 3
```

Lệnh internal (được `scenario` gọi tự động, chỉ dùng khi debug):

```powershell
dotnet run --project tools/WatchdogSpike -c Release -- watchdog --work-dir <path>
dotnet run --project tools/WatchdogSpike -c Release -- target --work-dir <path>
dotnet run --project tools/WatchdogSpike -c Release -- task-check --work-dir <path>
dotnet run --project tools/WatchdogSpike -c Release -- task-check-loop --work-dir <path>
```

Kết quả ghi vào `docs/spikes/results/watchdog-<timestamp>.{md,json}`.

## Các phase

| Phase | Kịch bản | Kỳ vọng |
|---|---|---|
| restart + duplicate | Kill target 3 lần; chạy thêm watchdog và target trùng | Watchdog restart với backoff tăng dần; instance trùng thoát mã 2 (watchdog) / 4 (target) |
| hang detection | Target ngừng heartbeat nhưng process còn sống | Watchdog phát hiện heartbeat cũ, kill và restart |
| clean exit | Target thoát êm, ghi `clean-exit.flag` | Watchdog dừng, không restart |
| uninstall stop | Kill target rồi đặt `stop-watchdog.flag` | Watchdog dừng trước backoff, exit 0, không start lại |
| restart budget | Kill target vượt `--max-restarts` | Watchdog ghi `give_up`, exit 3, không restart vô hạn |
| watchdog kill (bypass) | Kill riêng watchdog, sau đó kill target | Target vẫn chạy sau khi watchdog chết; không có gì restart target sau đó |
| scheduled task (simulation) | Vòng lặp kiểm tra mỗi 60 giây | Phát hiện target chết và restart trong một chu kỳ |

## Tham số

| Tham số | Mặc định | Ghi chú |
|---|---|---|
| `--work-dir` | (bắt buộc với lệnh internal) | Thư mục trạng thái |
| `--output-dir` | `docs/spikes/results` | Thư mục ghi báo cáo |
| `--poll-ms` | `200` | Chu kỳ kiểm tra của watchdog |
| `--restart-base-ms` | `1000` | Backoff cơ sở |
| `--restart-max-ms` | `4000` | Backoff tối đa |
| `--max-restarts` | `5` | Số lần restart tối đa trong cửa sổ |
| `--reset-seconds` | `600` | Thời gian target ổn định để reset bộ đếm |
| `--stale-seconds` | `3` | Ngưỡng heartbeat cũ để coi là treo |
| `--graceful-stop-ms` | `3000` | Chờ target thoát êm khi dừng |
| `--heartbeat-ms` | `300` | Chu kỳ heartbeat của target |
| `--task-interval-seconds` | `60` | Chu kỳ mô phỏng scheduled task |
| `--skip-task-simulation` | off | Bỏ phase scheduled task |
| `--keep-work-dir` | off | Giữ thư mục trạng thái sau khi chạy |

## File trạng thái trong work dir

| File | Ý nghĩa |
|---|---|
| `state.json` | `ProtectionEnabled` + `LastCleanShutdownAtUtc` |
| `target.pid`, `watchdog.pid` | PID + thời điểm bắt đầu để nhận diện tiến trình |
| `heartbeat.json` | PID + timestamp heartbeat mới nhất |
| `clean-exit.flag` | Target thoát hợp lệ; watchdog/task không restart |
| `stop-watchdog.flag` | Yêu cầu watchdog dừng (uninstall hoặc kết thúc test) |
| `freeze-once.flag` | Yêu cầu target ngừng heartbeat một lần |
| `audit.jsonl` | Sự kiện watchdog/target/task, dùng cho báo cáo |

## Giới hạn

- Phase scheduled task chỉ **mô phỏng** vòng lặp kiểm tra; chưa đăng ký Task Scheduler thật, chưa đo độ trễ thực tế của Task Scheduler khi máy tải cao hoặc sleep.
- Chưa kiểm chứng khác biệt giữa standard user và app elevated; chưa thử kill bằng Task Manager dưới tài khoản học sinh.
- Prototype không có lớp ngoài bảo vệ watchdog; kill cả app và watchdog thì không có restart cho tới lần đăng nhập sau.
- Task kiểm tra trong prototype không tự áp dụng restart budget; Phase 7 cần lưu bộ đếm vào state bền vững để tránh restart mỗi phút khi app crash lặp lại.
- Heartbeat dùng file trên cùng ổ đĩa; disk full, lock hoặc antivirus có thể làm phát hiện sai hoặc trễ.
- Chưa đánh giá EDR/antivirus, policy domain hoặc quyền can thiệp của phụ huynh/admin.

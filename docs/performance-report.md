# EduFocus — Báo cáo hiệu năng (issue #43)

Báo cáo ghi lại ngưỡng hiệu năng của BA Document §9.3, số đo tự động trên máy đại diện
và quy trình soak 8 giờ bắt buộc trước phát hành. Các cổng thủ công được chốt trong
[release-checklist.md](./release-checklist.md).

Tham chiếu: [ba_document.md](./ba_document.md) §9.3–§9.4,
[database_schema.md](./database_schema.md) §14–§15,
[system_architecture.md](./system_architecture.md) §16.

---

## 1. Điều kiện đo

| Hạng mục | Giá trị |
|---|---|
| Ngày đo | 2026-10-30 |
| Nhánh / commit nền | `feature/backlog`, `a77134a` (#42) + thay đổi #43 |
| Hệ điều hành | Windows 10 Pro 22H2, build 19045 |
| CPU | Intel Core i5-9300H, 4 nhân / 8 luồng |
| RAM | 15.9 GB |
| Ổ đĩa | Samsung PM981a NVMe SSD |
| .NET | SDK 10.0.401 |
| Cấu hình test | Release, không bật ACL thư mục (TempDatabase) |
| Máy test | Laptop phát triển, không phải VM sạch |

Máy này đại diện cho laptop học tập tầm trung. Các phép đo CPU/RAM/soak thực tế vẫn phải
chạy trên VM sạch Windows 10 22H2 và Windows 11 theo [installer.md](./installer.md) §7.

---

## 2. Số đo tự động

Chạy lại:

```powershell
dotnet test EduFocus.slnx --configuration Release `
  --filter "FullyQualifiedName~PerformanceBudgetTests|FullyQualifiedName~BlockPagePerformanceTests"
```

| Hạng mục | Ngưỡng cam kết | Số đo | Kết quả | Test |
|---|---|---|---|---|
| Khởi tạo DB lần đầu (migration + pragma) | một phần của khởi động < 3 giây | 993 ms | Đạt | `Database_initialization_stays_within_the_startup_budget` |
| Mở DB đã có | một phần của khởi động < 3 giây | 39 ms | Đạt | cùng test |
| DB sau 30 ngày (90 session, 21.600 sample, 360 event, 270 blocked attempt, 150 audit) | < 50 MB sau 1 năm | 8,14 MB | Đạt | `Thirty_day_dataset_meets_storage_and_report_budgets` |
| Báo cáo tháng 30 ngày (thời gian service, không gồm WPF) | không treo UI; mục tiêu < 1 giây | trung vị 171 ms (200/161/171 ms) | Đạt | cùng test |
| Retention mặc định 30/90/365 ngày (xóa 3.601 bản ghi cũ) | hoàn tất trong batch ngắn, không chặn UI | 147 ms | Đạt | `Retention_meets_the_time_budget_and_the_yearly_database_stays_under_fifty_megabytes` |
| DB mô phỏng 1 năm ở trạng thái ổn định | < 50 MB | 9,57 MB (trước retention 10,42 MB) | Đạt | cùng test |
| Block page loopback (20 request tuần tự) | < 100 ms | trung vị 0,27 ms; p95 0,43 ms; max 0,62 ms | Đạt | `Loopback_block_page_responds_within_the_hundred_millisecond_budget` |
| Chu kỳ start/stop listener | không rò listener | 20 chu kỳ, mỗi chu kỳ phục vụ HTTP 200 và bind lại thành công | Đạt | `Listener_survives_many_start_stop_cycles_without_leaking` |

Phép đo 30 ngày dùng dữ liệu tổng hợp đúng giả định database schema §15: 3 giờ học/ngày,
sample mỗi 15 giây. DB 1 năm được mô phỏng bằng dữ liệu còn lại sau retention:
activity sample 30 ngày, attention event/blocked attempt 90 ngày, session và audit 365 ngày.
Số đo 8,14 MB thấp hơn nhiều so với dự toán 50 MB.

Phép đo báo cáo tháng đọc 90 session và 21.600 sample qua repository thật; đây là phần nặng
nhất khi phụ huynh mở báo cáo. Phần dựng WPF/ViewModel cần đo thủ công trong checklist.

---

## 3. Soak 8 giờ (cổng thủ công)

Số đo 8 giờ **chưa chạy tự động** vì cần phiên desktop thật, camera thật và chế độ bảo vệ
đang bật. Repo cung cấp script lấy mẫu:

```powershell
# Chạy khi app đã mở ở chế độ cần đo
.\scripts\Measure-Soak.ps1 -Mode Idle -DurationHours 8 -OutputDirectory artifacts\soak-idle
.\scripts\Measure-Soak.ps1 -Mode Protection -DurationHours 8 -OutputDirectory artifacts\soak-protection
.\scripts\Measure-Soak.ps1 -Mode Attention -DurationHours 8 -OutputDirectory artifacts\soak-attention
```

Script ghi `soak-samples.csv`, `soak-summary.json` và `soak-summary.md`, đồng thời trả mã lỗi
nếu vượt ngưỡng:

| Chế độ | CPU trung bình | Working set đỉnh | Handle tăng tối đa | Working set tăng |
|---|---|---|---|---|
| Idle (Protection OFF) | < 1% | < 120 MB | +100 | ≤ 2 MB/giờ |
| Protection ON, không camera | < 3% | < 200 MB | +100 | ≤ 2 MB/giờ |
| Attention monitoring | < 10% | < 350 MB | +100 | ≤ 2 MB/giờ |

Điều kiện soak:

- VM sạch Windows 10 22H2 và Windows 11 23H2+, app cài từ MSI, đăng nhập Administrator.
- Chạy đủ 8 giờ, không thao tác trong lúc đo; không sleep/hibernate.
- Với Protection: bật bảo vệ, mở block page HTTP ít nhất một lần, để process watcher chạy.
- Với Attention: camera hoạt động, không dùng dữ liệu camera thật ngoài mục đích test;
  không lưu ảnh/frame.
- Ghi lại PID, version, thời điểm bắt đầu/kết thúc và đính kèm 3 file kết quả.

**Trạng thái hiện tại: chưa chạy 8 giờ.** Đây là cổng bắt buộc trước khi push tag; kết quả
phải được ký trong [release-checklist.md](./release-checklist.md) §2.3 và đính kèm artifact.

---

## 4. Kiểm tra thủ công bổ sung

Các mục sau cần đo trên VM sạch và ghi vào bản ghi phát hành:

| Mã | Kiểm tra | Ngưỡng |
|---|---|---|
| PERF-M-01 | Khởi động app đến dashboard (Stopwatch, SSD) | < 3 giây |
| PERF-M-02 | Mở báo cáo tháng trong Parent Mode | < 1 giây, UI không treo |
| PERF-M-03 | Chạy retention lần đầu sau khi mở app (dữ liệu mẫu) | UI phản hồi, không treo > 2 giây |
| PERF-M-04 | CPU/RAM khi Google Meet/Teams đang dùng camera và EduFocus attention | CPU < 10%, RAM < 350 MB |
| PERF-M-05 | CPU/RAM khi mở block page HTTP vài lần | trong ngưỡng Protection |
| PERF-M-06 | Sau 8 giờ, DB/tệp log không tăng bất thường | log theo retention 7–14 ngày; DB trong dự toán |

Nếu một mục vượt ngưỡng, tạo issue P1/P2 kèm số đo, cấu hình máy và bước tái hiện trước khi
phát hành; không đánh dấu bản phát hành đạt.

---

## 5. Kết luận

- Toàn bộ ngưỡng đo tự động (khởi tạo DB, dung lượng DB, báo cáo tháng, retention,
  block page, chu kỳ listener) đều đạt trên máy đại diện.
- Chưa có phát hiện P0/P1 từ phần hiệu năng tự động.
- Các mục soak 8 giờ, UI WPF, camera attention và process watcher thực tế đang chờ chạy trên
  VM sạch; đây là điều kiện bắt buộc trong release checklist.
- Script soak và số đo có thể tái lập; không có dữ liệu camera, PIN hay URL nhạy cảm trong
  artifact đo.

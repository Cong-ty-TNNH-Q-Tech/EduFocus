# Đánh giá pipeline attention Phase 0

- **Issue:** [#1 - Spike camera + pipeline nhận diện chú ý](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/1)
- **Ngày đo:** 2026-10-26
- **Trạng thái:** Spike hoàn tất phần đo; đề xuất chờ review (ADR-17)
- **Phạm vi:** face presence + head-pose proxy + độ ổn định/tài nguyên trên Windows. Không đánh giá danh tính, gaze, iris hay `Drowsy`.

## 1. Kết luận

1. **Chọn OpenCvSharp (MSMF) + ONNX Runtime + YuNet 2023mar** làm pipeline Phase 0. Trong 30 phút, YuNet phát hiện mặt ở **99,2%** mẫu chạy được, CPU trung bình **0,23% toàn máy** (1,81% một core), RAM working set **184 MiB**.
2. **Haar cascade không đủ làm backend chính.** Trong lần chạy 1 FPS cùng điều kiện, Haar chỉ phát hiện mặt ở **0,8%** mẫu so với **24,4%** của YuNet; Haar cũng không có landmark/confidence. Giữ Haar làm baseline chẩn đoán, không dùng cho sản phẩm.
3. **Hiệu năng đạt NFR dự kiến** (CPU < 10%, RAM < 350 MB, sampling 1-2 FPS) với biên rất lớn trên máy đo. Tuy nhiên **mở camera mất 6,4-8,2 giây**, nên không được mở camera đồng bộ trong startup.
4. **Chưa thể kết luận độ chính xác attention.** Dữ liệu hiện có đo detector/state machine, chưa có ground truth cho `Focused`/`Distracted`. Cần calibration head-pose và bổ sung EAR/gaze ở Phase 5 nếu muốn `Drowsy`.
5. **Giữ local-first:** không ghi frame, landmark, embedding hay gửi network. Báo cáo chỉ chứa số liệu tổng hợp và các trường số học.

## 2. Cách đo

### 2.1. Môi trường

| Hạng mục | Giá trị |
|---|---|
| OS | Windows 10.0.19045 |
| Runtime | .NET 9.0.15 (X64) |
| CPU | Intel Core i5-9300H @ 2.40 GHz, 4 core / 8 logical |
| RAM | 15,9 GiB khả dụng cho GC |
| Webcam | USB2.0 HD UVC WebCam |
| Capture API | OpenCV MSMF |
| Độ phân giải | 640x480 |
| ONNX intra-op threads | 1 |

### 2.2. Cấu hình

- YuNet: score threshold `0.60`, NMS `0.30`, model 227,1 KiB.
- Haar: `haarcascade_frontalface_default.xml` + `haarcascade_eye.xml`, tổng 1.241,7 KiB.
- Sampling mặc định 15 giây cho benchmark dài; load test 1 FPS.
- Warm-up 5 frame trước khi đo.
- Ngưỡng nghiệp vụ: Distracted > 20 giây, Absent > 30 giây, LowLight > 30 giây, luminance < 40.
- Head-pose là proxy từ 5 landmark YuNet (yaw/pitch chuẩn hóa); Haar dùng độ lệch tâm bounding box nên kém tin cậy hơn.

### 2.3. Các lần chạy

| Run | Backend | Thời lượng | Mục đích | File |
|---|---|---|---|---|
| Chính | YuNet | 30 phút, 15 giây/mẫu | Độ ổn định + tài nguyên dài | `results/yunet-20261026-111223.md` / `.json` |
| Load test | YuNet | 3 phút, 1 FPS | Kiểm tra tải cao | `results/yunet-20261026-110652.*` |
| So sánh | Haar | 2 phút, 1 FPS | Baseline classical CV | `results/haar-20261026-111006.*` |
| Failure | Haar | 6 mẫu | Không có camera | `results/haar-20261026-120557.*` |
| Smoke sau refactor | YuNet | 30 giây, 2 mẫu | Xác nhận adapter/Domain sau refactor | `results/yunet-20261026-120517.*` |

Số liệu chính được thu bằng harness trước khi refactor kiến trúc. Logic classifier/state machine được chuyển nguyên trạng vào `Domain`; smoke test sau refactor cho 2/2 mẫu, face 100%, CPU 0,29%, RAM 186,9 MiB. Các run 1 FPS không chạy đồng thời và không có ground truth, nên tỷ lệ face chỉ dùng để so sánh tương đối, không phải accuracy tuyệt đối.

## 3. Kết quả

### 3.1. YuNet 30 phút (run chính)

| Hạng mục | Giá trị |
|---|---|
| Mẫu | 120/120 thành công, 0 lỗi, 0 lần reopen |
| Thời lượng thực tế | 29,75 phút |
| Face detected | 99,2% (119/120 mẫu) |
| Luminance trung bình / thấp nhất | 97,9 / 83,9 |
| Model load | 170 ms (wall 185,9 ms gồm JIT) |
| Mở camera | 8.204,3 ms |
| Warm-up | 699,1 ms |
| Inference đầu tiên | 26,6 ms |
| CPU trung bình | 0,23% toàn máy; 1,81% một core |
| CPU cao nhất | 1,72% toàn máy; 13,79% một core |
| Working set | 184,0 MiB trung bình; 201,5 MiB cao nhất |
| Private memory | 208,5 MiB trung bình; 226,9 MiB cao nhất |

Độ trễ:

| Hạng mục | P50 | P95 | P99 | Max |
|---|---:|---:|---:|---:|
| Capture (ms) | 2,2 | 112,2 | 136,5 | 139,1 |
| Detect + inference (ms) | 22,7 | 32,3 | 37,7 | 44,6 |

Phân bố trạng thái:

| Trạng thái | Committed (debounce) | Raw theo từng mẫu |
|---|---:|---:|
| Focused | 1.784,9 giây (100%) | 1.635,0 giây (91,6%) |
| Distracted | 0 | 134,9 giây (7,6%) |
| Absent | 0 | 15,0 giây (0,8%) |
| CameraUnavailable | 0 | 0 |

**Đọc kết quả:** 7,6% raw `Distracted` bị debounce 20 giây nuốt hoàn toàn. Đây là hành vi mong muốn để tránh đổi trạng thái vì liếc mắt ngắn, nhưng cũng có nghĩa hệ thống có thể **đánh giá thấp** mức xao nhãng ngắn. Cần calibration ngưỡng khi có ground truth.

### 3.2. Load test 1 FPS

| Hạng mục | YuNet (3 phút) | Haar (2 phút) |
|---|---:|---:|
| Mẫu | 180/180 | 120/120 |
| Face detected | 24,4% | 0,8% |
| CPU trung bình | 0,37% máy / 2,97% core | 0,51% máy / 4,09% core |
| CPU cao nhất | 1,74% máy / 13,94% core | 2,13% máy / 17,04% core |
| Working set | 180,8 / 196,5 MiB | 147,2 / 155,5 MiB |
| Capture P95 | 118,4 ms | 109,7 ms |
| Detect P95 | 24,8 ms | 11,2 ms |
| Committed Focused / Absent / Unknown | 59,2% / 29,6% / 11,2% | 4,2% / 70,7% / 25,2% |

YuNet vẫn đạt 1 FPS với CPU rất thấp. Haar detect nhanh hơn trên mỗi frame nhưng gần như không phát hiện được mặt trong điều kiện thực tế, nên không thể bù bằng tốc độ. Tỷ lệ face thấp của YuNet ở run này phản ánh người dùng không phải lúc nào cũng trong khung hình, không phải accuracy của model.

### 3.3. Ảnh tĩnh (kiểm tra chẩn đoán)

Dùng ảnh mẫu công khai Lena 640x480, 30 lần lặp sau warm-up:

| Backend | Face | Confidence | Yaw / Pitch | Detect P50 | Detect P95 |
|---|---|---:|---:|---:|---:|
| YuNet | Có, 1 mặt, 2 mắt | 0,883 | 0,137 / 0,038 | 24,1 ms | 40,2 ms |
| Haar | Có, 1 mặt, 2 mắt | 0,750 | 0,180 / 0,173 | 17,6 ms | 20,4 ms |

Haar nhanh hơn trên ảnh tĩnh đã warm-up, nhưng chênh lệch không lớn và Haar yếu hơn rõ rệt trên live stream. Kết quả này chỉ xác nhận pipeline xử lý ảnh đúng, không dùng để chọn backend.

### 3.4. Không có camera

- Camera index 99: mở thất bại sau 40,5 ms; 6/6 mẫu lỗi; `CameraUnavailable` 100%; không crash.
- OpenCV MSMF in cảnh báo `can't be used to capture by index`.
- Chưa thử rút camera đột ngột giữa phiên. Adapter có cơ chế đóng camera khi capture fail và harness reopen theo chu kỳ, nhưng cần integration test riêng.

## 4. Đối chiếu NFR

| NFR dự kiến | Kết quả đo | Đánh giá |
|---|---|---|
| CPU attention < 10% trên CPU 4 nhân hiện đại | 0,23% toàn máy; 1,81% một core (30 phút) | Đạt, biên lớn |
| Sampling 1-2 FPS | 1 FPS: 180/180 mẫu, detect P95 24,8 ms, capture P95 118,4 ms | Đạt; chưa đo 2 FPS |
| RAM < 350 MB | 184,0 MiB working set; 208,5 MiB private | Đạt |
| Không block startup | Mở camera 6,4-8,2 giây | **Chưa đạt nếu mở đồng bộ**; cần async/defer |
| Không ghi frame/network | Thiết kế chỉ lưu metric; báo cáo không có ảnh/box/landmark | Đạt theo thiết kế |
| Face presence ổn định | 99,2% mẫu 30 phút | Đạt cho presence; chưa đủ kết luận attention accuracy |

## 5. So sánh phương án

| Phương án | Face box | Landmark / head pose | Chi phí | Kết luận |
|---|---|---|---|---|
| OpenCV + YuNet ONNX | Có, confidence | Có 5 landmark, yaw/pitch proxy | CPU/RAM thấp; thêm native OpenCV/ONNX | **Chọn cho Phase 0** |
| OpenCV Haar cascade | Có, không confidence | Không; phải proxy bằng tâm box | Nhẹ hơn, không cần ONNX | Baseline chẩn đoán; không dùng chính |
| WinRT `Windows.Media.FaceAnalysis` | Có | Không landmark | Không cần package ngoài; cần MediaCapture | Loại cho Phase 0 vì thiếu head pose |
| MediaPipe Face Mesh / Iris | Có | Có mesh, iris, EAR | Nặng hơn; thêm model/runtime | Để Phase 5 cho gaze/`Drowsy` |
| Cloud API | Có | Tùy dịch vụ | Network + chi phí + privacy | Loại vì local-first |

## 6. Rủi ro và hạn chế

- **Chưa có ground truth:** số liệu face/state chưa chứng minh độ chính xác phân loại chú ý. Cần study label thủ công hoặc so sánh với video đã gán nhãn.
- **Head-pose proxy:** YuNet 5 landmark đủ cho yaw/pitch thô, không đủ cho gaze/iris; Haar càng kém.
- **`Drowsy` chưa hỗ trợ:** cần EAR hoặc model riêng; không được ghi `Drowsy` trong production khi chưa có.
- **Camera startup chậm:** MSMF mở 6,4-8,2 giây trên máy đo; phải mở async, có timeout, hiển thị trạng thái.
- **Camera contention:** chưa đo đồng thời Meet/Teams; cần Multi-App Camera hoặc fallback `CameraUnavailable`.
- **Ánh sáng:** run chính có luminance thấp nhất 83,9, chưa stress ngưỡng 40; cần test phòng tối.
- **Một máy, một webcam, một người:** không đại diện cho nhiều khuôn mặt, kính, khẩu trang, góc camera.
- **License:** cần rà soát license của YuNet/OpenCV/ONNX Runtime trước khi phân phối.
- **Kích thước bản cài:** OpenCV native + ONNX Runtime làm tăng installer; cần đo sau khi đóng gói.

## 7. Khuyến nghị

1. Dùng YuNet + OpenCV MSMF làm backend Phase 0; giữ Haar chỉ để chẩn đoán/so sánh.
2. Mở camera bất đồng bộ sau khi UI sẵn sàng; không block startup; có timeout và trạng thái `CameraUnavailable`.
3. Giữ sampling mặc định 15 giây cho đo dài; hỗ trợ 1-2 FPS khi cần phản hồi nhanh. Đo thêm 2 FPS trên máy mục tiêu.
4. Calibrate ngưỡng yaw/pitch và debounce trên người dùng thật; log confidence/coverage để báo cáo độ tin cậy.
5. Phase 5 mới thêm EAR/gaze/iris và `Drowsy`; không hứa tính năng này trong Phase 0.
6. Thêm health check camera + hướng dẫn Multi-App Camera; test rút camera giữa phiên.
7. Rà soát license và kích thước redistributable trước khi đóng gói.
8. Giữ nguyên nguyên tắc không ghi frame/landmark/embedding; nếu thêm debug imagery phải là opt-in riêng và không dùng dữ liệu trẻ em.

## 8. Tái lập

Từ thư mục gốc repo:

```powershell
# Tải model (kiểm tra SHA256)
pwsh tools/AttentionPipelineSpike/fetch-models.ps1

# Test logic Domain
dotnet test src/EduFocus.Domain.Tests/EduFocus.Domain.Tests.csproj

# Smoke 30 giây
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends yunet --interval-seconds 15 --duration-minutes 0.5

# Benchmark chính 30 phút
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends yunet --interval-seconds 15 --duration-minutes 30

# Load test 1 FPS
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends yunet --interval-seconds 1 --duration-minutes 3

# Haar baseline
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends haar --interval-seconds 1 --duration-minutes 2

# Không có camera
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends haar --camera 99 --interval-seconds 5 --duration-minutes 0.5
```

Kết quả được ghi vào `docs/spikes/results/` dưới dạng `.json` (toàn bộ metric) và `.md` (báo cáo đọc được). Không có frame/landmark/embedding trong file output.

## 9. Tham chiếu

- ADR: [`docs/adr/ADR-17-attention-pipeline.md`](../adr/ADR-17-attention-pipeline.md)
- Kiến trúc attention: [`docs/system_architecture.md`](../system_architecture.md) §8, §17
- NFR: [`docs/ba_document.md`](../ba_document.md) §9.3
- Use case: [`docs/use_cases.md`](../use_cases.md) UC08
- Kết quả thô: [`docs/spikes/results/`](results/)

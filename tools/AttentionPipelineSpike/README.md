# Attention pipeline spike

Prototype Phase 0 cho issue [#1](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/1): đánh giá pipeline camera + nhận diện chú ý trên Windows.

## Phạm vi

- So sánh 2 backend phát hiện khuôn mặt:
  - `yunet`: OpenCV MSMF/DSHOW + ONNX Runtime + YuNet 2023mar (có 5 landmark).
  - `haar`: OpenCV MSMF/DSHOW + Haar cascade (classical CV, không landmark).
- Lấy mẫu theo chu kỳ (mặc định 15 giây), suy ra trạng thái `Focused`, `Distracted`, `Absent`, `CameraUnavailable` với debounce.
- Đo thời gian khởi tạo, độ trễ capture/inference, CPU/RAM, độ ổn định, tỷ lệ phát hiện mặt, ánh sáng.
- Mô phỏng lỗi camera (không camera) và cơ chế mở lại.

Không nằm trong phạm vi: `Drowsy` (cần eye-aspect-ratio hoặc model riêng), nhận diện danh tính, lưu ảnh/video.

## Vị trí trong kiến trúc

- `src/EduFocus.Domain/Attention/`: trạng thái, classifier, state machine thuần business rule.
- `src/EduFocus.Ports/`: `IAttentionFrameAnalyzer` + DTO, không phụ thuộc OpenCV/ONNX.
- `src/EduFocus.Infrastructure.Attention/`: adapter OpenCV MSMF/DSHOW + YuNet ONNX + Haar cascade.
- `tools/AttentionPipelineSpike/`: harness đo lường/báo cáo dùng một lần, không nằm trong `EduFocus.slnx`.

Adapter chưa được wire vào WPF/DI; việc đó thuộc Phase 5 sau khi chốt ADR.

## Quyền riêng tư

- Khung hình chỉ xử lý trong RAM và được `Dispose` ngay sau mỗi mẫu.
- Không ghi ảnh, landmark thô, embedding hay dữ liệu sinh trắc học ra đĩa/network.
- Báo cáo chỉ chứa số liệu tổng hợp và các trường số học của khuôn mặt (confidence, tỷ lệ yaw/pitch, số mắt); không có tọa độ hộp hay landmark.

## Yêu cầu

- Windows 10/11, .NET SDK 9 hoặc mới hơn.
- Webcam hoạt động; ứng dụng khác (Teams/Zoom) nên tắt để tránh camera bận.
- PowerShell 7+ (script tải model).

## Tải model

Chạy từ thư mục gốc repo:

```powershell
pwsh tools/AttentionPipelineSpike/fetch-models.ps1
```

Script tải và kiểm tra SHA256:

| Model | SHA256 |
|---|---|
| `face_detection_yunet_2023mar.onnx` | `8F2383E4DD3CFBB4553EA8718107FC0423210DC964F9F4280604804ED2552FA4` |
| `haarcascade_frontalface_default.xml` | `0F7D4527844EB514D4A4948E822DA90FBB16A34A0BBBBC6ADC6498747A5AAFB0` |
| `haarcascade_eye.xml` | `71CC64FC305A355DC60067880F6FBBD43DD155BD63EE3844661A1BDA34B2FD8C` |

Model nằm trong `tools/AttentionPipelineSpike/models/` (đã gitignore).

## Kiểm thử logic

```powershell
dotnet test src/EduFocus.Domain.Tests/EduFocus.Domain.Tests.csproj
```

Test bao phủ classifier và state machine (debounce 20s/30s, low-light, camera unavailable).

## Chạy

Từ thư mục gốc repo:

```powershell
# Smoke test 30 giây
dotnet run --project tools/AttentionPipelineSpike -c Release -- --duration-minutes 0.5

# Benchmark chính: YuNet, 15 giây/mẫu trong 30 phút
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends yunet --interval-seconds 15 --duration-minutes 30

# So sánh Haar trên cùng điều kiện (thời lượng ngắn hơn)
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends haar --interval-seconds 15 --duration-minutes 5

# Kiểm tra tải cao: 1 FPS trong 3 phút
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends yunet --interval-seconds 1 --duration-minutes 3

# Chạy lần lượt cả hai backend
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends yunet,haar --interval-seconds 15 --duration-minutes 5

# Mô phỏng không có camera
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --backends haar --camera 99 --interval-seconds 5 --duration-minutes 1

# Nếu MSMF lỗi, thử DirectShow
dotnet run --project tools/AttentionPipelineSpike -c Release -- `
  --capture dshow --duration-minutes 0.5
```

Kết quả ghi vào `docs/spikes/results/`:

- `<backend>-<timestamp>.json`: toàn bộ số liệu và từng mẫu.
- `<backend>-<timestamp>.md`: báo cáo đọc được, cùng dữ liệu.

## Tham số

| Tham số | Mặc định | Ghi chú |
|---|---|---|
| `--backends` | `yunet` | `yunet`, `haar`, hoặc danh sách cách nhau dấu phẩy |
| `--camera` | `0` | Index camera OpenCV |
| `--capture` | `msmf` | `msmf` hoặc `dshow` |
| `--width`, `--height` | `640`, `480` | Độ phân giải yêu cầu |
| `--interval-seconds` | `15` | Chu kỳ lấy mẫu |
| `--duration-minutes` | `30` | Thời lượng benchmark |
| `--warmup-frames` | `5` | Số frame bỏ qua trước khi đo |
| `--score-threshold` | `0.6` | Ngưỡng confidence YuNet |
| `--nms-threshold` | `0.3` | Ngưỡng NMS |
| `--low-light-threshold` | `40` | Luminance trung bình tối thiểu |
| `--distracted-seconds` | `20` | Debounce chuyển sang `Distracted` |
| `--absent-seconds` | `30` | Debounce chuyển sang `Absent` |
| `--low-light-seconds` | `30` | Debounce `CameraUnavailable` do thiếu sáng |
| `--reopen-seconds` | `30` | Chu kỳ thử mở lại camera |
| `--flush-frames` | `1` | Số frame bỏ trước mỗi lần đọc để tránh dữ liệu cũ |
| `--onnx-threads` | `1` | Intra-op threads của ONNX Runtime |

## Cách suy ra trạng thái

- Không mở được camera hoặc đọc frame thất bại: `CameraUnavailable` ngay lập tức.
- Không thấy mặt và luminance thấp: ứng viên `LowLight`, sau 30 giây chốt `CameraUnavailable` (kèm lý do `no-face-low-light` để phân biệt).
- Không thấy mặt: ứng viên `Absent`, chốt sau 30 giây.
- Có mặt nhưng yaw/pitch vượt ngưỡng: ứng viên `Distracted`, chốt sau 20 giây.
- Có mặt chính diện: `Focused` (chốt ngay).
- YuNet dùng landmark để tính yaw/pitch; Haar dùng độ lệch tâm khuôn mặt trong khung hình làm proxy nên kém chính xác hơn.

## Giới hạn đã biết

- Chưa đánh giá `Drowsy` (cần landmark mắt hoặc model riêng).
- Chưa tự điều chỉnh exposure/focus của webcam; kết quả phụ thuộc ánh sáng phòng.
- Chưa thử Windows.Media.FaceAnalysis (WinRT): chỉ có face box, không landmark, và cần thêm MediaCapture; ghi nhận định tính trong báo cáo.
- Benchmark chạy trên một máy và một webcam; số liệu mang tính tham chiếu, cần lặp lại trên cấu hình mục tiêu.
- Harness cố ý không nằm trong `EduFocus.slnx` để không ảnh hưởng CI; adapter `EduFocus.Infrastructure.Attention` nằm trong solution và được build cùng.

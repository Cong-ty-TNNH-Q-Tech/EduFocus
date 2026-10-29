# ADR-17: Attention pipeline Phase 0 - OpenCV + ONNX Runtime + YuNet

- **Status:** Proposed (chờ review)
- **Date:** 2026-10-26
- **Issue:** [#1](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/1)
- **Related:** ADR-03 (webcam attention estimate, không lưu frame), ADR-11 (`CameraUnavailable` là trạng thái hạng nhất)

## Context

Phase 0 cần một pipeline camera đủ ổn định để:
- phát hiện khuôn mặt và ước lượng head pose thô (trái/phải/lên/xuống);
- chạy 1-2 FPS với CPU thấp, RAM dưới 350 MB;
- không ghi frame/landmark/embedding, không gọi network;
- phân biệt `Focused`, `Distracted`, `Absent`, `CameraUnavailable`;
- cho biết chi phí camera startup và độ ổn định dài hạn trước khi tích hợp WPF.

Các lựa chọn đã cân nhắc: OpenCV + YuNet ONNX, OpenCV Haar cascade, WinRT `Windows.Media.FaceAnalysis`, MediaPipe Face Mesh/Iris, cloud API.

## Decision

Chọn **OpenCvSharp + OpenCV MSMF + ONNX Runtime + YuNet 2023mar** làm backend chính cho Phase 0.

- Face detection: YuNet, score threshold 0,60, NMS 0,30, 1 ONNX intra-op thread.
- Head pose: proxy yaw/pitch từ 5 landmark; không dùng gaze/iris ở Phase 0.
- Sampling: mặc định 15 giây cho đo dài; hỗ trợ 1-2 FPS khi cần.
- Haar cascade chỉ giữ làm baseline chẩn đoán, không làm backend sản phẩm.
- `Drowsy`, gaze và iris để Phase 5; không tuyên bố hỗ trợ trước khi có EAR/model riêng.
- Adapter nằm ở `EduFocus.Infrastructure.Attention`; Domain/Ports không phụ thuộc OpenCV/ONNX.
- Camera phải được mở bất đồng bộ ở Phase 5; không block startup.

## Evidence

Đo trên Windows 10.0.19045, i5-9300H (4C/8T), webcam USB2.0 640x480:

| Chỉ số | YuNet | Haar |
|---|---:|---:|
| 30 phút, 15 giây/mẫu | 120/120 mẫu, face 99,2% | Không chạy 30 phút |
| 1 FPS, face detected | 24,4% (3 phút) | 0,8% (2 phút) |
| CPU trung bình | 0,23% máy / 1,81% core (30 phút) | 0,51% máy / 4,09% core (1 FPS) |
| RAM working set | 184,0 MiB | 147,2 MiB |
| Detect P95 | 32,3 ms (15 giây/mẫu); 24,8 ms (1 FPS) | 11,2 ms (1 FPS) |
| Camera open | 8.204 ms | 6.433 ms |
| Landmark | 5 điểm | Không |
| Confidence | Có | Không |

- 30 phút: `CameraUnavailable` 0%, reopen 0, raw state `Focused` 91,6% / `Distracted` 7,6% / `Absent` 0,8%; committed `Focused` 100% do debounce 20 giây.
- Không camera: 6/6 mẫu `CameraUnavailable`, không crash; open fail 40,5 ms.
- Ảnh tĩnh Lena: YuNet p50 24,1 ms, Haar p50 17,6 ms sau warm-up; Haar nhanh hơn trên ảnh nhưng thua rõ trên live stream.
- Chi tiết và file thô: [`docs/spikes/attention-pipeline-evaluation.md`](../spikes/attention-pipeline-evaluation.md).

## Consequences

### Positive

- Face presence ổn định, có confidence và landmark để suy ra head pose.
- CPU/RAM thấp hơn nhiều so với NFR; đủ dư địa cho 1-2 FPS.
- Giữ được local-first, không ghi frame.
- Tách adapter sạch: Domain test được không cần camera/model.

### Negative / Risks

- Thêm native dependency OpenCV + ONNX Runtime, làm tăng kích thước installer và độ phức tạp đóng gói.
- Camera MSMF mở 6,4-8,2 giây trên máy đo; nếu mở đồng bộ sẽ vi phạm trải nghiệm startup.
- Head-pose từ 5 landmark là proxy, chưa đủ cho gaze/`Drowsy`; cần calibration trên người dùng thật.
- Chưa có ground truth để đo accuracy phân loại chú ý.
- Cần rà soát license của model/runtime trước khi phân phối.
- Chưa kiểm chứng Multi-App Camera/Meet/Teams và rút camera giữa phiên.

## Alternatives

- **Haar cascade:** nhẹ, không cần ONNX, nhưng face detected 0,8% ở 1 FPS, không confidence/landmark. Loại khỏi vai trò backend chính.
- **WinRT FaceAnalysis:** có sẵn trên Windows, không cần NuGet native, nhưng chỉ trả box, không landmark/head pose; tích hợp MediaCapture phức tạp. Loại cho Phase 0.
- **MediaPipe Face Mesh/Iris:** phù hợp cho gaze/EAR/`Drowsy` nhưng nặng hơn và cần thêm model/runtime. Để Phase 5.
- **Cloud API:** vi phạm local-first và privacy. Loại.

## Follow-ups

1. Review và chấp nhận/từ chối ADR này.
2. Đo lại trên máy mục tiêu và webcam mục tiêu (tối thiểu 30 phút).
3. Thiết kế async camera init + timeout + trạng thái UI.
4. Calibration yaw/pitch/debounce với ground truth.
5. Đánh giá Multi-App Camera, camera contention, mất camera giữa phiên.
6. Phase 5: thêm EAR/gaze/iris, model cho `Drowsy`; cập nhật ADR mới nếu đổi backend.
7. Rà soát license/redistributable và kích thước installer.

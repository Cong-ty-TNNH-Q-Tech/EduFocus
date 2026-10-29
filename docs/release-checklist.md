# EduFocus — Release checklist (bản ghi phát hành)

Bản ghi này được điền cho **mỗi bản phát hành**, ký duyệt và đính kèm cùng artifact
(GitHub Release hoặc artifact của release workflow). Không phát hành khi còn mục bắt buộc
chưa đạt hoặc còn issue P0/P1.

Tham chiếu: [release-process.md](./release-process.md),
[security-e2e-checklist.md](./security-e2e-checklist.md),
[performance-report.md](./performance-report.md),
[installer.md](./installer.md) §7, [SECURITY.md](../SECURITY.md).

---

## 1. Thông tin bản phát hành

| Trường | Giá trị |
|---|---|
| Version / tag | `v...` |
| Commit | `...` |
| Ngày phát hành | `...` |
| Người thực hiện | `...` |
| Release workflow run | `...` (URL) |
| Môi trường ký | có / không (ghi rõ `REQUIRE_CODE_SIGNING`) |

---

## 2. Cổng bắt buộc

### 2.1. CI và artifact

- [ ] Release workflow xanh; toàn bộ test pass (TRX đã upload).
- [ ] Dry run trên branch thử nghiệm thành công trước khi push tag.
- [ ] Version trong tên file, `EduFocus.exe`, `EduFocus.NativeHost.exe`, MSI khớp tag.
- [ ] `SHA256SUMS.txt` khớp và đã được đọc lại tự động.
- [ ] Chữ ký `Valid` trên exe/native host/MSI (nếu đã cấu hình chứng chỉ); nếu unsigned, đã ghi rõ lý do.
- [ ] Artifact có đủ MSI, portable ZIP, browser extension ZIP và hai hướng dẫn cài đặt.
- [ ] CHANGELOG có section của version và `### Breaking changes`.
- [ ] Không có secret, PFX, DB/log/bak/tmp trong artifact.

### 2.2. Bảo mật và quyền riêng tư

- [ ] Đã chạy [security-e2e-checklist.md](./security-e2e-checklist.md) §4 trên Windows 10 22H2
      và Windows 11; có evidence (ảnh/log) và không chứa PIN, recovery code, URL nhạy cảm.
- [ ] Không còn issue P0/P1 mở; các phát hiện P2/P3 còn lại đã có issue kèm rủi ro.
- [ ] Không có frame/ảnh/landmark/embedding camera trong thư mục dữ liệu, log hay artifact.
- [ ] Log không chứa PIN, recovery code, token, query string hoặc tiêu đề cửa sổ nhạy cảm.
- [ ] Browser extension chỉ gửi URL đã khớp rule (đã làm sạch query/fragment/credentials) qua
      named pipe cục bộ; không đọc/gửi lịch sử duyệt web, nội dung trang hay cookie.
- [ ] Gỡ cài đặt xóa hosts marker, listener, scheduled task và thư mục runtime đúng chính sách.

### 2.3. Hiệu năng và độ tin cậy

- [ ] Test hiệu năng tự động pass:
      `PerformanceBudgetTests` + `BlockPagePerformanceTests`.
- [ ] [performance-report.md](./performance-report.md) §2 có số đo của bản phát hành này
      (hoặc số đo không đổi so với báo cáo và đã ghi lý do).
- [ ] Soak 8 giờ `Idle` PASS; soak 8 giờ `Protection` PASS.
- [ ] Soak 8 giờ `Attention` PASS nếu attention monitoring được bật trong bản phát hành.
- [ ] Không có restart, không tăng handle quá 100, working set trong ngưỡng; đính kèm
      `soak-summary.json`/CSV.
- [ ] Khởi động app < 3 giây; mở báo cáo tháng < 1 giây; retention không treo UI.

### 2.4. Cài đặt, nâng cấp và gỡ cài đặt

- [ ] Cài mới bằng MSI trên VM sạch (Windows 10 22H2 và Windows 11) thành công; dialog
      **Thông báo quyền riêng tư** hiển thị đúng tiếng Việt và chặn Next khi chưa tick.
- [ ] Shortcut Desktop/Start Menu tồn tại; `%ProgramData%\EduFocus` có ACL
      Administrators/SYSTEM; thư mục cài có `docs\privacy-notice.md`,
      `THIRD-PARTY-NOTICES.md`, `LICENSE`.
- [ ] Standard user mở app nhận UAC dự kiến và app chạy sau khi nâng quyền.
- [ ] Nâng cấp từ bản trước giữ DB/settings, session cũ được phục hồi, chỉ còn một entry
      trong Apps & Features.
- [ ] Native messaging host được đăng ký cho Chrome/Edge sau khi cài (kiểm tra registry
      `NativeMessagingHosts\com.edufocus.native_host` ở cả hai registry view) và bị xóa khi gỡ.
- [ ] Cài browser extension từ ZIP (Load unpacked hoặc CRX), mở popup thấy trạng thái kết nối;
      khi EduFocus tắt, duyệt web không bị ảnh hưởng (fail-open).
- [ ] Gỡ cài đặt trả lời giữ/xóa dữ liệu đúng lựa chọn; hosts/listener/watchdog sạch.

### 2.5. Migration, backup và dữ liệu

- [ ] Migration chạy thành công trên DB mới và DB nâng cấp; backup trước migration được tạo
      và xác minh.
- [ ] Restore backup đã được kiểm tra (restore file mới nhất, DB mở được, dữ liệu còn nguyên).
- [ ] Retention 30/90/365 ngày chạy đúng trên dữ liệu mẫu 30 ngày; DB trong dự toán 50 MB/năm.
- [ ] Xóa dữ liệu theo yêu cầu (Privacy) hoạt động và có audit `DataDeleted`.

### 2.6. Tài liệu

- [ ] CHANGELOG, SECURITY (phiên bản hỗ trợ), hướng dẫn cài đặt, hướng dẫn browser extension
      và release notes đã cập nhật.
- [ ] [privacy-notice.md](./privacy-notice.md) và [dpia.md](./dpia.md) của bản phát hành đã được
      duyệt và khớp tính năng; timestamp consent trong onboarding và dialog MSI hoạt động.
- [ ] [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) khớp dependency thực tế
      (`dotnet list package --include-transitive`), không có GPL/AGPL/non-commercial;
      FluentAssertions vẫn pin 7.2.0.
- [ ] Tài liệu người dùng nêu rõ giới hạn hosts/HTTPS và block page chỉ áp dụng HTTP.

---

## 3. Ký duyệt

| Vai trò | Tên | Ngày | Chữ ký |
|---|---|---|---|
| Kỹ thuật / Release owner | | | |
| Kiểm thử | | | |
| Quyền riêng tư / pháp lý | | | |
| Quản lý phát hành | | | |

Kết luận: ☐ Đạt — phát hành  ☐ Không đạt — hoãn (lý do: ...)

---

## 4. Lưu trữ và phân phối

- [ ] Bản ghi này được điền đầy đủ, ký và đính kèm GitHub Release (hoặc artifact
      `edufocus-release-<version>` cùng release workflow).
- [ ] Kèm evidence: TRX test, số đo hiệu năng, `soak-summary.*`, ảnh/log E2E đã làm sạch.
- [ ] Lưu bản ghi tối thiểu 365 ngày cùng hồ sơ phát hành; không đưa dữ liệu nhạy cảm vào
      chứng từ.

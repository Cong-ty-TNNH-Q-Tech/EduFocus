# EduFocus — Release checklist (bản ghi phát hành)

Bản ghi này được điền cho **mỗi bản phát hành**, ký duyệt và đính kèm cùng artifact
(GitHub Release hoặc artifact của release workflow). Không phát hành khi còn mục bắt buộc
chưa đạt hoặc còn issue P0/P1.

Tham chiếu: [release-process.md](./release-process.md),
[security-e2e-checklist.md](./security-e2e-checklist.md),
[phase8-e2e-checklist.md](./phase8-e2e-checklist.md),
[mobile/README.md](../mobile/README.md),
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
- [ ] Version trong tên file, `EduFocus.exe`, `EduFocus.Service.exe`, MSI khớp tag.
- [ ] `SHA256SUMS.txt` (desktop) khớp và đã được đọc lại tự động.
- [ ] Chữ ký `Valid` trên exe/service exe/MSI (nếu đã cấu hình chứng chỉ); nếu unsigned, đã ghi rõ lý do.
- [ ] Artifact có đủ MSI, portable ZIP và hướng dẫn cài đặt.
- [ ] CHANGELOG có section của version và `### Breaking changes`.
- [ ] Không có secret, PFX, DB/log/bak/tmp trong artifact.

### 2.2. Bảo mật và quyền riêng tư

- [ ] Đã chạy [security-e2e-checklist.md](./security-e2e-checklist.md) §4 trên Windows 10 22H2
      và Windows 11; có evidence (ảnh/log) và không chứa PIN, recovery code, URL nhạy cảm.
- [ ] Không còn issue P0/P1 mở; các phát hiện P2/P3 còn lại đã có issue kèm rủi ro.
- [ ] Không có frame/ảnh/landmark/embedding camera trong thư mục dữ liệu, log hay artifact.
- [ ] Log không chứa PIN, recovery code, token, query string, tiêu đề cửa sổ nhạy cảm hoặc khóa mã hóa DB.
- [ ] DB/WAL/SHM và backup là file mã hóa SQLCipher (16 byte đầu khác `SQLite format 3\0`); không có
      chuỗi dữ liệu mẫu trong file; `edufocus.key` tồn tại và không chứa khóa plaintext.
- [ ] Thiếu/sai/hỏng file khóa: app fail-closed, giữ nguyên DB, không tự tạo khóa mới; backup/khôi phục
      trên cùng máy thành công; backup mã hóa bằng khóa khác bị từ chối.
- [ ] Gỡ cài đặt xóa hosts marker, listener, scheduled task/watchdog cũ, service `EduFocusProtection` và
      thư mục runtime đúng chính sách.

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
      `THIRD-PARTY-NOTICES.md`, `LICENSE`, `EduFocus.Service.exe`, native `e_sqlcipher.dll`.
- [ ] `%ProgramData%\EduFocus` có `edufocus.db` mã hóa và `edufocus.key` chỉ Administrators/SYSTEM;
      gỡ cài đặt **giữ dữ liệu** giữ cả hai; **xóa dữ liệu** xóa cả file khóa.
- [ ] Service `EduFocusProtection` ở trạng thái **Running**, auto-start, chạy LocalSystem;
      `EduFocus.Service.exe --health` trả healthy; marker `HKLM\SOFTWARE\EduFocus\ServiceMode=1`
      tồn tại.
- [ ] Đóng UI khi đang bảo vệ: hosts/block page/process watcher vẫn hoạt động; mở lại UI thấy
      trạng thái từ service, không có engine cục bộ thứ hai.
- [ ] Kill `EduFocus.Service.exe`: SCM restart trong khoảng 60 giây; session cũ `Interrupted`
      ghi chú `ServiceStopped`, có audit `ProtectionServiceStopped`/`SessionInterrupted`.
- [ ] Reboot khi đang bảo vệ: service tự khởi động, protection được reconcile, session được phục hồi.
- [ ] Standard user mở app nhận UAC dự kiến (UI vẫn cần admin vì đọc/ghi DB ACL) và app chạy sau
      khi nâng quyền; service mode không fallback về engine cục bộ khi service dừng.
- [ ] Nâng cấp từ bản trước giữ DB/settings, service dừng rồi khởi động lại, session cũ được phục hồi,
      chỉ còn một entry trong Apps & Features.
- [ ] Gỡ cài đặt trả lời giữ/xóa dữ liệu đúng lựa chọn; service row `EduFocusProtection`, hosts,
      listener, watchdog/scheduled task cũ và marker `ServiceMode` sạch.
- [ ] Portable ZIP chạy standalone (không có service row/marker) và watchdog vẫn hoạt động.

### 2.5. Mobile Android (Phase 8)

- [ ] `mobile-release.yml` xanh với tag `v...`; `versionName`/`versionCode` khớp quy tắc trong
      [mobile/README.md](../mobile/README.md) và tag; `versionCode` không dùng lại.
- [ ] Biến repository `CLOUD_RELAY_BASE_URL` trỏ đúng endpoint HTTPS production (không có `/` cuối);
      artifact desktop/mobile mở lên đã điền sẵn endpoint này.
- [ ] Đủ secret `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
      `ANDROID_KEY_PASSWORD`; không có giá trị nào trong repo/log/artifact.
- [ ] `apksigner verify` pass; APK cài được trên thiết bị sạch Android 8/12/14; launcher hiển thị
      "EduFocus Phụ huynh", icon adaptive và splash đúng.
- [ ] Cài đè để nâng cấp giữ SecureStorage (còn ghép nối); gỡ app rồi cài lại không còn token cũ.
- [ ] `SHA256SUMS-Android.txt` khớp APK/AAB đính kèm release; hướng dẫn cài nằm trong `mobile/README.md`.
- [ ] WSS hoạt động trên bản release khi app đóng: foreground service giữ kết nối, nhận đủ 5 loại push,
      deep link mở đúng màn hình; payload sau giải mã chỉ có `type`, `date`, `notificationId`.
- [ ] Backend production bật `PUSH_WEBSOCKET_ENABLED=true`; kiểm tra fallback UnifiedPush/ntfy nếu bật
      `PUSH_ENABLED=true`.
- [ ] App release chỉ dùng HTTPS (cleartext tắt) và kết nối backend staging/production thành công.
- [ ] Đã chạy [phase8-e2e-checklist.md](./phase8-e2e-checklist.md) trên staging + thiết bị thật và
      đính kèm biên bản (ngày, phiên bản, thiết bị).
- [ ] Privacy notice hiển thị trước khi bật cloud; known limitations có trong README và privacy notice.

### 2.6. Migration, backup và dữ liệu

- [ ] Migration chạy thành công trên DB mới và DB nâng cấp; backup trước migration được tạo
      và xác minh; backup của DB mã hóa cũng là file mã hóa.
- [ ] Nâng cấp từ DB plaintext (bản trước #47) được nâng cấp tại chỗ, dữ liệu và
      `__EFMigrationsHistory` còn nguyên, file gốc chỉ bị thay sau khi bản mã hóa verify thành công.
- [ ] Restore backup đã được kiểm tra (restore file mới nhất, DB mở được, dữ liệu còn nguyên);
      backup plaintext legacy được mã hóa khi restore; đã thử khôi phục khi có đủ `edufocus.key`.
- [ ] Đã xác nhận quy trình mất khóa: không có khóa thì không thể khôi phục; hướng dẫn giữ
      `edufocus.key` cùng backup và chỉ khôi phục trên cùng máy có trong tài liệu phát hành.
- [ ] Retention 30/90/365 ngày chạy đúng trên dữ liệu mẫu 30 ngày; DB trong dự toán 50 MB/năm.
- [ ] Xóa dữ liệu theo yêu cầu (Privacy) hoạt động và có audit `DataDeleted`; thao tác không xóa
      file khóa khi vẫn còn dữ liệu cần giải mã.

### 2.7. Tài liệu

- [ ] CHANGELOG, SECURITY (phiên bản hỗ trợ), hướng dẫn cài đặt và release notes đã cập nhật.
- [ ] [privacy-notice.md](./privacy-notice.md) và [dpia.md](./dpia.md) của bản phát hành đã được
      duyệt và khớp tính năng; timestamp consent trong onboarding và dialog MSI hoạt động.
- [ ] [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) khớp dependency thực tế
      (`dotnet list package --include-transitive`), không có GPL/AGPL/non-commercial;
      FluentAssertions vẫn pin 7.2.0; mục SQLCipher/SQLitePCLRaw.e_sqlcipher và giấy phép BSD-2-Clause
      khớp gói đang phân phối.
- [ ] [database_schema.md](./database_schema.md) §18, privacy notice và DPIA mô tả đúng mã hóa
      SQLCipher, khóa DPAPI gắn máy và quy trình mất khóa; SECURITY.md không còn câu "mã hóa post-MVP".
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

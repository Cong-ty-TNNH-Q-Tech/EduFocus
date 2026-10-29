# Tài liệu Use Case - EduFocus

> **Trạng thái:** Thiết kế / đang chuyển đổi từ **Don't Be Lazy**.
> **Phiên bản:** 2.0 — 2026-10-26

---

## 1. Tác nhân (Actors)

| Tác nhân | Mô tả |
|---|---|
| **Phụ huynh (Parent)** | Người tạo PIN, bật/tắt bảo vệ, quản lý blocklist, xem báo cáo và thoát app |
| **Học sinh (Student)** | Người sử dụng máy trong chế độ được bảo vệ; xem trạng thái và trang chặn |
| **Hệ thống (System)** | Scheduler, protection engine, activity tracker, attention monitor, retention job |
| **Windows OS** | Nguồn sự kiện khởi động, sleep/resume, camera, foreground window |

---

## 2. Danh sách Use Case

| Mã | Tên Use Case | Tác nhân chính |
|---|---|---|
| UC01 | Thiết lập lần đầu & tạo PIN phụ huynh | Phụ huynh, Hệ thống |
| UC02 | Đăng nhập Parent Mode & khóa phiên | Phụ huynh, Hệ thống |
| UC03 | Bật chế độ bảo vệ | Phụ huynh, Hệ thống |
| UC04 | Tắt chế độ bảo vệ | Phụ huynh, Hệ thống |
| UC05 | Quản lý blocklist / allowlist | Phụ huynh |
| UC06 | Cấu hình lịch bảo vệ | Phụ huynh, Hệ thống |
| UC07 | Phiên học & theo dõi hoạt động | Hệ thống |
| UC08 | Ước lượng mức độ tập trung | Hệ thống |
| UC09 | Trang chặn & chặn ứng dụng | Học sinh, Hệ thống |
| UC10 | Xem dashboard & báo cáo | Phụ huynh, Học sinh |
| UC11 | Quản lý dữ liệu & quyền riêng tư | Phụ huynh |
| UC12 | Vòng đời ứng dụng & chống tắt | Phụ huynh, Học sinh, Hệ thống |
| UC13 | Đổi PIN / khôi phục PIN | Phụ huynh, Hệ thống |
| UC14 | Tự phục hồi sau crash/restart | Hệ thống |

---

## 3. Permission Matrix

| Hành động | Chưa đăng nhập | Student Mode | Parent Mode |
|---|---:|---:|---:|
| Xem trạng thái bảo vệ | ✅ | ✅ | ✅ |
| Xem thời gian học hôm nay | ✅ (tổng hợp) | ✅ | ✅ |
| Xem blocklist | ❌ | ❌ | ✅ |
| Thêm/sửa/xóa block rule | ❌ | ❌ | ✅ |
| Bật bảo vệ | ❌ | ❌ | ✅ |
| Tắt bảo vệ | ❌ | ❌ | ✅ |
| Xem báo cáo chi tiết | ❌ | ❌ | ✅ |
| Đổi PIN / recovery code | ❌ | ❌ | ✅ |
| Xóa dữ liệu | ❌ | ❌ | ✅ |
| Thoát ứng dụng | ❌ | ❌ | ✅ |
| Tắt máy / đăng xuất Windows | ✅ | ✅ | ✅ |

---

## 4. Chi tiết Use Case

### UC01 — Thiết lập lần đầu & tạo PIN phụ huynh

**Tác nhân:** Phụ huynh, Hệ thống
**Mô tả:** Lần chạy đầu tiên, ứng dụng hướng dẫn phụ huynh thiết lập PIN và giải thích phạm vi giám sát.

**Tiền điều kiện**
- Ứng dụng chưa có `ParentCredential`, hoặc onboarding trước đó đang dang dở (`OnboardingCompleted = false`).
- Database EduFocus mới đã được tạo; không có dữ liệu migrate từ 1.x.

**Trigger**
- Người dùng mở app và `OnboardingCompleted = false`.

**Luồng chính**
1. Hệ thống hiển thị màn hình giới thiệu EduFocus: mục đích, dữ liệu thu thập, giới hạn và cam kết local-only.
2. Hệ thống yêu cầu phụ huynh xác nhận:
   - Đã đọc thông báo quyền riêng tư.
   - Đã thông báo cho học sinh/gia đình về việc giám sát.
3. Hệ thống lưu hai mốc `PrivacyAcceptedAtUtc` và `GuardianAcknowledgedAtUtc` vào `AppSettings`.
4. Hệ thống hiển thị tuỳ chọn an toàn và lưu ngay:
   - Giám sát attention (mặc định bật).
   - Ưu tiên khởi động cùng Windows (mặc định bật; việc đăng ký thực tế thuộc issue release).
   - Kiểm tra quyền camera ở mức Windows consent store; bị từ chối chỉ hiển thị cảnh báo, không chặn thiết lập.
5. Phụ huynh nhập PIN 6–8 chữ số và xác nhận lại.
6. Hệ thống kiểm tra:
   - Độ dài 6–8.
   - Chỉ gồm chữ số.
   - Hai lần nhập khớp nhau.
   - Không nằm trong danh sách PIN yếu phổ biến (000000, 111111, 123456, 654321…).
7. Hệ thống tạo `ParentCredential`:
   - Salt ngẫu nhiên 16 byte.
   - PBKDF2-SHA256, 600.000 iterations.
   - Recovery code 12 ký tự ngẫu nhiên, hash tương tự PIN.
8. Hệ thống hiển thị recovery code **một lần** kèm nút sao chép và cảnh báo không chia sẻ; phụ huynh xác nhận đã lưu mã.
9. Hệ thống đánh dấu `OnboardingCompleted = true` và mở dashboard. Parent Mode không tự mở sau onboarding.

**Luồng thay thế**
- **PIN yếu hoặc không khớp:** Hiển thị lỗi ngay tại bước PIN và yêu cầu chọn PIN khác; chưa tạo credential.
- **Không xác nhận đồng ý:** Nút tiếp tục bị vô hiệu hoá; phụ huynh chỉ có thể thoát app.
- **Đóng app giữa onboarding:** Các bước đã lưu được giữ nguyên. Lần mở sau:
  - Chưa có PIN → tiếp tục từ bước tuỳ chọn/PIN, không hỏi lại consent đã xác nhận.
  - Đã có PIN nhưng chưa hoàn tất → yêu cầu nhập PIN để tạo lại recovery code mới (mã cũ không thể hiển thị lại), sau đó hoàn tất.
- **Camera bị Windows từ chối:** Hiển thị cảnh báo kèm hướng dẫn mở Settings → Privacy & security → Camera; phụ huynh vẫn có thể tiếp tục và cấp quyền sau.

**Ngoại lệ**
- Ghi file thất bại: hiển thị lỗi quyền truy cập, hướng dẫn chạy với quyền Administrator và cho phép thử lại; dữ liệu đã lưu không bị mất.
- Recovery code chỉ lưu hash: phụ huynh có thể tạo lại sau khi đăng nhập Parent Mode (UC13) hoặc ngay tại bước resume bằng PIN; mã cũ bị vô hiệu.
- PIN sai khi resume dùng chung lockout 5 lần/15 phút với UC02/UC13.

**Hậu điều kiện**
- `ParentCredential` tồn tại; hai mốc consent có giá trị; `OnboardingCompleted = true`.
- PIN và recovery code không lưu plaintext; audit ghi `PrivacyConsentRecorded`, `PinSetup`, `RecoveryCodeRegenerated` (nếu resume) và `OnboardingCompleted`.

**Business rules:** BR-01, BR-02, BR-22.

---

### UC02 — Đăng nhập Parent Mode & khóa phiên

**Tác nhân:** Phụ huynh, Hệ thống
**Mô tả:** Phụ huynh mở khóa quyền quản trị bằng PIN.

**Tiền điều kiện**
- Đã hoàn tất UC01.
- Ứng dụng đang chạy.

**Trigger**
- Phụ huynh bấm **Parent Login** hoặc thực hiện thao tác cần quyền quản trị.

**Luồng chính**
1. Hệ thống hiển thị màn hình nhập PIN.
2. Phụ huynh nhập PIN.
3. Hệ thống kiểm tra lockout:
   - Nếu đang bị khóa, hiển thị thời gian còn lại.
4. Hệ thống xác thực PIN bằng PBKDF2 + constant-time compare.
5. Thành công:
   - Reset `FailedAttempts`.
   - Tạo Parent session (mặc định 5 phút idle).
   - Mở dashboard.
6. Hệ thống ghi audit log "Parent login".

**Luồng thay thế**
- **Sai PIN:** tăng `FailedAttempts`, hiển thị số lần còn lại.
- **Sai 5 lần:** khóa 15 phút và ghi audit. Màn hình khóa cho phép chuyển sang **nhập recovery code** (không hiển thị lại mã).
- **Idle 5 phút:** tự động khóa phiên, quay về Student Mode/khóa màn hình.
- **Quên PIN:** chuyển sang UC13; recovery code dùng chung lockout với PIN để tránh bypass.

**Ngoại lệ**
- App bị tắt khi đang Parent Mode: phiên không được khôi phục; phải đăng nhập lại.
- Database bị lỗi: hiển thị lỗi và không cho thay đổi cấu hình.

**Hậu điều kiện**
- Parent Mode active trong thời gian giới hạn.
- Các thao tác nhạy cảm được phép.

**Business rules:** BR-02, BR-03, BR-04, BR-21.

---

### UC03 — Bật chế độ bảo vệ

**Tác nhân:** Phụ huynh, Hệ thống
**Mô tả:** Kích hoạt chặn web/app và bắt đầu phiên học.

**Tiền điều kiện**
- Đã có PIN.
- Parent Mode cho thao tác thủ công; scheduler được phép tự bật không cần Parent Mode.
- Protection đang OFF.
- Không có phiên học active khác.

**Trigger**
- Phụ huynh bấm **Bật bảo vệ**.
- Hoặc đến khung giờ schedule (hệ thống tự bật).

**Luồng chính**
1. Nếu thao tác thủ công, hệ thống xác nhận cấu hình hiện tại (monitoring bật/tắt, lịch) và yêu cầu PIN nếu phiên Parent Mode đã khóa.
2. Hệ thống kiểm tra blocklist và schedule hợp lệ. MVP chỉ áp dụng rule `Domain` exact và `App`; rule URL/path bị bỏ qua kèm cảnh báo. Nếu thao tác thủ công, xóa `ScheduleOverrideUntilUtc` còn hiệu lực.
3. Hệ thống tạo `StudySession` mới với trạng thái `Active`, `LastHeartbeatAtUtc = now`, `ActiveSeconds = 0`.
4. `ProtectionEngine`:
   - Ghi các domain exact vào hosts.
   - Khởi động block page server trên loopback:80 (nếu port khả dụng).
   - Bật process watcher cho app bị chặn.
   - Ghi trạng thái `ProtectionEnabled = true`.
5. `ActivityTracker` bắt đầu ghi sample foreground app/URL và heartbeat.
6. Nếu monitoring được bật và (nếu cấu hình) đang trong lớp online:
   - Khởi động attention monitor.
   - Nếu camera bận/không có quyền/không có webcam, ghi `CameraUnavailable`.
7. Hệ thống hiển thị trạng thái ON, icon tray đổi màu, ghi audit.

**Luồng thay thế**
- **Đã có session active:** không tạo mới; hiển thị session hiện tại.
- **Schedule tự bật:** không yêu cầu PIN; ghi audit actor = System.
- **Không có quyền admin:** hiển thị lỗi, không bật protection, hướng dẫn chạy elevated.

**Ngoại lệ**
- Ghi hosts thất bại do antivirus: hiển thị lỗi rõ ràng, ghi log, giữ protection OFF.
- Port 80 bị chiếm: vẫn áp dụng hosts và protection ON; block page HTTP không khả dụng, hiển thị cảnh báo.
- Camera lỗi/không có webcam: protection vẫn ON; ghi `CameraUnavailable`, retry theo chu kỳ.
- Database lỗi: rollback trạng thái, gỡ chặn nếu đã áp dụng.

**Hậu điều kiện**
- Protection ON.
- Blocklist được áp dụng.
- StudySession active.
- Monitoring chạy hoặc ghi nhận không khả dụng.

**Business rules:** BR-05, BR-06, BR-07, BR-11, BR-12, BR-14, BR-17.

---

### UC04 — Tắt chế độ bảo vệ

**Tác nhân:** Phụ huynh, Hệ thống
**Mô tả:** Gỡ chặn và kết thúc phiên học.

**Tiền điều kiện**
- Protection đang ON.
- Có Parent Mode hoặc hành động hệ thống theo lịch.

**Trigger**
- Phụ huynh bấm **Tắt bảo vệ**.
- Hoặc hết khung giờ schedule.

**Luồng chính**
1. Nếu thao tác thủ công, hệ thống yêu cầu PIN (nếu chưa ở Parent Mode) và xác nhận.
2. `ProtectionEngine`:
   - Dừng process watcher.
   - Dừng block page server.
   - Gỡ block khỏi hosts.
   - Flush DNS.
3. `ActivityTracker` dừng ghi sample.
4. `AttentionMonitor` dừng camera.
5. `StudySession` kết thúc:
   - `Completed` nếu hết lịch hoặc phụ huynh tắt bình thường.
   - `Aborted` nếu protection gặp lỗi nghiêm trọng/tamper và không thể tiếp tục.
   - `Interrupted` nếu app crash và được phục hồi (xem UC14).
6. Cập nhật `ActiveSeconds`, tổng hợp attention/category, blocked count (best-effort).
7. Ghi audit, cập nhật UI/tray.

**Luồng thay thế**
- **Tắt trong khung giờ schedule:** hiển thị cảnh báo "Bảo vệ sẽ tắt đến hết khung giờ hiện tại; khung giờ kế tiếp sẽ tự bật lại." Ghi audit và lưu `ScheduleOverrideUntilUtc = cuối khung giờ hiện tại`.
- **Không có session active:** chỉ gỡ chặn, không tạo session.

**Ngoại lệ**
- Gỡ hosts thất bại: giữ trạng thái ON, báo lỗi, không giả vờ đã tắt.
- Camera đang được app khác dùng: dừng monitor an toàn, không ảnh hưởng app khác.
- App crash giữa chừng: lần khởi động sau self-heal dọn block mồ côi; session cũ đóng tại heartbeat.

**Hậu điều kiện**
- Protection OFF.
- Hosts sạch.
- Session kết thúc và được lưu.
- Không còn camera/monitor chạy.

**Business rules:** BR-05, BR-06, BR-07, BR-17.

> **Hiện trạng Phase 2 (issue #16):** UC03/UC04 đã có state machine bền vững: bật/tắt yêu cầu Parent Mode,
> tạo/kết thúc `StudySession`, ghi audit `ProtectionOn`/`ProtectionOff`, bật thủ công xóa
> `ScheduleOverrideUntilUtc`, và lặp lại thao tác không tạo phiên/audit trùng. Khi app khởi động, phiên
> `Active` mồ côi bị đóng tại heartbeat và phiên `Recovery` được tạo nếu bảo vệ ON (UC14). Engine chặn
> web/app thật đã được nối ở issue #24 (`ProtectionEngine`); ActivityTracker và attention vẫn thuộc
> Phase 3–5. Cảnh báo của engine (ví dụ cổng 80 bận) vẫn được hiển thị trên UI thay vì giả định mọi
> thành phần đều chạy.

---

### UC05 — Quản lý blocklist / allowlist

**Tác nhân:** Phụ huynh
**Mô tả:** Xem, thêm, sửa, xóa rule chặn/cho phép.

**Tiền điều kiện**
- Parent Mode.

**Trigger**
- Phụ huynh mở **Blocklist**.

**Luồng chính — Xem**
1. Hệ thống hiển thị các nhóm:
   - Chatbot / AI Assistant (mặc định).
   - Social.
   - Game.
   - Custom.
2. Mỗi rule hiển thị: giá trị, loại, trạng thái, nguồn (mặc định/tùy chỉnh), hết hạn.
3. Hệ thống đánh dấu rule đang được allowlist override.

**Luồng chính — Thêm rule**
1. Phụ huynh chọn **Thêm rule**.
2. Chọn loại: Domain / App (MVP). URL/path bị vô hiệu hóa kèm ghi chú "cần browser extension".
3. Nhập giá trị.
4. Chọn hiệu lực: Block / Allow.
5. Chọn danh mục và thời hạn (nếu có).
6. Hệ thống validate:
   - Domain hợp lệ, lowercase, bỏ scheme/path/trailing dot; chặn domain cấp cao dùng chung và IP/localhost.
   - Không phải domain thiết yếu trong danh sách bảo vệ.
   - Không trùng rule cùng loại/hiệu lực; rule Allow cùng domain với Block được phép và thắng.
7. Lưu rule; nếu Protection ON, áp dụng lại engine trong tối đa 60 giây hoặc yêu cầu xác nhận restart protection.

**Luồng chính — Sửa / Xóa**
1. Phụ huynh chọn rule.
2. Sửa giá trị/hiệu lực/thời hạn hoặc xóa.
3. Hệ thống yêu cầu xác nhận nếu rule đang được dùng.
4. Lưu; áp dụng lại engine nếu cần.

**Luồng thay thế**
- **Allowlist trùng blocklist:** allowlist thắng; hiển thị cảnh báo. Hai rule khác `Effect` được phép tồn tại song song.
- **Rule mặc định:** không cho xóa vĩnh viễn, chỉ tắt; có thể reset về mặc định.
- **Thời hạn:** rule hết hạn tự động bị bỏ qua; không cần thao tác.
- **Rule URL/path:** lưu được nhưng không enforce trong MVP; UI phải nói rõ.

**Ngoại lệ**
- Domain không hợp lệ: highlight, không lưu.
- Domain thiết yếu (`meet.google.com`, `teams.microsoft.com`): từ chối chặn, gợi ý allowlist.
- Lưu DB lỗi: giữ nguyên UI, báo lỗi.

**Hậu điều kiện**
- Rule được lưu và áp dụng.
- Audit log ghi thay đổi.

**Business rules:** BR-08, BR-09, BR-23, BR-24, BR-25.

> **Hiện trạng (issue #19):** domain đã có `BlockRule` (chuẩn hóa domain/app/URL, đánh dấu
> `NotEnforced` cho rule chưa hỗ trợ), `BlockRuleEvaluator` (allow thắng block) và `BlockRuleSet` —
> snapshot bất biến bỏ rule disabled/`NotEnforced` lúc tạo, kiểm tra hết hạn mỗi lần đánh giá, coi
> domain thiết yếu là `Allow` và so khớp `Exact` nên `chatgpt.com.evil.test` không khớp
> `chatgpt.com`. `IBlockRuleRepository`/`SqliteBlockRuleRepository` lưu rule; `ProtectionEngine`
> (issue #24) nạp snapshot mỗi lần bật/reconcile nên vòng lặp matching không truy vấn DB. Thay đổi
> rule khi Protection ON chưa tự áp dụng lại — cần tắt/bật hoặc khởi động lại; live reload chưa có
> trong bản dựng này.
>
> **Hiện trạng (issue #20):** `BuiltInRuleCatalog` phiên bản 1 cung cấp 14 domain chatbot/AI và 7
> allowlist học tập (Meet, Teams, Classroom, Zoom, trang đăng nhập Google/Microsoft).
> `BlockRuleSeedService` chạy lúc khởi động, sau onboarding và trước reconcile: seed idempotent theo
> `(RuleType, MatchType, NormalizedValue, Effect)`, không ghi đè rule phụ huynh và giữ trạng thái
> bật/tắt của rule mặc định; mỗi lần có thay đổi ghi audit kèm `seedVersion`. Thẻ "Danh sách chặn"
> trong Parent Mode nhóm rule theo danh mục và cho bật/tắt (kể cả rule mặc định) với audit actor
> Parent. UI thêm/sửa rule tùy chỉnh và reset mặc định chưa triển khai.

---

### UC06 — Cấu hình lịch bảo vệ

**Tác nhân:** Phụ huynh, Hệ thống
**Mô tả:** Tự động bật/tắt bảo vệ theo khung giờ học.

**Tiền điều kiện**
- Parent Mode.

**Trigger**
- Phụ huynh mở **Schedule**.

**Luồng chính**
1. Hệ thống hiển thị danh sách khung giờ theo tuần.
2. Phụ huynh thêm khung giờ:
   - Chọn ngày (T2–CN).
   - Giờ bắt đầu / kết thúc.
   - Nhãn (ví dụ "Giờ học tối").
3. Hệ thống validate:
   - Giờ kết thúc > giờ bắt đầu; MVP không hỗ trợ khung qua nửa đêm.
   - Không chồng lấn trong cùng ngày; từ chối lưu nếu trùng.
4. Lưu schedule; bật `ScheduleEnabled`.
5. Scheduler kiểm tra mỗi 30 giây:
   - Nếu đang trong khung giờ và Protection OFF → tự bật.
   - Nếu hết khung giờ và Protection ON do schedule → tự tắt.
   - Nếu `ScheduleOverrideUntilUtc` còn hiệu lực → không tự bật lại trong khung giờ hiện tại.
6. Khi app khởi động trong khung giờ → áp dụng ngay.

**Luồng thay thế**
- **Phụ huynh tắt Protection trong khung giờ:** override đến hết khung giờ hiện tại; khung giờ kế tiếp tự bật lại.
- **Máy sleep qua khung giờ:** khi resume, scheduler đánh giá lại trạng thái hiện tại; thời gian sleep không cộng vào session.
- **Trùng khung giờ:** báo lỗi, không tạo hai session.

**Ngoại lệ**
- Thay đổi giờ hệ thống: schedule dùng wall clock/local time; monotonic clock chỉ dùng để phát hiện nhảy giờ bất thường. Khi phát hiện nhảy giờ, ghi audit `TamperDetected` và không tự tắt protection chỉ vì thay đổi giờ.
- Không có quyền admin khi tự bật: ghi lỗi, hiển thị cảnh báo cho phụ huynh.

**Hậu điều kiện**
- Schedule được lưu; trạng thái ON/OFF phản ánh đúng thời điểm.

**Hiện trạng triển khai (issue #17)**
- `ScheduleService` xử lý CRUD khung giờ (yêu cầu phiên Parent Mode, audit `ScheduleChanged`, chống chồng lấn) và `EvaluateAsync` chạy mỗi 30 giây cùng một lần lúc khởi động. `ProtectionSource` lưu bền vững nguồn bật (`Manual`/`Schedule`) nên lịch chỉ tự tắt bảo vệ do chính nó bật.
- Tắt thủ công trong khung giờ tạo `ScheduleOverrideUntilUtc` đến hết khung; khi sửa khung đang hoạt động, override được clamp theo giờ kết thúc mới. Bật thủ công xóa override và chuyển nguồn sang `Manual`.
- UI Parent Mode nhận giờ theo `HH:mm`, hỗ trợ `24:00` cho giờ kết thúc; khung không vắt qua nửa đêm. Lỗi engine khi tự bật/tắt được hiển thị thành cảnh báo và sẽ được thử lại ở lần đánh giá sau.

**Business rules:** BR-07, BR-20.

---

### UC07 — Phiên học & theo dõi hoạt động

**Tác nhân:** Hệ thống
**Mô tả:** Ghi nhận phiên học và app/URL đang dùng.

**Tiền điều kiện**
- Protection ON.
- `StudySession` active.

**Trigger**
- Bắt đầu protection (UC03).
- Timer sample mỗi 10–30 giây.
- Heartbeat session mỗi 10 giây (độc lập với sample).

**Luồng chính**
1. Hệ thống tạo `ActivitySample`:
   - Timestamp UTC.
   - App name / process name.
   - Window title.
   - URL/domain nếu lấy được.
   - Idle seconds.
   - Cờ foreground.
2. Hệ thống phân loại:
   - `Study` nếu app/URL nằm trong nhóm học tập hoặc lớp online.
   - `Neutral` nếu không xác định.
   - `Distraction` nếu thuộc nhóm bị chặn/giải trí.
3. Nếu là Google Meet/Teams/Zoom:
   - Đánh dấu `IsOnlineClass = true`.
   - Gắn URL/domain nếu có.
4. Hệ thống cập nhật `StudySession`:
   - `LastHeartbeatAtUtc` và `ActiveSeconds`.
   - Tổng thời gian theo category (`StudySeconds`, `DistractionSeconds`, `NeutralSeconds`) và overlay `OnlineClassSeconds`.
   - `IdleSeconds` lưu riêng, không trộn với category.
5. Sample được lưu theo retention; báo cáo dài hạn dùng session summary khi sample đã bị xóa.

**Luồng thay thế**
- **Không lấy được URL:** lưu app + window title; đặt `UrlSource = None`.
- **Idle > 5 phút:** tăng `IdleSeconds`; không tính là thời gian học active.
- **App chuyển foreground liên tục:** debounce 2 giây để tránh nhiễu.

**Ngoại lệ**
- Quyền truy cập process bị từ chối: ghi `Unknown`, không crash.
- Database đầy/lỗi: giữ sample trong bộ nhớ tối đa 5 phút, ghi log, thử lại.

**Hậu điều kiện**
- Activity sample và tổng hợp session được cập nhật.

**Business rules:** BR-11, BR-12, BR-17.

> **Triển khai (issue #25):** `StudySessionHeartbeatService` chạy beat mỗi 10 giây bằng `PeriodicTimer`,
> độc lập với activity sample. `ActiveSeconds` cộng theo monotonic clock và chặn tối đa 60 giây mỗi beat;
> wall clock lùi được clamp về `LastHeartbeatAtUtc`, nên sleep/tắt máy hoặc đổi giờ không làm sai thời gian.
> Repository ghi heartbeat bằng `UPDATE` có điều kiện (chỉ session `Active`, không lùi timestamp); lỗi DB
> được log và thử lại ở beat sau. Heartbeat dừng trước khi session `Complete`/`Interrupt` và tự dừng khi
> session không còn `Active`.

> **Triển khai (issue #26):** `ActivityTrackingService` poll foreground mỗi giây, debounce 2 giây và ghi
> tối đa một `ActivitySample` mỗi 15 giây; `DurationSeconds` lấy từ monotonic clock, chặn 60 giây/sample
> nên suspend không làm phồng thời gian. Idle ≥ 5 phút chỉ ghi một sample cho mỗi chuỗi nghỉ; `IdleSeconds`
> lưu riêng và bị trừ khỏi category time. `ActivityClassifier` dùng `BlockRuleSet` (Study/Distraction/
> Neutral/Unknown, allow thắng block); `OnlineClassDetector` nhận Meet/Teams/Zoom qua domain/process/title.
> URL đọc best-effort qua UI Automation, strip query/fragment/credentials; không có URL thì `UrlSource=None`
> và sample vẫn ghi app/process. Counters session ghi bằng targeted `AddActivitySecondsAsync` chỉ trên
> session `Active` trước khi insert sample; lỗi DB được log và thử lại ở beat sau, session kết thúc thì
> sampling tự dừng. Sampling start/stop cùng heartbeat trong enable/disable/reconcile và shutdown.

> **Triển khai (issue #27):** `OnlineClassDetector` nhận Meet qua `meet.google.com`/title `Google Meet` hoặc
> `Meet` + dấu phân cách, Teams qua process `ms-teams`/`teams` hoặc `teams.microsoft.com`, Zoom qua process
> `zoom` hoặc `zoom.us`; path/`.exe`/dấu nháy được chuẩn hóa và chat như Discord/Slack/Messenger/Zalo không
> bị nhận nhầm. Sample idle không đánh dấu `IsOnlineClass`, nên overlay `OnlineClassSeconds` không cộng
> trùng với category time. `MonitoringEligibility.IsEligible` là policy dùng chung cho UC08: khi
> `MonitorOnlyDuringClasses` bật, chỉ thời gian phát hiện lớp online mới được cộng vào
> `MonitoringEligibleSeconds`.

> **Triển khai (issue #28):** khi máy sleep hoặc phiên bị lock/disconnect, `ISystemPowerEventPort` báo
> suspend cho heartbeat và activity. Heartbeat chỉ cộng phần trước sleep vào `ActiveSeconds`; activity bỏ
> khoảng sleep và reset cửa sổ sample, nên không ghi sample cho thời gian máy ngủ và không cộng gap vào
> sample kế tiếp. Gap vượt `MaxActiveDelta`/`MaxSampleGap` mà không có sự kiện cũng bị bỏ. Thời lượng vẫn
> tính bằng monotonic clock, nên chỉnh đồng hồ tiến không làm phồng `ActiveSeconds`/`DurationSeconds`;
> đồng hồ lùi được clamp để timestamp không đảo.

---

### UC08 — Ước lượng mức độ tập trung

**Tác nhân:** Hệ thống
**Mô tả:** Dùng webcam để ước lượng học sinh có đang nhìn màn hình hay không.

**Tiền điều kiện**
- Protection ON.
- StudySession active.
- Monitoring được bật.
- (Nếu `MonitorOnlyDuringClasses`) app đang phát hiện lớp online.
- Camera được cấu hình; nếu không khả dụng, UC08 vẫn chạy nhánh `CameraUnavailable`.

**Trigger**
- Timer sample mỗi 1–2 giây (xử lý), tổng hợp mỗi 10 giây.
- Bắt đầu/kết thúc lớp online khi `MonitorOnlyDuringClasses` bật.

**Luồng chính**
1. Hệ thống mở camera với resolution thấp (640×480), sampling 1–2 FPS.
2. Với mỗi frame:
   - Phát hiện khuôn mặt.
   - Ước lượng head pose (yaw/pitch).
   - Ước lượng iris/gaze (coarse).
   - Tính eye aspect ratio.
3. Hệ thống phân loại trạng thái:
   - `Focused`: có mặt, đầu hướng màn hình, mắt mở, gaze on-screen.
   - `Distracted`: gaze/head lệch > ngưỡng trong > 20 giây.
   - `Absent`: không thấy mặt > 30 giây.
   - `Drowsy`: mắt nhắm lặp lại > 2 giây.
   - `CameraUnavailable`: không mở được camera.
4. Khi trạng thái thay đổi, tạo/cập nhật `AttentionEvent`.
5. Hệ thống cập nhật thời gian focused/distracted/absent/drowsy, `MonitoredSeconds`, `MonitoringEligibleSeconds` và attention score.
6. Frame bị hủy ngay sau xử lý; không ghi disk/network.
7. `ConfidenceAvg` là trung bình có trọng số theo thời lượng event; event confidence thấp không được dùng để kết luận `Distracted`.

**Calibration**
- Tùy chọn: học sinh nhìn 5 điểm trên màn hình 10–15 giây.
- Nếu chưa calibration: chỉ phân loại nhị phân on-screen/off-screen, confidence thấp hơn.

**Luồng thay thế**
- **Camera bận (Meet/Teams đang dùng):**
  - Ghi `CameraUnavailable`, thử mở lại sau 30 giây.
  - Không tính là mất tập trung.
  - `MonitoringEligibleSeconds` vẫn tăng; báo cáo hiển thị coverage giảm.
- **Camera không có quyền:** hiển thị hướng dẫn bật quyền Windows; ghi `CameraUnavailable`.
- **Không có webcam:** ghi `CameraUnavailable` cho phiên, tắt retry, protection vẫn hoạt động; báo phụ huynh.
- **Nhiều khuôn mặt:** lấy khuôn mặt lớn nhất; ghi confidence thấp; không nhận diện danh tính.
- **Ánh sáng yếu / đeo kính:** confidence thấp; không kết luận mất tập trung nếu confidence dưới ngưỡng.

**Ngoại lệ**
- App crash khi camera đang mở: camera được nhả bởi OS; event đang mở được đóng tại `LastHeartbeatAtUtc` khi phục hồi.
- Session/monitoring dừng: đóng event đang mở tại thời điểm dừng; không cộng thời gian sau đó.
- Học sinh che camera: `Absent` nếu camera vẫn mở; `CameraUnavailable` nếu không mở được; ghi nhận, không tự động phạt.

**Hậu điều kiện**
- Attention event và score được lưu.
- Không có ảnh/video nào bị lưu.

**Business rules:** BR-12, BR-13, BR-14, BR-15.

> **Triển khai (issue #27):** policy `MonitoringEligibility.IsEligible(monitoringEnabled,
> monitorOnlyDuringClasses, isOnlineClass)` đã có trong domain để UC08 quyết định mỗi tick attention có cộng
> `MonitoringEligibleSeconds` hay không. Việc cộng eligible/monitored theo tick và tính coverage thuộc
> issue #33; policy được test với cùng 40 giây monitored cho coverage 20% (mọi lúc) và 80% (chỉ trong lớp).

> **Triển khai (issue #30):** `ICameraSessionUseCase`/`CameraSessionService` chọn camera đã lưu trong
> `AppSettings.CameraDeviceId` hoặc camera mặc định, mở/đóng tuần tự và bọc open/list/capture trong timeout
> 5 giây. Camera bận/không quyền/mất kết nối → `CameraUnavailable`, tự mở lại sau 30 giây (backoff nhân đôi
> tới 5 phút); lỗi capture lẻ không kết luận mất tập trung, chỉ 3 lỗi liên tiếp hoặc `Disconnected` mới đóng
> thiết bị. `CameraStatusViewModel` hiển thị trạng thái và hướng dẫn quyền trong cửa sổ chính/tray. Việc
> phân tích frame, ghi `AttentionEvent`/`CameraUnavailable` và cộng thời gian monitored thuộc #31–#33.

> **Triển khai (issue #32):** `AttentionMonitorService` biến `AttentionSample` thành state ổn định qua
> `AttentionStateWindow` (cửa sổ 6 giây, tối thiểu 3 mẫu, majority ≥ 60%) rồi `AttentionStateMachine`
> (Focused ngay, Distracted > 20 giây, Absent > 30 giây, Drowsy > 2 giây). `AttentionEvent` chỉ mở/đóng khi
> state đổi; `CameraUnavailable` ghi nhận ngay và không vào mẫu số attention; inference lỗi không bị tính là
> `Absent`; gap > 5 giây không cộng thời gian. `ConfidenceAvg` được cập nhật trọng số trong cùng `UPDATE`
> với counter. Crash đóng event đang mở tại `LastHeartbeatAtUtc`. Việc start/stop monitor theo phiên học,
> cộng `MonitoringEligibleSeconds` và tính coverage thuộc #33.

> **Triển khai (issue #31):** `AttentionPipelineService` chạy vòng sampling 1 FPS qua `ICameraSessionUseCase`,
> gọi `IAttentionFrameAnalyzer` trên từng `CameraFrame` và phát `AttentionSample` sau khi đã dispose frame.
> Sample camera lỗi có `CameraFailure` + `Observation = null`; sample inference lỗi có `Error` nhưng giữ
> `CameraFailure = None`, để monitor (#32) không biến lỗi kỹ thuật thành `Absent`. `OpenCvAttentionFrameAnalyzer`
> không còn mở camera; nó chỉ nhận frame BGR24 managed, tính luminance và chạy YuNet/Haar. Classifier,
> state machine và `AttentionEvent` đã có ở #32; coverage/eligibility thuộc #33.

---

### UC09 — Trang chặn & chặn ứng dụng

**Tác nhân:** Học sinh, Hệ thống
**Mô tả:** Học sinh truy cập domain hoặc mở app bị chặn; hệ thống chặn, hiển thị thông báo và ghi log khi quan sát được.

**Tiền điều kiện**
- Protection ON.
- Domain/app khớp rule Block và không có Allow override.

**Trigger**
- Học sinh mở URL trong trình duyệt.
- Hoặc app bị chặn được phát hiện ở foreground/process start.

**Luồng chính**
1. Hosts file trỏ domain về `127.0.0.1` và `::1`; chỉ áp domain exact của rule đang bật — không tự thêm biến thể `www.`.
2. `BlockPageServer` bind `127.0.0.1:80` bằng TCP listener loopback, chỉ nhận GET/HEAD và trả HTML tĩnh; request khác hoặc Host header lạ bị từ chối.
3. Trang chặn hiển thị:
   - "Trang này không nằm trong môi trường học tập."
   - Tên miền bị chặn (đã HTML-escape).
   - Hướng dẫn nhờ phụ huynh.
   - Không tải font/ảnh/script từ ngoài; không chứa dữ liệu nhạy cảm.
   - Không hiển thị nội dung gây xấu hổ/guilt-trip.
4. Khi quan sát được, hệ thống tạo `BlockedAttempt`:
   - Timestamp.
   - Domain/URL (URL chỉ khi có extension).
   - Process/window nếu xác định được.
   - SessionId.
5. Hệ thống debounce: không ghi trùng cùng domain trong 30 giây.
6. Học sinh quay lại học hoặc nhờ phụ huynh.

**Luồng chặn ứng dụng**
1. `ProcessWatcher` phát hiện process khớp rule Block, không có rule Allow, trong khi Protection ON.
2. Gửi `WM_CLOSE` tới cửa sổ chính của app; chờ tối đa 2 giây.
3. Nếu process vẫn chạy, kill process tree của app đó.
4. Ghi `BlockedAttempt` với `TargetType = App`, `ObservedVia = ProcessWatcher`, process name và `SessionId` nếu có.
5. Hiển thị thông báo ngắn "Ứng dụng này bị chặn trong giờ học"; không dùng trang chặn web cho app.
6. Debounce 30 giây theo process để tránh spam log/thông báo.

**Luồng thay thế**
- **HTTPS:** trình duyệt thường báo lỗi kết nối/SSL thay vì trang chặn; domain vẫn bị chặn. Hosts không tạo log HTTPS; blocked attempt có thể không được ghi.
- **Port 80 bị chiếm:** blocking vẫn hoạt động; block page không khả dụng; ghi cảnh báo.
- **Giới hạn hiển thị:** màn hình Protection hiển thị cảnh báo thường trực về HTTPS/DoH/VPN/proxy và về việc block page không khả dụng khi cổng 80 bị chiếm.
- **Allowlist:** nếu rule Allow còn hiệu lực, không chặn, không ghi blocked attempt.
- **Parent override:** phụ huynh có thể thêm allowlist tạm thời từ app; block page không có nút mở khóa trực tiếp để tránh học sinh tự bỏ chặn.

**Ngoại lệ**
- Domain dùng chung (Google/Microsoft): không chặn; có allowlist mặc định.
- Học sinh dùng VPN/DoH: hosts có thể bị bypass; ghi log cấu hình mạng nếu phát hiện; không tuyên bố chặn tuyệt đối.
- Request không phải loopback hoặc Host header lạ: từ chối, không phục vụ.
- App không cho phép truy cập process: bỏ qua, ghi log mức debug; không crash.
- App không có cửa sổ chính hoặc `WM_CLOSE` không hiệu lực: kill process tree sau 2 giây.
- App bị chặn đang có dữ liệu chưa lưu: việc kill có thể làm mất dữ liệu; thông báo cho người dùng và ghi rõ trong known limitations.

**Hậu điều kiện**
- Blocked attempt được lưu nếu quan sát được.
- Trang chặn hiển thị với HTTP; HTTPS bị chặn nhưng browser báo lỗi.
- App bị chặn đã đóng hoặc bị kill; process hệ thống/EduFocus/Allow không bị tác động.

**Business rules:** BR-09, BR-10, BR-23, BR-24, BR-26, BR-29.

> **Triển khai (issue #21):** `HostsFileBlocker` sở hữu khối marker trong hosts; chỉ chấp nhận
> domain exact và ghi cả IPv4 `127.0.0.1` lẫn IPv6 `::1`, không tự sinh biến thể `www.`. Backup +
> `journal.json` nằm trong `%ProgramData%\EduFocus\hosts-backup`; restore khôi phục exact khi ngoài
> marker chưa bị sửa, ngược lại chỉ gỡ marker để giữ chỉnh sửa của người dùng. Lỗi quyền/khóa file
> được báo bằng `HostsFileException` để use case không coi là đã tắt; log chỉ ghi số domain.
> `ProtectionEngine` (issue #24) gọi apply/restore/self-heal adapter này khi bật, tắt và reconcile.
>
> **Triển khai block page (issue #22):** `BlockPageServer` dùng `TcpListener` bind `127.0.0.1:80`
> (cổng 0 cho test) thay vì `HttpListener` nên không cần URL ACL; chỉ phục vụ GET/HEAD và chỉ trả
> trang khi Host header khớp domain exact đang chặn. Trang tĩnh escape domain, không tài nguyên
> ngoài, kèm CSP `default-src 'none'`, `no-store`, `nosniff`, `X-Frame-Options: DENY`,
> `Referrer-Policy: no-referrer`. Cổng bận/thiếu quyền trả warning để engine giữ Protection ON và UI
> cảnh báo. Mỗi request bị chặn phát `BlockPageHit` (domain + UTC); `ProtectionEngine` (issue #24)
> debounce 30 giây theo domain rồi ghi `BlockedAttempt` với `ObservedVia = BlockPage`.
>
> **Triển khai process watcher (issue #23):** `ProcessWatcher` polling mỗi 2 giây qua
> `IProcessController` (`WindowsProcessController` dùng `Process.GetProcesses`), chỉ xét process có
> cửa sổ chính và khớp **chính xác** tên đã chuẩn hóa (bỏ path/`.exe`, lowercase); allowlist thắng
> blocklist. Process hệ thống quan trọng, PID ≤ 4, process EduFocus và process không có cửa sổ chính
> bị bỏ qua. Trước khi kill, watcher gửi `WM_CLOSE` và chờ tối đa 2 giây; quá hạn mới kill process
> tree và cảnh báo dữ liệu chưa lưu có thể bị mất. Attempt được debounce 30 giây theo tên process
> bằng monotonic clock; `ProcessBlockedObservation` (tên, PID, kết quả, cảnh báo) được
> `ProtectionEngine` (issue #24) debounce/ghi `BlockedAttempt` với `ObservedVia = ProcessWatcher`.
> Log không ghi window title, path hay tài liệu.
>
> **Triển khai engine (issue #24):** `ProtectionEngine` ghép block page → hosts → watcher khi bật
> (listener sẵn sàng trước khi hosts trỏ domain về loopback) và gỡ rule watcher/page trước khi dừng
> adapter + restore hosts khi tắt. Cổng 80 bận chỉ là cảnh báo; lỗi adapter khác khi bật rollback phần
> đã áp và ném `ProtectionEngineException`; hosts restore thất bại khi tắt ném lỗi để Protection giữ
> ON. Một writer channel ghi `BlockedAttempt` kèm `SessionId` đang hoạt động và tăng
> `BlockedAttemptCount`; debounce trung tâm 30 giây theo domain/tên app bằng monotonic clock, lỗi ghi
> DB chỉ log. Thay đổi rule khi đang ON chưa tự áp dụng lại.

---

### UC10 — Xem dashboard & báo cáo

**Tác nhân:** Phụ huynh, Học sinh
**Mô tả:** Xem tổng quan học tập và chi tiết theo ngày/tuần.

**Tiền điều kiện**
- Ứng dụng đang chạy.
- Có dữ liệu session.

**Trigger**
- Mở tab **Tổng quan** hoặc **Báo cáo**.

**Luồng chính — Student summary**
1. Học sinh thấy:
   - Trạng thái bảo vệ.
   - Thời gian học hôm nay.
   - Thời gian còn lại của khung giờ (nếu có).
2. Không thấy chi tiết vi phạm, blocklist hay báo cáo tuần.

**Luồng chính — Parent dashboard**
1. Phụ huynh đăng nhập Parent Mode.
2. Hệ thống mặc định hiển thị **hôm nay**:
   - Tổng thời gian học.
   - % tập trung (kèm coverage).
   - Số lần mất tập trung/vắng mặt.
   - Số lần bị chặn (best-effort quan sát được).
   - Timeline theo giờ.
3. Phụ huynh chọn bộ lọc:
   - Hôm nay / 7 ngày / 30 ngày: dữ liệu chi tiết.
   - 90 ngày / 365 ngày: chỉ session summary và biểu đồ tổng hợp.
4. Hệ thống query dữ liệu và render:
   - Biểu đồ thời gian học theo ngày.
   - Danh sách sự kiện attention (nếu còn trong retention chi tiết).
   - Danh sách blocked attempts (nếu còn; ghi rõ best-effort).
   - Top app/domain theo thời gian (nếu còn sample).
5. Phụ huynh có thể xuất CSV theo phạm vi đang xem.

**Luồng thay thế**
- **Chưa có dữ liệu:** hiển thị empty state hướng dẫn bật bảo vệ.
- **Camera không khả dụng nhiều:** hiển thị cảnh báo coverage thấp; không hiển thị % tập trung như dữ liệu chắc chắn.
- **Session đang chạy:** dashboard cập nhật realtime mỗi 10–30 giây.
- **Dữ liệu chi tiết đã hết retention:** chỉ hiển thị summary; không báo thiếu dữ liệu như lỗi.

**Ngoại lệ**
- Query lỗi: hiển thị lỗi, không crash.
- Dữ liệu quá lớn: phân trang/tổng hợp theo ngày.

**Hậu điều kiện**
- Không thay đổi dữ liệu (trừ export).

**Business rules:** BR-15, BR-21.

---

### UC11 — Quản lý dữ liệu & quyền riêng tư

**Tác nhân:** Phụ huynh
**Mô tả:** Xem chính sách, xóa dữ liệu, cấu hình retention.

**Tiền điều kiện**
- Parent Mode.

**Trigger**
- Phụ huynh mở **Dữ liệu & Quyền riêng tư**.

**Luồng chính**
1. Hệ thống hiển thị:
   - Dữ liệu đang lưu (session, sample, event, blocked attempt).
   - Không có ảnh/video.
   - Retention hiện tại.
2. Phụ huynh có thể:
   - Đổi retention trong giới hạn cho phép.
   - Xóa dữ liệu theo khoảng thời gian.
   - Xuất dữ liệu CSV.
   - Xem audit log thao tác.
3. Xóa dữ liệu yêu cầu xác nhận và PIN.
4. Hệ thống xóa đúng phạm vi, ghi audit.
5. Retention job chạy mỗi ngày, xóa dữ liệu quá hạn.

**Luồng thay thế**
- **Xóa toàn bộ:** yêu cầu nhập lại PIN + gõ "XÓA".
- **Đang có session active:** không cho xóa session hiện tại; chỉ xóa dữ liệu cũ.

**Ngoại lệ**
- File DB bị khóa: báo lỗi, thử lại sau.
- Xóa một phần lỗi: ghi log, không báo thành công giả.

**Hậu điều kiện**
- Dữ liệu được xóa theo yêu cầu hoặc retention.

**Business rules:** BR-13, BR-16.

**Hiện trạng issue #29:** retention job tự động đã chạy: `RetentionService` đọc hạn trong `AppSettings`, xóa
từng bảng theo batch 500 (sample/event/blocked attempt/audit/session), bỏ qua session `Active` và event còn
mở, ghi audit `RetentionApplied` kèm số bản ghi và danh sách bảng lỗi. Job chạy lúc khởi động và mỗi ngày khi
local time ≥ 03:00, sau đó `PRAGMA incremental_vacuum`. Đổi retention, xóa theo khoảng thời gian, xóa toàn bộ,
xuất CSV và audit viewer trong màn hình **Dữ liệu & Quyền riêng tư** vẫn chưa có UI/use case — thuộc phần còn
lại của UC11.

---

### UC12 — Vòng đời ứng dụng & chống tắt

**Tác nhân:** Phụ huynh, Học sinh, Hệ thống
**Mô tả:** Quản lý đóng/mở/khởi động lại app và chống tắt trái phép.

**Tiền điều kiện**
- Ứng dụng đang chạy.

**Trigger**
- Người dùng đóng cửa sổ, thoát từ tray, khởi động lại Windows, hoặc app bị kill.

**Luồng chính — Đóng cửa sổ**
1. Người dùng bấm X hoặc Alt+F4.
2. Nếu Protection ON:
   - Ẩn cửa sổ xuống tray.
   - Hiển thị thông báo "Ứng dụng vẫn đang bảo vệ".
3. Nếu Protection OFF:
   - Ẩn xuống tray (không thoát) để tránh tắt nhầm.

**Luồng chính — Thoát ứng dụng**
1. Người dùng chọn **Thoát** từ tray.
2. Hệ thống luôn yêu cầu PIN, kể cả khi Protection OFF.
3. Nếu Protection ON, cảnh báo rõ việc thoát sẽ tắt bảo vệ và yêu cầu xác nhận lần hai.
4. Nếu hợp lệ:
   - Dừng monitoring.
   - Kết thúc session `Completed`.
   - Gỡ block và hosts.
   - Đặt `ProtectionEnabled = false` nếu đang ON; xóa `ScheduleOverrideUntilUtc`.
   - Ghi `LastCleanShutdownAtUtc` và đánh dấu clean exit.
   - Đóng app; watchdog không khởi động lại sau exit sạch.

**Luồng chính — Khởi động cùng Windows**
1. Khi Protection ON hoặc ScheduleEnabled, app đăng ký auto-start.
2. Khi user đăng nhập Windows, app khởi động ẩn xuống tray.
3. Nếu Protection ON trước khi tắt máy, app tự bật lại protection.

**Luồng thay thế — App bị kill**
1. Watchdog (scheduled task/service) phát hiện process chết.
2. Chỉ khởi động lại khi `ProtectionEnabled = true` và lần thoát trước không phải clean exit.
3. Khởi động lại app trong vòng khoảng 60 giây (Task Scheduler repetition tối thiểu 1 phút; có thể trễ khi máy tải cao hoặc sleep).
4. Áp dụng lại protection nếu trạng thái trước đó là ON.
5. Ghi audit "unexpected termination".

**Ngoại lệ**
- Windows shutdown/logoff: app không gỡ chặn và không đổi trạng thái bảo vệ; chỉ cố gắng đóng session với `Interrupted`/`Note = SystemShutdown` và ghi audit trong thời gian ngắn (tối đa 2 giây) trước khi tiến trình bị dừng. Không đặt `LastCleanShutdownAtUtc` và không xóa `ProtectionEnabled`, để lần đăng nhập sau phục hồi bảo vệ; nếu không kịp đóng session thì reconcile lúc khởi động self-heal. Đây là đường bypass được chấp nhận và phải ghi rõ trong tài liệu.
- Học sinh kill từ Task Manager: nếu có quyền admin, có thể kill; watchdog khởi động lại; ghi nhận.
- Uninstall: yêu cầu PIN/cảnh báo nếu Protection ON ở mức best-effort; admin Windows vẫn có thể gỡ (giới hạn đã biết).

**Hậu điều kiện**
- App chạy nền hoặc thoát hợp lệ.
- Protection state được bảo toàn.

**Business rules:** BR-17, BR-18, BR-19, BR-28, BR-30.

> **Hiện trạng Phase 2 (issue #18):** đóng cửa sổ luôn ẩn xuống tray, kèm balloon nhắc bảo vệ đang
> chạy; thoát app chỉ từ menu tray và luôn yêu cầu PIN qua `ExitWindow`. `AppLifecycleService`
> xác thực PIN, gỡ chặn (`DisableForExitAsync`, không tạo `ScheduleOverrideUntilUtc` và xóa override
> còn sót), kết thúc session `Completed`, ghi `LastCleanShutdownAtUtc`, audit `ProtectionOff` +
> `CleanExit` và luôn khóa Parent Mode sau mỗi lần thử. Khi bảo vệ ON, dialog hiển thị cảnh báo và
> bắt buộc xác nhận lần hai; gỡ chặn thất bại giữ app chạy với bảo vệ nguyên vẹn, còn lỗi ghi dấu
> thoát sạch chỉ tạo cảnh báo vì reconcile lúc khởi động tự dọn. `App.OnSessionEnding` không hủy
> Windows shutdown, chỉ ghi nhận best-effort trong 2 giây: session `Interrupted` với ghi chú
> `SystemShutdown`, audit `SessionInterrupted`/`SystemShutdown`, giữ nguyên `ProtectionEnabled` và
> engine; lần đăng nhập sau `ReconcileAsync` phục hồi bảo vệ với session `Recovery`. Watchdog tự
> khởi động lại sau kill vẫn thuộc Phase 7.

---

### UC13 — Đổi PIN / Khôi phục PIN

**Tác nhân:** Phụ huynh, Hệ thống
**Mô tả:** Thay đổi PIN định kỳ hoặc khôi phục khi quên.

**Tiền điều kiện**
- Có `ParentCredential`.

**Trigger**
- Phụ huynh mở **Đổi PIN** hoặc **Quên PIN**.

**Luồng chính — Đổi PIN**
1. Phụ huynh nhập PIN hiện tại.
2. Nhập PIN mới + xác nhận.
3. Hệ thống validate như UC01.
4. Cập nhật hash/salt, reset failed attempts, ghi audit.

**Luồng chính — Khôi phục bằng recovery code**
1. Phụ huynh chọn **Quên PIN**.
2. Nhập recovery code.
3. Hệ thống xác thực hash; dùng chung `FailedAttempts`/lockout với PIN.
4. Cho phép đặt PIN mới.
5. Vô hiệu hóa recovery code cũ, tạo recovery code mới và hiển thị một lần. Hệ thống không thể hiển thị lại mã cũ.

**Luồng thay thế — Mất cả PIN và recovery code**
1. Chỉ có thể reset bằng quyền Administrator Windows.
2. Hệ thống cảnh báo: reset sẽ tắt protection và xóa credential.
3. Phụ huynh cài đặt lại PIN; dữ liệu báo cáo có thể được giữ hoặc xóa theo lựa chọn.
4. Ghi audit.

**Ngoại lệ**
- Recovery code sai quá 5 lần: khóa 15 phút, dùng chung lockout với PIN.
- DB lỗi: không thay đổi credential.
- Recovery code đã dùng: vô hiệu, không thể dùng lại.

**Hậu điều kiện**
- PIN mới có hiệu lực; recovery code mới được tạo.

**Business rules:** BR-02, BR-03.

---

### UC14 — Tự phục hồi sau crash/restart

**Tác nhân:** Hệ thống
**Mô tả:** Khôi phục trạng thái bảo vệ và session sau khi app hoặc máy bị tắt đột ngột.

**Tiền điều kiện**
- Có trạng thái `ProtectionEnabled = true` hoặc session `Active` trong DB.

**Trigger**
- App khởi động.
- Windows resume từ sleep.

**Luồng chính**
1. App khởi động, kiểm tra DB:
   - Protection state.
   - Session chưa kết thúc.
   - Block rules.
2. Đóng session cũ tại `LastHeartbeatAtUtc` với trạng thái `Interrupted`; đóng mọi `AttentionEvent` đang mở tại cùng thời điểm.
3. Nếu Protection ON:
   - Self-heal hosts (chỉ xóa block của EduFocus, không xóa block khác).
   - Áp dụng lại blocklist.
   - Tạo session mới với `Trigger = Recovery` nếu vẫn trong thời gian bảo vệ; không cộng thời gian app tắt.
   - Khởi động activity/attention monitor nếu được bật.
4. Nếu phát hiện block mồ côi:
   - Xóa block cũ của EduFocus.
   - Áp dụng lại theo trạng thái hiện tại.
5. Ghi audit "recovery completed".
6. Hiển thị banner cho phụ huynh nếu có gián đoạn.

**Luồng thay thế**
- **Protection OFF:** chỉ dọn block mồ côi, không bật lại; session cũ vẫn `Interrupted`.
- **Session cũ đã quá stale (> 24h):** vẫn `Interrupted`, không cộng thời gian; không tạo session Recovery nếu ngoài lịch.
- **DB hỏng:** backup file lỗi, tạo DB mới, cảnh báo phụ huynh.

**Ngoại lệ**
- Không có quyền admin: protection không thể khôi phục; hiển thị cảnh báo và yêu cầu chạy elevated.
- Camera bận: attention monitor ghi `CameraUnavailable`.

**Hậu điều kiện**
- Trạng thái bảo vệ và session được khôi phục nhất quán.
- Không còn block mồ côi.

**Business rules:** BR-17, BR-20.

> **Triển khai (issue #25):** `ReconcileAsync` dừng heartbeat, đóng session cũ tại `LastHeartbeatAtUtc`
> (`Interrupted`) rồi tạo phiên `Recovery` từ thời điểm hiện tại và khởi động lại heartbeat. Mọi thao tác
> enable/disable/reconcile được serialize bằng semaphore để hai luồng không tạo hai session hoặc hai
> heartbeat; unique partial index vẫn là chốt cuối ở tầng DB.

> **Triển khai (issue #28):** resume từ sleep không tạo session mới khi app vẫn chạy: heartbeat và activity
> nhận `Resumed`, bỏ khoảng sleep và tiếp tục trên session `Active` hiện tại. Thời gian sleep không vào
> `ActiveSeconds` và không sinh sample; nếu app bị tắt trong lúc sleep thì lần khởi động sau đi theo luồng
> crash/recovery ở trên, đóng session tại `LastHeartbeatAtUtc`.

---

## 5. Luồng xuyên suốt: Một buổi học điển hình

```text
18:55  Phụ huynh mở app → Parent Login (PIN)
18:57  Bật Protection ON
18:58  Hệ thống chặn chatbot, bắt đầu session, bật camera
19:00  Học sinh vào Google Meet → activity sample "Online class"
19:15  Học sinh mở chatgpt.com → bị chặn (HTTPS: browser báo lỗi; BlockedAttempt có thể không ghi)
19:20  Học sinh rời màn hình 45 giây → AttentionEvent "Absent"
19:45  Học sinh quay lại, tiếp tục học → AttentionEvent "Focused"
20:00  Hết khung giờ schedule → Protection OFF, session Completed
20:05  Phụ huynh xem báo cáo: 60 phút học, 92% coverage, blocked attempts quan sát được
```

---

## 6. Quy tắc chung khi viết Use Case

- Mọi thời gian lưu DB là UTC; hiển thị theo local time.
- Mọi thao tác nhạy cảm phải ghi audit log (không ghi PIN).
- Không có thao tác nào được âm thầm bật camera khi chưa thông báo.
- Khi có lỗi, ưu tiên trạng thái an toàn:
  - Không chặn được → báo lỗi, không giả vờ ON.
  - Không mở được camera → ghi `CameraUnavailable`, không kết luận mất tập trung.
- Các thay đổi rule/state phải có hiệu lực ngay hoặc thông báo rõ khi nào có hiệu lực.

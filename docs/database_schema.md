# Cấu trúc Cơ sở dữ liệu - EduFocus

> **Trạng thái:** Thiết kế đích / schema mới cho EduFocus.
> **Phiên bản:** 2.0 — 2026-10-26
> **DB engine:** SQLite + EF Core 10
> **Tên file:** `%ProgramData%\EduFocus\edufocus.db`

---

## 1. Nguyên tắc thiết kế

1. **Local-first:** chỉ lưu trên máy, không đồng bộ cloud.
2. **Data minimization:** không lưu ảnh, video, âm thanh, nội dung trang hay lịch sử duyệt web đầy đủ.
3. **UTC storage:** mọi timestamp lưu UTC dạng ISO-8601 (`yyyy-MM-ddTHH:mm:ss.fffZ`); hiển thị theo local time.
4. **Local date snapshot:** lưu thêm `StartLocalDate` cho session để nhóm báo cáo theo ngày địa phương, tránh lỗi DST/timezone.
5. **Immutable history:** session đã kết thúc không bị sửa ngoài việc tính lại tổng hợp khi cần.
6. **Retention:** dữ liệu chi tiết bị xóa theo hạn; session summary giữ lâu hơn.
7. **No secrets in plaintext:** PIN/recovery code chỉ lưu hash + salt.

> **Không migrate dữ liệu từ `DontBeLazy` 1.x.** EduFocus dùng DB mới. Nếu cần giữ blocklist tùy chỉnh, cung cấp công cụ import riêng (post-MVP).

---

## 2. Sơ đồ quan hệ

```text
ParentCredentials (1)

AppSettings (1)

BlockRules (N)

ScheduleWindows (N)

StudySessions (1) ──< ActivitySamples (N)
        │
        ├──< AttentionEvents (N)
        │
        └──< BlockedAttempts (N, nullable)

AuditLogs (N, độc lập)
```

- `StudySessions` là trung tâm của dữ liệu báo cáo.
- `ActivitySamples`, `AttentionEvents`, `BlockedAttempts` là dữ liệu chi tiết, có thể xóa theo retention mà vẫn giữ session summary.
- `BlockedAttempts.SessionId` nullable để giữ log khi session bị xóa.

---

## 3. Quy ước kiểu dữ liệu

| Kiểu logic | SQLite | Ghi chú |
|---|---|---|
| Guid / Id | `TEXT(36)` | UUID dạng chuỗi |
| Bool | `INTEGER` | 0/1 |
| Enum | `TEXT(32)` | Lưu tên enum, dễ đọc |
| DateTime | `TEXT(28)` | ISO-8601 UTC |
| Date | `TEXT(10)` | `yyyy-MM-dd` local |
| Thời lượng | `INTEGER` | Giây |
| Điểm % | `REAL` | 0.0–100.0 |
| JSON | `TEXT` | Metadata/audit |

**Index chung**
- Mọi FK đều có index.
- Index theo thời gian cho bảng lớn.
- Composite index cho truy vấn timeline.

---

## 4. Bảng `AppSettings`

Singleton — ứng dụng đảm bảo chỉ có 1 bản ghi với `Id` cố định.

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID cố định |
| `ProtectionEnabled` | INTEGER | NOT NULL, default 0 | Trạng thái bảo vệ hiện tại; exit hợp lệ đặt 0, Windows shutdown giữ nguyên |
| `ProtectionSource` | TEXT(16) | NOT NULL, default `'None'` | Nguồn bật bảo vệ: `None` / `Manual` / `Schedule`; scheduler chỉ tự tắt khi nguồn là `Schedule` |
| `MonitoringEnabled` | INTEGER | NOT NULL, default 1 | Bật attention monitoring |
| `ScheduleEnabled` | INTEGER | NOT NULL, default 0 | Bật lịch tự động |
| `MonitorOnlyDuringClasses` | INTEGER | NOT NULL, default 0 | Chỉ monitor khi phát hiện Meet/Teams/Zoom |
| `CameraDeviceId` | TEXT(256) | NULL | Thiết bị camera được chọn |
| `DistractionThresholdSeconds` | INTEGER | NOT NULL, default 20 | Ngưỡng chuyển sang Distracted |
| `AbsenceThresholdSeconds` | INTEGER | NOT NULL, default 30 | Ngưỡng chuyển sang Absent |
| `SampleIntervalSeconds` | INTEGER | NOT NULL, default 15 | Tần suất ghi ActivitySample |
| `ActivityRetentionDays` | INTEGER | NOT NULL, default 30 | Giữ ActivitySample |
| `EventRetentionDays` | INTEGER | NOT NULL, default 90 | Giữ AttentionEvent/BlockedAttempt |
| `SessionRetentionDays` | INTEGER | NOT NULL, default 365 | Giữ StudySession |
| `StartWithWindows` | INTEGER | NOT NULL, default 1 | Auto-start |
| `UpdateCheckEnabled` | INTEGER | NOT NULL, default 0 | Opt-in kiểm tra phiên bản qua GitHub Releases (migration `AddUpdateCheckOptIn`) |
| `Theme` | TEXT(16) | NOT NULL, default `'dark'` | `dark` / `light` / `system` |
| `Language` | TEXT(8) | NOT NULL, default `'vi'` | `vi` / `en` (dự kiến) |
| `OnboardingCompleted` | INTEGER | NOT NULL, default 0 | Chỉ `1` khi đã có PIN và đủ hai xác nhận quyền riêng tư |
| `PrivacyAcceptedAtUtc` | TEXT(28) | NULL | Thời điểm phụ huynh xác nhận đã đọc thông báo quyền riêng tư |
| `GuardianAcknowledgedAtUtc` | TEXT(28) | NULL | Thời điểm phụ huynh xác nhận đã thông báo cho học sinh/gia đình |
| `ScheduleOverrideUntilUtc` | TEXT(28) | NULL | Hết hiệu lực tạm tắt schedule |
| `LastCleanShutdownAtUtc` | TEXT(28) | NULL | Thời điểm phụ huynh thoát app hợp lệ; không set khi Windows shutdown |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |
| `UpdatedAtUtc` | TEXT(28) | NOT NULL | |

**Ghi chú**
- `ProtectionEnabled` là nguồn sự thật cho trạng thái ON/OFF.
- `ProtectionSource` phân biệt bảo vệ do lịch bật với bảo vệ phụ huynh bật thủ công. Bật thủ công đặt `Manual` và xóa override; scheduler không tự tắt bảo vệ `Manual` khi hết khung giờ. Migration `AddProtectionSource` backfill `Manual` cho các bản ghi cũ đang bật.
- Khi app crash, lần khởi động sau đọc cờ này để phục hồi.
- Hai mốc consent được ghi cùng lúc khi phụ huynh xác nhận cả hai mục ở onboarding; `OnboardingCompleted` chỉ được đặt khi `ParentCredentials` tồn tại và cả hai mốc đã có. Mốc đầu tiên được giữ nguyên nếu onboarding được tiếp tục sau restart.
- Nếu có `ParentCredentials` nhưng `OnboardingCompleted = 0`, app coi onboarding đang dang dở: phụ huynh nhập PIN để tạo lại recovery code (mã cũ chỉ lưu hash nên không thể hiển thị lại) rồi hoàn tất.
- `ScheduleOverrideUntilUtc` dùng cho trường hợp phụ huynh tắt tạm trong khung giờ.

---

## 5. Bảng `ParentCredentials`

Singleton.

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID cố định |
| `PinHash` | TEXT(128) | NOT NULL | Base64 PBKDF2 hash (32 byte) |
| `PinSalt` | TEXT(64) | NOT NULL | Base64 salt (16 byte) |
| `PinIterations` | INTEGER | NOT NULL, ≥ 600000 | Số vòng PBKDF2 |
| `RecoveryCodeHash` | TEXT(128) | NOT NULL | Hash recovery code |
| `RecoveryCodeSalt` | TEXT(64) | NOT NULL | Salt riêng |
| `RecoveryCodeIterations` | INTEGER | NOT NULL, ≥ 600000 | |
| `FailedAttempts` | INTEGER | NOT NULL, default 0 | Số lần sai liên tiếp |
| `LockoutUntilUtc` | TEXT(28) | NULL | Khóa đến thời điểm |
| `PinUpdatedAtUtc` | TEXT(28) | NOT NULL | |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |

**Quy tắc**
- Không lưu PIN/recovery code plaintext.
- `FailedAttempts` dùng chung cho PIN và recovery code; reset khi đăng nhập thành công.
- Sai 5 lần (PIN hoặc recovery code) → `LockoutUntilUtc = now + 15 phút`.
- Khi đổi PIN, tạo salt mới; không tái sử dụng hash cũ.
- Recovery code chỉ hiển thị một lần khi tạo/đổi; DB chỉ giữ hash, không thể hiển thị lại.
- Issue #13 dùng PBKDF2-SHA256 600.000 iterations, salt 16 byte, hash 32 byte; recovery code 12 ký tự được chuẩn hóa trước khi hash. Không cần migration mới.

---

## 6. Bảng `BlockRules`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `RuleType` | TEXT(16) | NOT NULL | `Domain` / `App` (MVP); `Url` post-MVP |
| `MatchType` | TEXT(16) | NOT NULL | `Exact` (MVP); `Suffix` / `Contains` post-MVP |
| `Value` | TEXT(512) | NOT NULL | Giá trị gốc người dùng nhập |
| `NormalizedValue` | TEXT(512) | NOT NULL | Chuẩn hóa lowercase, bỏ scheme/path thừa |
| `Category` | TEXT(32) | NOT NULL | `Chatbot` / `Social` / `Game` / `Study` / `System` / `Custom` |
| `Effect` | TEXT(8) | NOT NULL | `Block` / `Allow` |
| `IsEnabled` | INTEGER | NOT NULL, default 1 | |
| `IsBuiltIn` | INTEGER | NOT NULL, default 0 | Rule mặc định đi kèm app |
| `EnforcementStatus` | TEXT(16) | NOT NULL, default `Enforced` | `Enforced` / `NotEnforced` (URL/path post-MVP) |
| `ExpiresAtUtc` | TEXT(28) | NULL | Hết hạn (allowlist tạm thời) |
| `Note` | TEXT(512) | NULL | Ghi chú phụ huynh |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |
| `UpdatedAtUtc` | TEXT(28) | NOT NULL | |

**Constraints**
- `UNIQUE(RuleType, MatchType, NormalizedValue, Effect)` — cho phép Allow và Block cùng domain; chống trùng cùng hiệu lực.
- `CHECK(Effect IN ('Block','Allow'))`.
- `CHECK(Category IN (...))` nếu muốn chặt; có thể validate ở app.
- MVP chỉ enforce `RuleType IN ('Domain','App')` và `MatchType = 'Exact'`; rule khác lưu trạng thái `NotEnforced`.

**Index**
- `IX_BlockRules_Effect_IsEnabled` trên `(Effect, IsEnabled)`.
- `IX_BlockRules_Category` trên `Category`.
- `IX_BlockRules_ExpiresAtUtc` trên `ExpiresAtUtc`.

**Quy tắc matching**
1. Bỏ rule `IsEnabled = 0`, `ExpiresAtUtc < now` hoặc rule `NotEnforced`.
2. `Allow` thắng `Block`.
3. MVP chỉ so khớp `Exact` sau khi chuẩn hóa domain (lowercase, bỏ scheme/path/trailing dot); không so khớp chuỗi thô.
4. Domain thiết yếu có allowlist built-in ở app, không cần row DB.

**Ví dụ rule mặc định**
| RuleType | MatchType | Value | Category | Effect |
|---|---|---|---|---|
| Domain | Exact | `chatgpt.com` | Chatbot | Block |
| Domain | Exact | `chat.openai.com` | Chatbot | Block |
| Domain | Exact | `claude.ai` | Chatbot | Block |
| Domain | Exact | `gemini.google.com` | Chatbot | Block |
| Domain | Exact | `copilot.microsoft.com` | Chatbot | Block |
| Domain | Exact | `meet.google.com` | Study | Allow |
| Domain | Exact | `teams.microsoft.com` | Study | Allow |

---

## 7. Bảng `ScheduleWindows`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `DaysMask` | INTEGER | NOT NULL, 1–127 | Bitmask: Mon=1, Tue=2, Wed=4, Thu=8, Fri=16, Sat=32, Sun=64 |
| `StartMinutes` | INTEGER | NOT NULL, 0–1439 | Phút trong ngày |
| `EndMinutes` | INTEGER | NOT NULL, 1–1440 | 1440 = 24:00 |
| `Label` | TEXT(100) | NULL | Ví dụ "Giờ học tối" |
| `IsEnabled` | INTEGER | NOT NULL, default 1 | |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |
| `UpdatedAtUtc` | TEXT(28) | NOT NULL | |

**Quy tắc**
- `EndMinutes > StartMinutes` trong MVP; không hỗ trợ khung qua nửa đêm (có thể thêm sau).
- Các khung trong cùng ngày không được chồng lấn; app validate.
- `DaysMask = 0` không hợp lệ.
- Scheduler đánh giá mỗi 30 giây.

**Index**
- `IX_ScheduleWindows_IsEnabled` trên `IsEnabled`.

---

## 8. Bảng `StudySessions`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `StartedAtUtc` | TEXT(28) | NOT NULL | Bắt đầu protection ON |
| `EndedAtUtc` | TEXT(28) | NULL | Kết thúc |
| `LastHeartbeatAtUtc` | TEXT(28) | NOT NULL | Heartbeat gần nhất; dùng khi crash |
| `StartLocalDate` | TEXT(10) | NOT NULL | Ngày local để nhóm báo cáo |
| `Status` | TEXT(16) | NOT NULL | `Active` / `Completed` / `Aborted` / `Interrupted` |
| `Trigger` | TEXT(16) | NOT NULL | `Manual` / `Schedule` / `Recovery` |
| `MonitoringEnabled` | INTEGER | NOT NULL | Snapshot cấu hình lúc bắt đầu |
| `ActiveSeconds` | INTEGER | NOT NULL, default 0 | Thời gian app thực sự chạy; loại trừ sleep/tắt |
| `FocusedSeconds` | INTEGER | NOT NULL, default 0 | |
| `DistractedSeconds` | INTEGER | NOT NULL, default 0 | |
| `AbsentSeconds` | INTEGER | NOT NULL, default 0 | |
| `DrowsySeconds` | INTEGER | NOT NULL, default 0 | |
| `CameraUnavailableSeconds` | INTEGER | NOT NULL, default 0 | |
| `IdleSeconds` | INTEGER | NOT NULL, default 0 | Không có input; lưu riêng |
| `StudySeconds` | INTEGER | NOT NULL, default 0 | Category Study |
| `DistractionSeconds` | INTEGER | NOT NULL, default 0 | Category Distraction |
| `NeutralSeconds` | INTEGER | NOT NULL, default 0 | Category Neutral/Unknown |
| `OnlineClassSeconds` | INTEGER | NOT NULL, default 0 | Overlay; không cộng trùng vào tổng category |
| `MonitoredSeconds` | INTEGER | NOT NULL, stored generated | Tổng Focused + Distracted + Absent + Drowsy; SQLite tự tính |
| `MonitoringEligibleSeconds` | INTEGER | NOT NULL, default 0 | Thời gian monitoring được kỳ vọng |
| `AttentionPercent` | REAL | stored generated, NULL | Focused / Monitored × 100; SQLite tự tính |
| `CoveragePercent` | REAL | stored generated, NULL | Monitored / MonitoringEligible × 100; SQLite tự tính |
| `ConfidenceAvg` | REAL | NULL | 0.0–1.0, trung bình có trọng số |
| `BlockedAttemptCount` | INTEGER | NOT NULL, default 0 | Số quan sát được; best-effort |
| `Note` | TEXT(512) | NULL | |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |
| `UpdatedAtUtc` | TEXT(28) | NOT NULL | |

**Quy tắc**
- Chỉ có tối đa **một** session `Active` tại một thời điểm (unique partial index).
- `ActiveSeconds` là thời gian app chạy thực tế; không dùng `EndedAtUtc - StartedAtUtc` vì có thể gồm sleep/tắt máy.
- Thời gian sleep/lock không vào `ActiveSeconds`; heartbeat chỉ cộng phần trước suspend và bỏ gap vượt biên (issue #28).
- `Status`: `Completed` khi kết thúc bình thường; `Aborted` khi lỗi/tamper nghiêm trọng; `Interrupted` khi crash/shutdown và được phục hồi.
- Khi kết thúc bình thường, `LastHeartbeatAtUtc = EndedAtUtc`; khi crash, giữ heartbeat cuối cùng.
- Khi crash, `EndedAtUtc = LastHeartbeatAtUtc`; mọi `AttentionEvent` đang mở cũng đóng tại mốc này.
- Heartbeat được ghi bằng `UPDATE` có điều kiện trên session `Active`: beat cũ hơn hoặc session đã kết thúc bị bỏ qua, nên không hồi sinh session và không đè lên `BlockedAttemptCount` (increment cũng là targeted update).
- Nếu Protection vẫn ON khi phục hồi, tạo session mới `Trigger = Recovery`, không cộng thời gian gián đoạn.
- `AttentionPercent` chỉ tính khi `MonitoredSeconds > 0`.
- `CoveragePercent` chỉ tính khi `MonitoringEligibleSeconds > 0`; nếu bằng 0 thì NULL.
- `MonitoringEligibleSeconds` chỉ tăng trong cùng `UPDATE` với category/idle/online-class của activity sample
  (`AddActivitySecondsAsync`), theo policy monitoring đọc lúc start; idle vẫn là thời gian kỳ vọng trừ khi
  `MonitorOnlyDuringClasses`. Mẫu số này độc lập với tick camera nên camera outage làm coverage giảm
  (issue #33).
- `MonitoredSeconds`, `AttentionPercent`, `CoveragePercent` là **stored generated columns**: SQLite tự tính từ các cột đếm khi ghi, EF Core chỉ đọc. EF SQLite provider bỏ kiểu khai báo trong DDL generated column (SQLite không bắt buộc), nên `PRAGMA table_xinfo` trả `type = ''`; giá trị vẫn là INTEGER/REAL theo biểu thức.
- `OnlineClassSeconds` là overlay (thời gian được đánh dấu lớp online), không cộng vào `StudySeconds + DistractionSeconds + NeutralSeconds`.
- Nếu `CoveragePercent < 50`, `SessionAttentionSummary.Warning` trả "Dữ liệu chưa đủ tin cậy" để báo cáo hiển thị (issue #33).
- `BlockedAttemptCount` chỉ phản ánh attempt quan sát được; báo cáo phải ghi rõ best-effort.
- Session summary không bị xóa khi xóa sample/event chi tiết (trừ khi phụ huynh xóa toàn bộ).

**Index**
- `IX_StudySessions_StartedAtUtc`.
- `IX_StudySessions_StartLocalDate`.
- `IX_StudySessions_Status`.
- `IX_StudySessions_LastHeartbeatAtUtc`.
- Unique partial: `CREATE UNIQUE INDEX UX_StudySessions_Active ON StudySessions(Status) WHERE Status = 'Active';`

---

## 9. Bảng `ActivitySamples`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `SessionId` | TEXT(36) | FK → `StudySessions.Id`, NOT NULL, ON DELETE CASCADE | |
| `SampledAtUtc` | TEXT(28) | NOT NULL | |
| `DurationSeconds` | INTEGER | NOT NULL, default 0 | Thời lượng sample để tính top app/domain |
| `AppName` | TEXT(128) | NULL | Tên hiển thị |
| `ProcessName` | TEXT(128) | NULL | Ví dụ `chrome`, `ms-teams` |
| `WindowTitle` | TEXT(512) | NULL | Cắt ngắn, không lưu nội dung nhạy cảm |
| `Url` | TEXT(2048) | NULL | Chỉ khi lấy được |
| `Domain` | TEXT(256) | NULL | Domain đã chuẩn hóa |
| `UrlSource` | TEXT(16) | NOT NULL, default `None` | `UIA` / `Extension` / `None` |
| `Category` | TEXT(32) | NOT NULL, default `Unknown` | `Study` / `Distraction` / `Neutral` / `Unknown`; `OnlineClass` là overlay qua `IsOnlineClass` |
| `IsForeground` | INTEGER | NOT NULL, default 1 | |
| `IsOnlineClass` | INTEGER | NOT NULL, default 0 | Meet/Teams/Zoom |
| `IdleSeconds` | INTEGER | NOT NULL, default 0 | |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |

**Index**
- `IX_ActivitySamples_SessionId_SampledAtUtc` trên `(SessionId, SampledAtUtc)`.
- `IX_ActivitySamples_Domain` trên `Domain`.
- `IX_ActivitySamples_Category` trên `Category`.

**Retention**
- Xóa theo `ActivityRetentionDays` (mặc định 30).
- `SessionId` là FK `ON DELETE CASCADE`; không có sample mồ côi trong điều kiện FK bật.

**Quy tắc riêng tư**
- Không lưu query string/fragment; chỉ lưu domain và path đã chuẩn hóa.
- `WindowTitle` được truncate/strip trước khi lưu; coi là dữ liệu nhạy cảm.
- Không lưu nội dung trang hoặc clipboard.

> **Triển khai issue #26:** `ActivityTrackingService` ghi tối đa một sample mỗi 15 giây và một sample idle
> cho mỗi chuỗi idle ≥ 5 phút. `SqliteActivitySampleRepository` chỉ insert sample; counters của session
> (`StudySeconds`, `DistractionSeconds`, `NeutralSeconds`, `IdleSeconds`, `OnlineClassSeconds`) được cộng
> bằng `SqliteStudySessionRepository.AddActivitySecondsAsync` — `UPDATE` targeted chỉ trên session `Active`
> và chạy trước khi insert sample, nên sample không xuất hiện cho session đã kết thúc và full-entity write
> không đè heartbeat. Sample vẫn bị retention xóa theo §14; báo cáo dài hạn đọc session summary.
>
> **Bổ sung issue #27:** `IsOnlineClass` chỉ được đặt khi sample không idle và foreground khớp Meet/Teams/Zoom,
> nên cửa sổ lớp bị bỏ quên khi người dùng rời máy không tạo thời gian lớp online. `OnlineClassSeconds` là
> overlay; báo cáo cộng nó tách khỏi `StudySeconds`/`DistractionSeconds`/`NeutralSeconds`.

---

## 10. Bảng `AttentionEvents`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `SessionId` | TEXT(36) | FK → `StudySessions.Id`, NOT NULL, ON DELETE CASCADE | |
| `State` | TEXT(24) | NOT NULL | `Focused` / `Distracted` / `Absent` / `Drowsy` / `CameraUnavailable` |
| `StartedAtUtc` | TEXT(28) | NOT NULL | |
| `EndedAtUtc` | TEXT(28) | NULL | NULL nếu đang diễn ra |
| `DurationSeconds` | INTEGER | NOT NULL, default 0 | |
| `Confidence` | REAL | NULL | 0.0–1.0 |
| `AppName` | TEXT(128) | NULL | App foreground lúc event |
| `Domain` | TEXT(256) | NULL | |
| `Note` | TEXT(256) | NULL | Ví dụ "camera busy" |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |

**Index**
- `IX_AttentionEvents_SessionId_StartedAtUtc` trên `(SessionId, StartedAtUtc)`.
- `IX_AttentionEvents_State` trên `State`.

**Quy tắc**
- Chỉ tạo event khi state thay đổi, không ghi mỗi frame.
- `CameraUnavailable` có thể kéo dài; cập nhật `EndedAtUtc` khi camera hoạt động lại.
- Khi session/monitoring dừng, event đang mở được đóng tại thời điểm dừng; khi crash, đóng tại `LastHeartbeatAtUtc`.
- `Confidence` của session là trung bình có trọng số theo `DurationSeconds`, không phải trung bình cộng đơn giản.
- Không lưu frame, landmark hay embedding.
- **Triển khai issue #32:** `SqliteAttentionEventRepository` thêm/đóng event; `GetOpenAsync` lấy event mở mới
  nhất của session; `CloseOpenAsync` đóng mọi event mở tại mốc cho trước và clamp về `StartedAtUtc` nếu mốc
  sớm hơn — dùng khi reconcile sau crash với `LastHeartbeatAtUtc`. `AttentionMonitorService` chỉ mở event khi
  state đổi nên không có event trùng lặp; event `CameraUnavailable` có `Confidence = NULL`.
- **Cộng counter attention:** `FocusedSeconds`/`DistractedSeconds`/`AbsentSeconds`/`DrowsySeconds`/
  `CameraUnavailableSeconds` và `ConfidenceAvg` trọng số được cộng trong một câu raw `UPDATE` (SQLite dùng
  giá trị dòng cũ cho mọi biểu thức SET); `MonitoredSeconds`/`AttentionPercent`/`CoveragePercent` vẫn do
  stored generated column tính. `CameraUnavailableSeconds` cộng riêng và không vào confidence.

---

## 11. Bảng `BlockedAttempts`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `SessionId` | TEXT(36) | FK → `StudySessions.Id`, NULL, ON DELETE SET NULL | Có thể NULL |
| `OccurredAtUtc` | TEXT(28) | NOT NULL | |
| `TargetType` | TEXT(16) | NOT NULL | `Domain` / `App`; `Url` post-MVP |
| `TargetValue` | TEXT(512) | NOT NULL | Domain hoặc process name/path |
| `Domain` | TEXT(256) | NULL | |
| `Url` | TEXT(2048) | NULL | Chỉ khi có browser extension |
| `ProcessName` | TEXT(128) | NULL | |
| `WindowTitle` | TEXT(512) | NULL | Truncate/strip |
| `ObservedVia` | TEXT(24) | NOT NULL | `BlockPage` / `ProcessWatcher` / `Extension` |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |

**Index**
- `IX_BlockedAttempts_OccurredAtUtc`.
- `IX_BlockedAttempts_Domain`.
- `IX_BlockedAttempts_SessionId`.

**Quy tắc**
- Chỉ ghi khi sự kiện được quan sát; hosts không tạo log HTTPS.
- Blocked attempt là best-effort, không phải tổng tuyệt đối; báo cáo phải ghi rõ.
- App bị chặn ghi `ObservedVia = ProcessWatcher`; `TargetValue` là process name/path và có thể bị giả mạo/đổi tên.
- Debounce 30 giây cho cùng domain/process để tránh spam.
- Khi xóa session, `SessionId` về NULL nhưng log vẫn giữ đến retention.

> **Triển khai (issue #24):** `IBlockedAttemptRepository`/`SqliteBlockedAttemptRepository` chỉ có
> `AddAsync`; `ProtectionEngine` là nơi duy nhất ghi bảng này — một writer channel đọc observation từ
> block page/watcher, debounce 30 giây theo domain/tên app bằng monotonic clock, gắn `SessionId` đang
> hoạt động rồi tăng `BlockedAttemptCount` của session. Domain ghi `TargetType = Domain` +
> `ObservedVia = BlockPage`; app ghi `TargetType = App` + `ObservedVia = ProcessWatcher` và
> `ProcessName`; không ghi `WindowTitle`/path. Lỗi ghi chỉ log, không làm dừng adapter.

---

## 12. Bảng `AuditLogs`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `OccurredAtUtc` | TEXT(28) | NOT NULL | |
| `Actor` | TEXT(16) | NOT NULL | `Parent` / `System` / `Student` |
| `EventType` | TEXT(64) | NOT NULL | `ParentLogin`, `AuthenticationFailed`, `LockoutTriggered`, `PinSetup`, `PinChanged`, `RecoveryCodeUsed`, `RecoveryCodeRegenerated`, `PrivacyConsentRecorded`, `OnboardingCompleted`, `ProtectionOn`, `ProtectionOff`, `DataDeleted`, `TamperDetected`, `SessionInterrupted`, `WatchdogRestart`, `SystemShutdown`, … |
| `Description` | TEXT(512) | NOT NULL | Mô tả ngắn, không chứa PIN/URL nhạy cảm |
| `MetadataJson` | TEXT(2048) | NULL | Dữ liệu bổ sung đã redact |
| `CorrelationId` | TEXT(36) | NULL | Liên kết log file |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |

**Index**
- `IX_AuditLogs_OccurredAtUtc`.
- `IX_AuditLogs_EventType`.

**Retention**
- Mặc định 365 ngày; phụ huynh có thể xóa.
- Không bao giờ ghi PIN, recovery code, token hay nội dung camera.

---

## 13. Tổng hợp & truy vấn báo cáo

### 13.1. Dashboard hôm nay

```sql
SELECT
    COUNT(*) AS Sessions,
    SUM(ActiveSeconds) AS ActiveSeconds,
    SUM(StudySeconds) AS StudySeconds,
    SUM(DistractionSeconds) AS DistractionSeconds,
    SUM(FocusedSeconds) AS FocusedSeconds,
    SUM(DistractedSeconds) AS DistractedSeconds,
    SUM(AbsentSeconds) AS AbsentSeconds,
    SUM(DrowsySeconds) AS DrowsySeconds,
    SUM(MonitoredSeconds) AS MonitoredSeconds,
    SUM(MonitoringEligibleSeconds) AS MonitoringEligibleSeconds,
    SUM(BlockedAttemptCount) AS BlockedAttempts
FROM StudySessions
WHERE StartLocalDate = @today;
```

> **Triển khai issue #35:** `SqliteStudySessionRepository.GetDailySummaryAsync` thực thi truy vấn aggregate
> này bằng EF Core (`GROUP BY` hằng số) và trả `DailyStudySummary`; không đọc `ActivitySamples` hay
> `AttentionEvents` mỗi lần render dashboard. Ngày local do `DashboardService` lấy từ `TimeProvider`.
> `ActiveSessionStartedAtUtc` lấy từ session `Active` duy nhất; `MonitoredSeconds` vẫn suy ra từ bốn counter
> attention (không đọc stored column) để giữ invariant của `SessionAttentionSummary`.

### 13.2. Timeline theo giờ

```sql
SELECT
    strftime('%H', SampledAtUtc, 'localtime') AS HourLocal,
    Category,
    SUM(DurationSeconds) AS Seconds
FROM ActivitySamples
WHERE SessionId = @sessionId
GROUP BY HourLocal, Category
ORDER BY HourLocal;
```

> `localtime` dùng timezone của máy; với báo cáo dài hạn ưu tiên `StartLocalDate` để tránh DST.
>
> **Triển khai issue #36:** không dùng `strftime(..., 'localtime')` cho báo cáo. Repository trả
> `SampledAtUtc` (UTC) cùng `DurationSeconds - IdleSeconds`, `Category`, `AppName`, `Domain`; `ReportService`
> chuyển từng sample sang giờ local bằng `TimeProvider.LocalTimeZone` rồi gộp bucket `(LocalDate, LocalHour)`,
> nên giờ lặp khi fall-back được cộng vào cùng bucket và giờ khuyết khi spring-forward không xuất hiện.
> Sample fully idle (`DurationSeconds = IdleSeconds`) bị loại khỏi timeline và top.

### 13.3. Top app/domain

```sql
SELECT
    COALESCE(AppName, Domain, 'Unknown') AS Name,
    SUM(DurationSeconds) AS Seconds
FROM ActivitySamples
WHERE SessionId = @sessionId
GROUP BY Name
ORDER BY Seconds DESC
LIMIT 10;
```

> **Triển khai issue #36:** `ListForReportAsync` nhận danh sách session id (một khoảng ngày, cap 31 ngày) và
> `maxCount`; projection chỉ gồm `SessionId`, `SampledAtUtc`, `ActiveSeconds`, `Category`, `AppName`, `Domain`
> — không đọc `WindowTitle`, URL đầy đủ hay `ProcessName`. Top ưu tiên `Domain` đã chuẩn hóa, sau đó `AppName`,
> cuối cùng là "Không rõ"; tổng `ActiveSeconds` thay vì `DurationSeconds` để idle không được tính là hoạt động.

### 13.4. Attention events

```sql
SELECT State, StartedAtUtc, EndedAtUtc, DurationSeconds, Confidence
FROM AttentionEvents
WHERE SessionId = @sessionId
ORDER BY StartedAtUtc;
```

> **Triển khai issue #36:** `ListForReportAsync` lọc theo danh sách session id, sắp xếp `StartedAtUtc` giảm dần
> và cap 100 event (đọc 101 để biết truncation). Session có `MonitoredSeconds`/`CameraUnavailableSeconds` > 0
> nhưng không còn event nào bị coi là detail đã hết retention → `StudyReport.AttentionDetailsExpired` tạo
> cảnh báo, không phải lỗi; cờ này bị bỏ qua khi danh sách event đã bị truncation để tránh báo nhầm.
>
> **Triển khai issue #37:** xuất CSV không tạo bảng/cột mới. `ReportExportService` dùng lại
> `ListForReportAsync` của `ActivitySamples` (projection chỉ có app name/domain đã làm sạch) và danh sách
> `AttentionEvents` trong `StudyReport`; session và tổng ngày lấy từ snapshot báo cáo nên không cần query lại.
> Không truy vấn nào đọc `WindowTitle`, URL đầy đủ hay `ProcessName`; file CSV nằm ngoài DB tại
> `%LocalAppData%\EduFocus\exports`.

---

## 14. Retention & dọn dẹp

| Dữ liệu | Mặc định | Cấu hình |
|---|---:|---|
| `ActivitySamples` | 30 ngày | `ActivityRetentionDays` |
| `AttentionEvents` | 90 ngày | `EventRetentionDays` |
| `BlockedAttempts` | 90 ngày | `EventRetentionDays` |
| `StudySessions` | 365 ngày | `SessionRetentionDays` |
| `AuditLogs` | 365 ngày | Cố định / tùy chọn |
| Log file | 7–14 ngày | `FileLogger` |

**Retention job**
- Chạy một lần khi app khởi động và mỗi ngày khi local time ≥ 03:00 (`RetentionSchedulerService`).
- Xóa theo batch 500 mặc định (`RetentionOptions.BatchSize`), lặp đến khi batch ngắn, để tránh khóa DB lâu.
- Thứ tự xóa: `ActivitySamples` → `AttentionEvents` → `BlockedAttempts` → `AuditLogs` → `StudySessions`; chi tiết xóa trước để session summary giữ đủ hạn.
- Không xóa row của session `Active`, không xóa `AttentionEvent` còn mở; `StudySessions` chỉ xóa khi `Status <> 'Active'` và `EndedAtUtc < cutoff`. Xóa session cascade sample/event còn sót và đặt `BlockedAttempts.SessionId = NULL`.
- Khi tạo DB, đặt `PRAGMA auto_vacuum = INCREMENTAL;`; sau mỗi pass retention chạy `PRAGMA incremental_vacuum;`.
- Bật `PRAGMA foreign_keys = ON;` và `PRAGMA journal_mode = WAL;` khi mở kết nối.
- Ghi audit `RetentionApplied` (tên cũ trong tài liệu: "RetentionCompleted") với số bản ghi từng bảng và danh sách bảng lỗi; lỗi một bảng không dừng các bảng khác và không báo thành công giả.
- `AuditLogs` dùng cửa sổ cố định 365 ngày (`RetentionOptions.AuditRetentionDays`) vì `AppSettings` không có knob riêng.
- Retention chỉ xóa bản ghi chi tiết; `StudySessions` (summary) được giữ đến `SessionRetentionDays`, nên xóa `ActivitySamples`/`AttentionEvents` không làm mất báo cáo dài hạn.

**Xóa toàn bộ theo yêu cầu (issue #38)**
- `SqlitePrivacyDataRepository` đọc `PrivacyDataCounts` + dung lượng file DB (`page_count * page_size`) và xóa theo batch 500 (`DataDeletionOptions.BatchSize`).
- Batch dùng `DELETE ... WHERE Id IN (SELECT Id FROM ... LIMIT @batch)` vì build SQLite này không bật `DELETE ... LIMIT`; lặp đến khi trả 0.
- Thứ tự: `ActivitySamples` → `AttentionEvents` → `BlockedAttempts` → `StudySessions` (chỉ `Status <> 'Active'`); sample/event của session `Active` không bao giờ bị chọn. `BlockedAttempts.SessionId` đặt `NULL` khi session bị xóa.
- Không xóa `AppSettings`, `ParentCredentials`, `AuditLogs` hay dữ liệu của phiên đang chạy; không thể hoàn tác.
- Sau khi xóa chạy `VACUUM` best-effort: lỗi vacuum chỉ ghi warning, không biến thao tác thành thất bại.
- Ghi audit `DataDeleted` với số bản ghi từng bảng; khi một bảng lỗi, các bảng khác vẫn tiếp tục, audit ghi `Incomplete` kèm danh sách bảng lỗi và UI báo lỗi (không báo thành công giả). Hủy giữa chừng không ghi audit thành công.

---

## 15. Dung lượng dự kiến

Giả định:
- 3 giờ học/ngày.
- Activity sample mỗi 15 giây → 720 sample/ngày.
- Attention event thay đổi vài chục lần/ngày.
- 30 ngày sample → ~21.600 rows.
- 365 ngày session → ~365 rows.

**Ước tính:** DB < 50 MB sau 1 năm với cấu hình mặc định. Nếu giảm sampling hoặc retention, dung lượng còn nhỏ hơn.

Từ issue #47, DB được mã hóa SQLCipher. Đo lại trên dữ liệu tổng hợp 30 ngày: 8,25 MB (plaintext trước đây 8,14 MB);
DB mô phỏng một năm sau retention: 9,71 MB. Overhead của SQLCipher không làm thay đổi ngưỡng 50 MB.

---

## 16. Bảo mật dữ liệu

- Thư mục `%ProgramData%\EduFocus`:
  - ACL: `Administrators` và `SYSTEM` full control.
  - Học sinh standard user: không đọc/ghi.
  - `DataDirectorySecurity` áp ACL khi khởi tạo DB; quyền kế thừa cho file DB, `-wal`/`-shm` và thư mục `backups`.
  - Nếu không áp được ACL, initializer log lỗi và ném `DataDirectorySecurityException`; không xóa dữ liệu.
- Nếu phụ huynh và học sinh dùng chung tài khoản Windows, ACL không tách được; đây là giới hạn đã biết. PIN chỉ bảo vệ UI, không bảo vệ file DB. Tuy nhiên DB/WAL/backup được mã hóa SQLCipher nên standard user không đọc được nếu chỉ copy file; tài khoản Administrator/SYSTEM trên máy vẫn giải mã được (xem §18).
- Không lưu secret trong DB ngoài hash PIN/recovery code. Khóa mã hóa không nằm trong DB, log, audit, source hay registry.
- DB, WAL/SHM và backup được mã hóa SQLCipher; khóa 32 byte lưu DPAPI tại `%ProgramData%\EduFocus\edufocus.key` (xem §18).
- Backup DB trước migration; giữ tối đa 3 bản; backup của DB mã hóa cũng được mã hóa.
- Log không chứa dữ liệu nhạy cảm và không chứa khóa mã hóa.

**Kiểm tra ACL (thủ công, sau lần chạy đầu tiên bằng Administrator)**
- `icacls "%ProgramData%\EduFocus"` chỉ hiển thị `BUILTIN\Administrators:(OI)(CI)(F)` và `NT AUTHORITY\SYSTEM:(OI)(CI)(F)`; không còn `Users`/`Authenticated Users`.
- Đăng nhập tài khoản standard user: `dir "%ProgramData%\EduFocus"` bị từ chối truy cập; chạy EduFocus dưới tài khoản này nhận thông báo an toàn thay vì mở DB.

---

## 17. Migration & phiên bản schema

- Dùng EF Core migrations.
- Migration đầu tiên: `InitialEduFocusSchema` — đã tạo trong `src/EduFocus.SqliteDataAccess/Migrations` (issue #9), gồm 9 bảng, index, check constraint, FK và generated column.
- Migration `AddOnboardingConsent` (issue #15) thêm hai cột nullable `PrivacyAcceptedAtUtc` và `GuardianAcknowledgedAtUtc` vào `AppSettings`; DB cũ được backup tự động trước khi nâng cấp.
- Migration `AddProtectionSource` (issue #17) thêm cột `ProtectionSource` vào `AppSettings` và backfill `Manual` cho các hàng đang có `ProtectionEnabled = 1`.
- Issue #20 không thêm migration: seed version của rule mặc định là hằng số trong `BuiltInRuleCatalog` và được ghi vào audit metadata; rule seed vào bảng `BlockRules` theo unique key sẵn có.
- Mỗi migration có backup tự động trước khi chạy — `SqliteBackupStore` dùng SQLite backup API cho DB plaintext cũ và `sqlcipher_export` cho DB mã hóa (SQLite backup API không hỗ trợ DB mã hóa), kiểm tra `PRAGMA integrity_check` và chỉ tiếp tục khi bản backup hợp lệ; giữ tối đa 3 bản gần nhất.
- Tên backup `edufocus-{UTC}-{nn}.db` là duy nhất, không ghi đè bản cũ; retention chỉ xóa bản cũ nhất.
- Nếu migration thất bại:
  - Giữ DB cũ.
  - Ghi log.
  - Hiển thị lỗi cho phụ huynh; không tự xóa dữ liệu.
- Nếu DB hiện có hỏng integrity check: không sửa file, ném `SqliteDatabaseCorruptException` kèm đường dẫn thư mục backup.
- Khôi phục: đóng EduFocus, gọi `SqliteDatabaseInitializer.RestoreBackup(path)` (API) — backup được kiểm tra integrity, DB hiện tại được snapshot best-effort, file `-wal`/`-shm` được xóa trước khi thay thế. Backup mã hóa chỉ mở được bằng đúng khóa hiện tại; backup plaintext legacy được mã hóa trong lúc restore. UI khôi phục sẽ ở phase Reports/Settings.
- Không hỗ trợ downgrade schema; nếu cần, khôi phục từ backup.
- DB từ `DontBeLazy` 1.x không được mở bởi EduFocus.
- Convention khi mở kết nối: `PRAGMA key` (SQLCipher) luôn được áp **trước** mọi statement, sau đó `PRAGMA foreign_keys = ON` (interceptor); DB mới được set `auto_vacuum = INCREMENTAL` trước khi tạo bảng và `journal_mode = WAL`; DB cũ được bật WAL sau khi backup, trước migration.
- Test tích hợp: `src/EduFocus.SqliteDataAccess.Tests` (schema, khởi tạo/backup/retention, roundtrip entity, heartbeat có điều kiện/chống hồi sinh, retention giữ session summary, ACL, corrupt DB và restore, mã hóa và khôi phục khóa — xem §18).

---

## 18. Mã hóa cơ sở dữ liệu (SQLCipher)

Issue #47 mã hóa toàn bộ DB tại chỗ bằng **SQLCipher 4.5.2** (gói `SQLitePCLRaw.bundle_e_sqlcipher`
2.1.11, dùng `Microsoft.EntityFrameworkCore.Sqlite.Core`). Mục tiêu: file DB, WAL/SHM và backup
không đọc được nếu bị copy ra ngoài, đồng thời không lưu khóa dưới dạng plaintext cạnh DB.

**Khóa và file khóa**
- Khóa là 32 byte sinh bằng CSPRNG, không suy ra từ PIN hay mật khẩu người dùng.
- Lưu tại `%ProgramData%\EduFocus\edufocus.key`, định dạng `EDUFKEY1` + blob DPAPI
  `DataProtectionScope.LocalMachine` với entropy `EduFocus.DatabaseKey.v1`; file kế thừa ACL
  Administrators/SYSTEM. Không có byte khóa plaintext trong file, log, audit, registry hay source.
- Ghi file khóa bằng file tạm + `File.Move(overwrite: false)` để hai tiến trình không ghi đè khóa
  của nhau; file khóa hỏng hoặc không giải mã được **không bao giờ bị ghi đè tự động**.
- Khóa gắn với máy: DPAPI `LocalMachine` chỉ giải mã được trên đúng máy đã tạo khóa. Sao lưu DB
  phải đi kèm `edufocus.key` và chỉ khôi phục trên máy đó; copy DB + khóa sang máy khác vẫn không
  mở được.

**Áp khóa cho kết nối**
- `SqlitePragmaInterceptor` gọi `sqlite3_key` trên mỗi kết nối EF trước `PRAGMA foreign_keys = ON`;
  initializer, backup store và các kết nối trực tiếp áp khóa tương tự.
- Khóa được truyền dưới dạng raw key `x'<64 hex>'`. Vì khóa đã 32 byte ngẫu nhiên, dạng này bỏ qua
  PBKDF2 của SQLCipher: mở DB mất vài ms thay vì ~500 ms mỗi kết nối.
- Mỗi native `sqlite3` handle chỉ được key **một lần** bằng `ConditionalWeakTable`. Khi connection
  pooling trả handle về pool và dùng lại, key lại sẽ reset codec và làm mất trạng thái kết nối như
  `auto_vacuum`; vì vậy lần mở sau chỉ áp pragma, không gọi `sqlite3_key` lần nữa.

**Phát hiện trạng thái và fail-closed**
- 16 byte đầu `SQLite format 3\0` = DB plaintext; header khác = DB mã hóa (SQLCipher dùng header
  ngẫu nhiên). Không có/sai khóa trả SQLite error 26.
- DB mã hóa nhưng thiếu file khóa → `DatabaseKeyException` kèm đường dẫn khóa, không tự tạo khóa mới.
- File khóa hỏng/không giải mã được → `DatabaseKeyException`, giữ nguyên file khóa.
- Sai khóa hoặc integrity check thất bại → `SqliteDatabaseCorruptException`, giữ nguyên DB, chỉ
  khôi phục từ backup. Không có chế độ tắt mã hóa hay fallback sang DB plaintext.

**Nâng cấp plaintext → encrypted**
- Khi phát hiện DB plaintext (nâng cấp từ bản trước #47), initializer backup plaintext trước, dùng
  `ATTACH` + `sqlcipher_export` tạo bản mã hóa, chạy `PRAGMA integrity_check` trên bản mới rồi mới
  thay thế file gốc; lỗi giữ nguyên DB plaintext và backup.
- Backup plaintext legacy trong `backups/` được mã hóa lại ở lần khởi động sau khi nâng cấp; lỗi mã
  hóa một backup chỉ ghi warning, không chặn app và không xóa file.

**Backup và restore**
- SQLite backup API không hỗ trợ DB mã hóa; backup được tạo bằng `sqlcipher_export` vào database
  đích đã key bằng `sqlite3_key_v2` (alias `cipher_export`), sau đó verify `integrity_check`.
- Restore chỉ chấp nhận backup hợp lệ; backup mã hóa bằng khóa khác bị từ chối. Backup plaintext
  legacy được mã hóa trong lúc restore nên DB đang chạy không bao giờ trở lại plaintext.
- Restore tạo snapshot DB hiện tại (best-effort) và xóa `-wal`/`-shm` trước khi thay file.

**Quy trình mất khóa và hỗ trợ người dùng**
1. Giữ `edufocus.key` cùng bộ backup ở nơi an toàn (ví dụ ổ ngoài được mã hóa); không đổi tên file.
2. Khi app báo thiếu/hỏng khóa: dừng app, khôi phục đúng file khóa đã sao lưu trên **cùng máy**, rồi
   mở lại. Có thể dùng backup DB đi kèm nếu DB hiện tại hỏng.
3. Nếu không còn file khóa (mất ổ, đổi máy, cài lại Windows): dữ liệu 2.0 **không thể khôi phục** —
   không có backdoor, khóa không nằm trong cloud và nhà phát triển không thể giải mã. Người dùng có
   thể gỡ cài đặt, xóa dữ liệu và bắt đầu lại onboarding.
4. Khi báo sự cố, không gửi `edufocus.key` hoặc DB qua kênh công khai; chỉ mô tả lỗi và làm theo
   hướng dẫn trong app/SECURITY.

**Giới hạn**
- Administrator/SYSTEM đọc được file khóa và giải mã DB; mã hóa chống đọc file bởi standard user và
  chống lộ khi file bị copy, không chống admin cục bộ.
- CSV xuất và log kỹ thuật không được mã hóa; BitLocker vẫn được khuyến nghị cho ổ đĩa.
- Khóa gắn máy nên đổi phần cứng/Windows có thể làm mất khả năng đọc dữ liệu cũ.

**Kiểm thử:** `EncryptionTests` (12 test) — header DB không phải plaintext, sai/thiếu/hỏng khóa,
file khóa DPAPI không chứa khóa plaintext, nâng cấp plaintext giữ dữ liệu, backup mã hóa/restore,
backup plaintext legacy, backup khóa khác bị từ chối, log không chứa khóa, DB/WAL/SHM không chứa
marker plaintext. `PerformanceBudgetTests` đo lại toàn bộ ngưỡng trên DB mã hóa.

---

## 19. Tài liệu liên quan

- [BA Document](./ba_document.md)
- [Use Cases](./use_cases.md)
- [System Architecture](./system_architecture.md)
- [Security Policy](../SECURITY.md)

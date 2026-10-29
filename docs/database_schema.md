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
| `Theme` | TEXT(16) | NOT NULL, default `'dark'` | `dark` / `light` / `system` |
| `Language` | TEXT(8) | NOT NULL, default `'vi'` | `vi` / `en` (dự kiến) |
| `OnboardingCompleted` | INTEGER | NOT NULL, default 0 | Đã setup PIN |
| `ScheduleOverrideUntilUtc` | TEXT(28) | NULL | Hết hiệu lực tạm tắt schedule |
| `LastCleanShutdownAtUtc` | TEXT(28) | NULL | Thời điểm phụ huynh thoát app hợp lệ; không set khi Windows shutdown |
| `CreatedAtUtc` | TEXT(28) | NOT NULL | |
| `UpdatedAtUtc` | TEXT(28) | NOT NULL | |

**Ghi chú**
- `ProtectionEnabled` là nguồn sự thật cho trạng thái ON/OFF.
- Khi app crash, lần khởi động sau đọc cờ này để phục hồi.
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
- `Status`: `Completed` khi kết thúc bình thường; `Aborted` khi lỗi/tamper nghiêm trọng; `Interrupted` khi crash/shutdown và được phục hồi.
- Khi kết thúc bình thường, `LastHeartbeatAtUtc = EndedAtUtc`; khi crash, giữ heartbeat cuối cùng.
- Khi crash, `EndedAtUtc = LastHeartbeatAtUtc`; mọi `AttentionEvent` đang mở cũng đóng tại mốc này.
- Nếu Protection vẫn ON khi phục hồi, tạo session mới `Trigger = Recovery`, không cộng thời gian gián đoạn.
- `AttentionPercent` chỉ tính khi `MonitoredSeconds > 0`.
- `CoveragePercent` chỉ tính khi `MonitoringEligibleSeconds > 0`; nếu bằng 0 thì NULL.
- `MonitoredSeconds`, `AttentionPercent`, `CoveragePercent` là **stored generated columns**: SQLite tự tính từ các cột đếm khi ghi, EF Core chỉ đọc. EF SQLite provider bỏ kiểu khai báo trong DDL generated column (SQLite không bắt buộc), nên `PRAGMA table_xinfo` trả `type = ''`; giá trị vẫn là INTEGER/REAL theo biểu thức.
- `OnlineClassSeconds` là overlay (thời gian được đánh dấu lớp online), không cộng vào `StudySeconds + DistractionSeconds + NeutralSeconds`.
- Nếu `CoveragePercent < 50`, báo cáo hiển thị cảnh báo.
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

---

## 12. Bảng `AuditLogs`

| Cột | Kiểu | Ràng buộc | Ý nghĩa |
|---|---|---|---|
| `Id` | TEXT(36) | PK | GUID |
| `OccurredAtUtc` | TEXT(28) | NOT NULL | |
| `Actor` | TEXT(16) | NOT NULL | `Parent` / `System` / `Student` |
| `EventType` | TEXT(64) | NOT NULL | `ParentLogin`, `ProtectionOn`, `ProtectionOff`, `PinChanged`, `DataDeleted`, `TamperDetected`, `SessionInterrupted`, `WatchdogRestart`, `SystemShutdown`, … |
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

### 13.4. Attention events

```sql
SELECT State, StartedAtUtc, EndedAtUtc, DurationSeconds, Confidence
FROM AttentionEvents
WHERE SessionId = @sessionId
ORDER BY StartedAtUtc;
```

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
- Chạy mỗi ngày lúc 03:00 local hoặc khi app khởi động.
- Xóa theo batch (500–1000 rows) để tránh khóa DB lâu.
- Khi tạo DB, đặt `PRAGMA auto_vacuum = INCREMENTAL;`; sau khi xóa, chạy `PRAGMA incremental_vacuum;` định kỳ.
- Bật `PRAGMA foreign_keys = ON;` và `PRAGMA journal_mode = WAL;` khi mở kết nối.
- Ghi audit "RetentionCompleted" với số bản ghi đã xóa.

---

## 15. Dung lượng dự kiến

Giả định:
- 3 giờ học/ngày.
- Activity sample mỗi 15 giây → 720 sample/ngày.
- Attention event thay đổi vài chục lần/ngày.
- 30 ngày sample → ~21.600 rows.
- 365 ngày session → ~365 rows.

**Ước tính:** DB < 50 MB sau 1 năm với cấu hình mặc định. Nếu giảm sampling hoặc retention, dung lượng còn nhỏ hơn.

---

## 16. Bảo mật dữ liệu

- Thư mục `%ProgramData%\EduFocus`:
  - ACL: `Administrators` và `SYSTEM` full control.
  - Học sinh standard user: không đọc/ghi.
  - `DataDirectorySecurity` áp ACL khi khởi tạo DB; quyền kế thừa cho file DB, `-wal`/`-shm` và thư mục `backups`.
  - Nếu không áp được ACL, initializer log lỗi và ném `DataDirectorySecurityException`; không xóa dữ liệu.
- Nếu phụ huynh và học sinh dùng chung tài khoản Windows, ACL không tách được; đây là giới hạn đã biết. PIN chỉ bảo vệ UI, không bảo vệ file DB.
- Không lưu secret trong DB ngoài hash PIN/recovery code.
- Cân nhắc SQLCipher ở post-MVP nếu cần mã hóa toàn bộ DB.
- Backup DB trước migration; giữ tối đa 3 bản.
- Log không chứa dữ liệu nhạy cảm.

**Kiểm tra ACL (thủ công, sau lần chạy đầu tiên bằng Administrator)**
- `icacls "%ProgramData%\EduFocus"` chỉ hiển thị `BUILTIN\Administrators:(OI)(CI)(F)` và `NT AUTHORITY\SYSTEM:(OI)(CI)(F)`; không còn `Users`/`Authenticated Users`.
- Đăng nhập tài khoản standard user: `dir "%ProgramData%\EduFocus"` bị từ chối truy cập; chạy EduFocus dưới tài khoản này nhận thông báo an toàn thay vì mở DB.

---

## 17. Migration & phiên bản schema

- Dùng EF Core migrations.
- Migration đầu tiên: `InitialEduFocusSchema` — đã tạo trong `src/EduFocus.SqliteDataAccess/Migrations` (issue #9), gồm 9 bảng, index, check constraint, FK và generated column.
- Mỗi migration có backup tự động trước khi chạy — `SqliteBackupStore` backup bằng SQLite backup API, kiểm tra `PRAGMA integrity_check` và chỉ tiếp tục khi bản backup hợp lệ; giữ tối đa 3 bản gần nhất.
- Tên backup `edufocus-{UTC}-{nn}.db` là duy nhất, không ghi đè bản cũ; retention chỉ xóa bản cũ nhất.
- Nếu migration thất bại:
  - Giữ DB cũ.
  - Ghi log.
  - Hiển thị lỗi cho phụ huynh; không tự xóa dữ liệu.
- Nếu DB hiện có hỏng integrity check: không sửa file, ném `SqliteDatabaseCorruptException` kèm đường dẫn thư mục backup.
- Khôi phục: đóng EduFocus, gọi `SqliteDatabaseInitializer.RestoreBackup(path)` (API) — backup được kiểm tra integrity, DB hiện tại được snapshot best-effort, file `-wal`/`-shm` được xóa trước khi thay thế. UI khôi phục sẽ ở phase Reports/Settings.
- Không hỗ trợ downgrade schema; nếu cần, khôi phục từ backup.
- DB từ `DontBeLazy` 1.x không được mở bởi EduFocus.
- Convention khi mở kết nối: `PRAGMA foreign_keys = ON` (interceptor); DB mới được set `auto_vacuum = INCREMENTAL` trước khi tạo bảng và `journal_mode = WAL`; DB cũ được bật WAL sau khi backup, trước migration.
- Test tích hợp: `src/EduFocus.SqliteDataAccess.Tests` (schema, khởi tạo/backup/retention, roundtrip entity, ACL, corrupt DB và restore).

---

## 18. Tài liệu liên quan

- [BA Document](./ba_document.md)
- [Use Cases](./use_cases.md)
- [System Architecture](./system_architecture.md)
- [Security Policy](../SECURITY.md)

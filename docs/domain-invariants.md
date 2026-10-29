# Domain Invariants - EduFocus 2.0

> **Trạng thái:** Đã triển khai trong `EduFocus.Domain` (issue #8).
> **Nguồn:** [database_schema.md](database_schema.md) §4–§12, [system_architecture.md](system_architecture.md) §3.1, [ba_document.md](ba_document.md) §7.
> **Test:** `src/EduFocus.Domain.Tests/Domain/*`.

---

## 1. Nguyên tắc nền

- `EduFocus.Domain` không tham chiếu EF Core, WPF hoặc Windows API; không I/O, clock, camera hay hosts file.
- Entity chỉ được tạo qua factory; setter private; aggregate tự bảo vệ invariant.
- Mọi timestamp là `DateTime` UTC (`Kind = Utc`). Ngày báo cáo dùng `DateOnly` local.
- StronglyTypedId cho từng entity: `StudySessionId`, `BlockRuleId`, `ParentCredentialId`, …
- Không có plaintext PIN/recovery code; không lưu ảnh, video, âm thanh, landmark hay nội dung trang.
- Factory kiểm tra enum hợp lệ, độ dài và thứ tự thời gian; ném `ArgumentException` hoặc `ArgumentOutOfRangeException`.

---

## 2. Invariant theo entity

### 2.1. `AppSettings`

| Invariant | Thực thi |
|---|---|
| Singleton; `CreateDefault` dùng đúng default của schema | `CreateDefault(nowUtc)` |
| Bật/tắt thủ công xóa `ScheduleOverrideUntilUtc` (BR-07) | `EnableProtection`, `DisableProtection` |
| `ProtectionSource` khác `None` khi ON và bằng `None` khi OFF; bật thủ công ghi `Manual` | `EnableProtection`, `DisableProtection`, `DisableProtectionWithScheduleOverride` |
| Tắt schedule cũng xóa override | `SetScheduleEnabled(false, …)` |
| Override phải kết thúc trong tương lai | `SetScheduleOverrideUntil`, `DisableProtectionWithScheduleOverride` |
| `UpdatedAtUtc` UTC và không lùi | `Touch` nội bộ |
| Retention/threshold dương | `UpdateRetention`, `UpdateThresholds` |
| Chỉ PIN exit mới đặt `LastCleanShutdownAtUtc` | `RecordCleanShutdown` — use case quyết định, Windows shutdown không gọi |

### 2.2. `ParentCredential` và `ParentPinPolicy`

| Invariant | Thực thi |
|---|---|
| Chỉ lưu Base64 PBKDF2 hash 32 byte và salt 16 byte | `Create`, `ValidateHash`, `ValidateSalt` |
| Iterations ≥ 600.000 | `ValidateIterations` |
| PIN plaintext 6–8 chữ số, không lặp, không tăng/giảm liên tiếp | `ParentPinPolicy` |
| PIN và recovery code dùng chung bộ đếm sai (BR-03) | `FailedAttempts` |
| Sai 5 lần → khóa 15 phút | `RegisterFailedAttempt` |
| Trong thời gian khóa, mọi lần thử ném lỗi; hết khóa tự reset bộ đếm | `IsLockedOut`, `RegisterFailedAttempt` |
| Đăng nhập thành công / đổi PIN reset bộ đếm | `RegisterSuccessfulAuthentication`, `ChangePin` |
| Không có property plaintext | Không tồn tại `Pin`/`RecoveryCode` |

### 2.3. `BlockRule` và `BlockRuleEvaluator`

| Invariant | Thực thi |
|---|---|
| Domain chuẩn hóa: bỏ scheme/path/port/userinfo, lowercase, IDN → punycode, bắt buộc có dấu chấm | `DomainName.Parse` |
| App chuẩn hóa: lấy tên file, bỏ `.exe`, lowercase | `NormalizeProcessName` |
| URL chỉ nhận http/https, bỏ query/fragment/credentials | `UrlSanitizer.Sanitize` |
| Không cho block domain thiết yếu (BR-24) | `ProtectedDomains.IsProtected` |
| MVP chỉ enforce `Domain`/`App` + `Exact`; URL/Suffix/Contains lưu `NotEnforced` | `ResolveEnforcement` |
| Rule hiệu lực = enabled + enforced + chưa hết hạn | `IsEffective` |
| Suffix khớp theo ranh giới domain; Contains khớp theo segment | `Matches` |
| Allow thắng Block (BR-08) | `BlockRuleEvaluator` |
| Snapshot matching không truy vấn DB | `BlockRuleSet.Create` bỏ rule disabled/`NotEnforced`; hết hạn kiểm tra mỗi lần đánh giá |
| Domain thiết yếu luôn `Allow` | `BlockRuleSet.EvaluateDomain` → `ProtectedDomains` |
| Catalog mặc định hợp lệ và không trùng | `BuiltInRuleCatalog` từ chối version ≤ 0 và definition trùng `(RuleType, Value, Effect)` |
| Rule mặc định không chứa domain bảo vệ | `BuiltInRuleCatalogTests` kiểm tra từng rule qua `ProtectedDomains` |

`google.com` và `microsoft.com` chỉ được bảo vệ ở đúng host apex; các dịch vụ cụ thể như `gemini.google.com`, `copilot.microsoft.com` vẫn chặn được. Các domain dịch vụ (`meet.google.com`, `zoom.us`, …) được bảo vệ cả subdomain.

### 2.4. `ScheduleWindow` và `ScheduleValidator`

| Invariant | Thực thi |
|---|---|
| `DaysMask` khác 0 và chỉ chứa bit hợp lệ | `Create`, `Reschedule` |
| `0 ≤ StartMinutes ≤ 1439`, `1 ≤ EndMinutes ≤ 1440`, `End > Start` | `Validate` |
| Không hỗ trợ khung qua nửa đêm | `Validate` |
| End là exclusive | `Covers` |
| Chọn khung kết thúc sớm nhất khi nhiều khung cùng phủ một thời điểm | `ScheduleClock.FindActive` |
| Local→UTC xử lý DST: giờ không tồn tại tiến tới phút hợp lệ, giờ ambiguous dùng offset chuẩn | `ScheduleClock.LocalToUtc` |
| Khung enabled không chồng lấn; bỏ qua khung disabled | `ScheduleValidator` |
| Label ≤ 100 ký tự, được trim/sanitize | `TextSanitizer.Clean` |
| Đánh giá theo wall clock local | Use case/scheduler (domain chỉ cung cấp `Covers`) |

### 2.5. `StudySession`

| Invariant | Thực thi |
|---|---|
| Chỉ mutate khi `Active` | `EnsureActive` |
| Heartbeat UTC, không lùi (BR-11, BR-17) | `Heartbeat`, `EnsureNotBeforeHeartbeat` |
| Mọi counter không âm | `Guard.NonNegative` |
| `MonitoredSeconds = Focused + Distracted + Absent + Drowsy` | Computed property |
| `CameraUnavailableSeconds` tách riêng, không tính monitored (BR-15) | `AddAttentionSeconds` |
| `AttentionPercent`/`CoveragePercent` NULL khi mẫu số 0 | Computed property |
| `MonitoringEligibleSeconds` chỉ tăng khi monitoring bật; nếu `MonitorOnlyDuringClasses` thì chỉ khi đang trong lớp online | `MonitoringEligibility.IsEligible` (issue #27); activity sample cộng (#33) |
| Summary attention nhất quán với counter và cảnh báo khi coverage < 50% | `SessionAttentionSummary` (issue #33) |
| `Unknown` và `Neutral` cộng vào `NeutralSeconds` | `AddCategorySeconds` |
| `OnlineClassSeconds` là overlay, không cộng trùng category | `AddOnlineClassSeconds` |
| Thời gian sleep/lock không cộng vào `ActiveSeconds`; resume chỉ cộng phần trước suspend | Use case (issue #28), không thuộc domain |
| Gap vượt `MaxActiveDelta` khi thiếu sự kiện power bị bỏ, không cộng dồn | Use case (issue #28), không thuộc domain |
| Giây chờ khi ghi heartbeat lỗi được cộng ở beat sau và vẫn clamp `MaxActiveDelta` | Use case (issue #28), không thuộc domain |
| Complete/Abort yêu cầu end ≥ heartbeat cuối | `End` |
| Interrupt kết thúc tại heartbeat cuối, không cộng downtime | `Interrupt` |
| Confidence ∈ [0, 1]; note ≤ 512 | `SetAttentionConfidence`, `SetNote` |
| Tối đa một session Active | DB unique partial index (issue #9), không phải entity |
| Heartbeat chỉ ghi trên session Active và không lùi thời gian | Repository conditional update (issue #25) |

### 2.6. `ActivitySample`

| Invariant | Thực thi |
|---|---|
| `DurationSeconds ≥ 0` và `0 ≤ IdleSeconds ≤ DurationSeconds` | `Capture` |
| `CreatedAtUtc ≥ SampledAtUtc` | `Capture` |
| `UrlSource = None` khi không có URL và ngược lại | `SanitizeUrl` |
| URL http/https, đã bỏ query/fragment/credentials | `UrlSanitizer` |
| `Domain` suy ra từ URL đã sanitize | `DeriveDomain` |
| Title ≤ 512, app/process ≤ 128, control character bị thay bằng space | `TextSanitizer` |
| Gap > `MaxSampleGap` (sleep/treo) không vào `DurationSeconds`; window đếm khởi động lại | Use case (issue #28) |
| Timestamp sample monotonic khi wall clock lùi; forward jump không làm phồng duration | Use case (issues #26/#28) |

**Phân loại activity (`ActivityClassifier`, dùng chung `BlockRuleSet` snapshot):**

| Invariant | Thực thi |
|---|---|
| Online class luôn là `Study` và thắng mọi rule block | `Classify` trả sớm trước khi đánh giá rule |
| Block trên domain **hoặc** process → `Distraction` | `Classify` |
| Không block và có allow trên domain **hoặc** process → `Study` | `Classify` |
| Không có cả domain lẫn process → `Unknown`; còn lại → `Neutral` | `Classify` |
| Idle không cộng giây vào category nào | Use case (issue #26), không thuộc domain |
| Sample idle không đánh dấu `IsOnlineClass` dù cửa sổ lớp còn foreground | Use case (issue #27), không thuộc domain |

### 2.7. `AttentionEvent`

| Invariant | Thực thi |
|---|---|
| State thuộc `AttentionState`; confidence ∈ [0, 1] | `Open` |
| Chỉ đóng một lần; end ≥ start | `Close` |
| Duration làm tròn giây | `Close` |
| Note ≤ 256, app name ≤ 128 | `TextSanitizer` |
| Không lưu frame/landmark (BR-13) | Domain không có field tương ứng |
| Event chỉ mở khi state đổi; event cũ đóng trước khi mở event mới | `AttentionMonitorService` (issue #32) |
| `CameraUnavailable` confidence NULL và không vào monitored/confidence trọng số | `ConfidenceFor`, repository (issue #32) |

**Phân loại và ổn định trạng thái (`AttentionClassifier`, `AttentionStateWindow`, `AttentionStateMachine`):**

| Invariant | Thực thi |
|---|---|
| Focused commit ngay; Distracted/Absent/Drowsy cần đủ 20/30/2 giây | `AttentionStateMachine` |
| LowLight commit `CameraUnavailable` sau 30 giây | `AttentionStateMachine` |
| Cửa sổ 5–10 giây: tối thiểu 3 mẫu, majority ≥ 60%, confidence trung bình | `AttentionStateWindow` |
| `CameraUnavailable` bỏ qua window và reset mẫu | `AttentionStateWindow.Observe` |
| `EyesClosed = null` không suy đoán Drowsy | `AttentionClassifier` |
| Mỗi tick chỉ cộng thời gian khi state đã xác định; gap > `MaxSampleGap` bị bỏ | `AttentionMonitorService` (issue #32) |

### 2.8. `BlockedAttempt`

| Invariant | Thực thi |
|---|---|
| `BlockPage` → Domain, `ProcessWatcher` → App, `Extension` → Domain/URL | `EnsureObservationMatches` |
| Domain/App/URL đều được chuẩn hóa | `Record` |
| Target ≤ 512; title ≤ 512; URL không có query/fragment | `Record` |
| `CreatedAtUtc ≥ OccurredAtUtc` | `Record` |
| Session có thể NULL; best-effort, không phải tổng tuyệt đối (BR-10) | Kiểu nullable + tài liệu |
| Debounce 30 giây | Use case, không thuộc domain |

### 2.9. `AuditLog`

| Invariant | Thực thi |
|---|---|
| Description bắt buộc, single-line, ≤ 512 | `Record`, `TextSanitizer` |
| Metadata ≤ 2048; correlation không rỗng | `Record` |
| `CreatedAtUtc ≥ OccurredAtUtc` | `Record` |
| Không chứa PIN/recovery code/token/nội dung camera | Caller chịu trách nhiệm; domain không nhận plaintext secret |

---

## 3. Ranh giới thực thi

| Invariant | Domain | Database / Infrastructure | Use case |
|---|---|---|---|
| Một session Active | Không kiểm tra toàn cục | Unique partial index (issue #9) | Điều phối start/stop |
| PBKDF2 hash/salt | Kiểm tra shape, iterations | Cột hash/salt | Hashing trong Infrastructure |
| Built-in allowlist | `ProtectedDomains` chặn block; `BuiltInRuleCatalog` định nghĩa rule | Unique key `(RuleType, MatchType, NormalizedValue, Effect)` | `BlockRuleSeedService` seed sau onboarding: idempotent, không ghi đè rule phụ huynh, giữ trạng thái bật/tắt |
| Debounce blocked attempt | Không | — | `ProtectionEngine` debounce 30 giây theo domain/tên app bằng monotonic clock; ghi `BlockedAttempt` qua writer channel bất đồng bộ (best-effort) |
| Đánh giá schedule | `Covers`, `Overlaps` | Bảng `ScheduleWindows` | Scheduler local time |
| Xóa theo retention | Không | `SqliteRetentionRepository` batch 500, loại session `Active`/event mở, `incremental_vacuum` | `RetentionService` đọc hạn từ `AppSettings`, xóa detail trước session, audit `RetentionApplied` kèm lỗi từng bảng |
| Khôi phục Protection sau crash | Cờ `ProtectionEnabled` | Bảng `AppSettings` | Startup/recovery use case |
| Sở hữu hosts file | Không (domain thuần) | `HostsFileBlocker` chỉ sửa khối marker, backup/journal và restore exact khi ngoài marker không đổi; lỗi ném `HostsFileException` | `ProtectionEngine` (issue #24) gọi apply/restore/self-heal; lỗi restore khi tắt ném lỗi để giữ nguyên trạng thái bảo vệ |
| PIN gate cho thao tác nhạy cảm | Không (domain thuần) | — | `IParentAuthorizer` |
| Clean shutdown (BR-30) | Chỉ `RecordCleanShutdown` đặt `LastCleanShutdownAtUtc`; timestamp UTC và không lùi | Bảng `AppSettings` | `AppLifecycleService` sau khi gỡ chặn thành công; Windows shutdown chỉ interrupt session, không đặt cờ |

---

## 4. Quan hệ với schema

- Tên property bám theo cột trong [database_schema.md](database_schema.md) §4–§12; không có EF attribute trong Domain.
- Enum lưu dạng text ở tầng lưu trữ; tên enum giữ ổn định.
- Các giá trị dẫn xuất (`MonitoredSeconds`, `AttentionPercent`, `CoveragePercent`) được tính từ counter; tầng lưu trữ có thể snapshot theo schema khi map ở issue #9.
- Không migrate dữ liệu 1.x; domain model 2.0 là nguồn sự thật mới.

---

## 5. Business rules liên quan

- **BR-01–BR-04:** PIN policy, PBKDF2, lockout 5 lần/15 phút — domain + policy.
- **BR-07:** override schedule bị xóa khi bật/tắt thủ công hoặc tắt schedule.
- **BR-08–BR-10:** allow thắng block, domain boundary, best-effort log.
- **BR-11–BR-15:** session lifecycle, heartbeat, monitored/coverage, camera unavailable.
- **BR-17:** session crash kết thúc tại heartbeat cuối, session phục hồi tạo mới.
- **BR-20:** UTC cho timestamp, `DateOnly` local cho báo cáo.
- **BR-24:** domain thiết yếu không bị block.
- **BR-30:** clean shutdown chỉ đặt bởi luồng exit hợp lệ.

Các rule cần quyền phụ huynh (BR-05), watchdog (BR-28), block page (BR-26) và installer không thuộc domain model.

---

## 6. Kiểm thử

| File | Nội dung |
|---|---|
| `AppSettingsTests` | Protection state, override, retention, UTC/monotonic |
| `ParentCredentialTests`, `ParentPinPolicyTests` | Hash shape, lockout, PIN yếu |
| `BlockRuleTests`, `BlockRuleSetTests` | Chuẩn hóa, protected domains, matching, allow-wins, expiry, snapshot |
| `BuiltInRuleCatalogTests` | Catalog mặc định 21 rule, allowlist học tập, không trùng, version hợp lệ |
| `ScheduleWindowTests` | Biên giờ, overlap, validator |
| `StudySessionTests` | Lifecycle, counters, percentage, interrupt |
| `ActivitySampleTests` | URL/title privacy, source consistency |
| `AttentionEventTests` | Open/close, confidence, sanitization |
| `BlockedAttemptTests` | Observation channel, normalization |
| `ActivityClassifierTests`, `OnlineClassDetectorTests` | Rule snapshot, online class overlay, block/allow precedence, Unknown/Neutral, nhận diện Meet/Teams/Zoom và âm bản chat |
| `MonitoringEligibilityTests` | Truth table monitoring/only-class/online-class và mẫu số coverage |
| `AttentionClassifierTests`, `AttentionStateMachineTests`, `AttentionStateWindowTests` | Phân loại Drowsy/eyes-closed, debounce 20/30/2 giây, majority/prune/camera bypass |
| `SessionAttentionSummaryTests` | Monitored/attention/coverage, mẫu số 0, biên 50%, camera outage giảm coverage nhưng không giảm attention |
| `AuditLogTests` | Kích thước, correlation, timestamp |
| `DomainNameTests`, `PrivacySanitizerTests` | Normalization và privacy helper |

Chạy: `dotnet test src/EduFocus.Domain.Tests/EduFocus.Domain.Tests.csproj -c Release`.

Adapter hạ tầng như `HostsFileBlockerTests`, `BlockPageServerTests`, `ProcessWatcherTests` và
`ProtectionEngineTests` nằm trong `EduFocus.Infrastructure.Tests`, chạy sandbox (thư mục tạm/cổng
ephemeral/process và adapter giả, không cần elevation):
`dotnet test src/EduFocus.Infrastructure.Tests/EduFocus.Infrastructure.Tests.csproj -c Release`.

Use case tests trong `EduFocus.UseCases.Tests` phủ heartbeat (monotonic, clamp đồng hồ, biên 60 giây,
lỗi DB retry, suspend/resume, dừng khi suspended, DST), serialize enable/disable/reconcile, rollback và phục hồi `Recovery`; `ActivityTrackingServiceTests`
phủ debounce foreground, cadence 15 giây, một sample idle/chuỗi, sanitize URL, phân loại, Teams/title Meet
đa ngôn ngữ/chat/idle cho overlay lớp online, bỏ gap suspend, reset cửa sổ sau resume, forward/backward clock,
lỗi port/DB, eligible theo policy (mọi lúc/chỉ trong lớp/tắt monitoring/idle) và dừng khi session kết thúc.
`ProtectionService`/`AppLifecycleService` test attention start/stop theo phiên, rollback và shutdown; monitor
test event → counter → `SessionAttentionSummary`.
`EduFocus.SqliteDataAccess.Tests` phủ heartbeat có điều kiện, chống hồi sinh session, unique active,
retention job (bảng quá hạn, session `Active`, event còn mở, batch session + cascade/SET NULL, vacuum),
round-trip `ActivitySample` (kể cả `IsOnlineClass`) và
`AddActivitySecondsAsync` (targeted, chỉ session `Active`, cộng `MonitoringEligibleSeconds`).

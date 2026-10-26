# EduFocus domain invariants

The `EduFocus.Domain` project is framework independent. It has no EF Core, WPF, or Windows API references. Factories and methods on its entities enforce the rules below; persistence must enforce concurrent invariants too (in particular, the partial unique index for an active session).

- All persisted timestamps are `DateTimeOffset` values with UTC offset `+00:00`. Durations and counters cannot be negative.
- `AppSettings` has one fixed singleton ID. Protection is represented by the schema's `Off`/`On` state; a clean shutdown records its UTC time and clears protection.
- `ParentCredentials` accepts only validated PBKDF2 hash/salt records (32-byte hash, 16-byte salt, at least 600,000 iterations). It never accepts a plaintext PIN or recovery code. Five consecutive failed attempts lock both credential flows for 15 minutes; successful authentication clears the counter and lockout.
- Block rules use schema enum values. Domain values are lowercased and reduced to their host; app values are trimmed and lowercased. Only enabled, unexpired, enforced exact rules participate in MVP matching, and Allow wins over Block. Non-exact and URL rules are retained as `NotEnforced` for post-MVP support.
- Schedule days use Monday bit 0 through Sunday bit 6. Windows use `[start, end)` intervals, must fit within one day, and enabled windows cannot overlap on a shared weekday.
- A session starts `Active`, and its factory rejects an existing active session. Completion and abort end at a supplied UTC time and set the heartbeat to that time. Interruption closes at the last heartbeat. Active time is accumulated explicitly and does not derive from wall-clock duration. Attention and activity counters are accumulated separately; online-class time is an overlay. Attention and coverage percentages are null when their denominator is zero.
- Activity URLs are HTTP(S) only and have query strings/fragments stripped. Domain fields are normalized. No captured page content, camera frame, landmark, or biometric template belongs in the domain model.
- An attention event can be closed once and cannot end before it starts. Blocked attempts are best-effort observations. Audit metadata must be valid JSON and is rejected when secret-like keys (PIN, password, recovery, token, or secret) appear.

These rules are covered by `EduFocus.Domain.Tests/Entities/DomainEntityTests.cs`. EF mapping and database enforcement are intentionally outside this issue.

# Watchdog and tamper-resistance spike

Issue: #3, “Spike: Đánh giá watchdog và khả năng chống tắt”

## Recommendation

For the MVP, use a Windows Task Scheduler task as the recovery launcher, configured for the current user and created/removed by the installer. Use a logon trigger and a one-minute repeat/retry interval; treat recovery as roughly a minute or longer and measure on supported Windows versions. Do not promise a sub-minute restart. Do not run the WPF UI as a Windows Service. Microsoft documents Task Scheduler's restart-on-failure count/interval, with a one-minute minimum interval, and logon/repetition triggers.

Only restart when persisted protection state is ON and the previous shutdown was not clean. The app should acquire a per-user, per-product named mutex at startup. The watchdog should use bounded exponential backoff (for example 5, 10, 20, 40, then 60 seconds), cap consecutive attempts, and stop retrying after a clean exit or when protection is OFF. Emit an `AuditLog` event `WatchdogRestart` with UTC time, restart count, and a short reason; do not persist command-line contents, secrets, or camera data. On recovery, close the prior session at its last heartbeat, exclude downtime, reapply protection, and create a recovery session only if the persisted schedule/session policy requires it.

The scheduled task is a best-effort deterrent. A local administrator can stop or remove it, and Windows shutdown/logoff creates a recovery gap. Reassess a service only if Phase 7 requires stronger reliability and a secure, ACL-restricted IPC protocol is designed.

## Mechanism comparison

| Mechanism | Complexity | Rights and reliability | Removal and limits | Assessment |
| --- | --- | --- | --- | --- |
| Scheduled Task | Low to medium; installer must register a task and a logon/repeat trigger | Runs in configured user context; restart-on-failure has a one-minute minimum interval; a repeat trigger can check at one-minute intervals | Installer must disable/delete the task before removing binaries; local admin can disable it; no recovery while Windows is off or user is logged out | Recommended MVP recovery launcher; expected delay is about one minute or more |
| Separate watchdog process | Medium to high; lifecycle, IPC, logging, duplicate handling, and backoff are needed | Can notice a child exit quickly while alive, but same-user tools can terminate both processes | Must be stopped before uninstall; needs an external launcher to survive its own termination; UI/process boundaries add complexity | Prototype proves mechanics; do not ship as the sole launcher |
| Auto-start (HKCU Run / Startup) | Low | Starts at the next user logon; does not detect a crash during the current logon | Remove the exact product value/shortcut during uninstall; user can disable it | Useful for logon recovery only; not a watchdog |
| Windows Service | High; service installer, service account, recovery policy, IPC, and diagnostics | Service Control Manager can apply configured recovery actions; a service has no direct interactive UI and must communicate with a user-session app | Requires administrative installation and explicit stop/delete rollback; named-pipe IPC must be authenticated and ACL-restricted; service can be disabled by admin | Defer; evaluate for hardened production, outside this spike's implementation scope |

## Prototype and results

`tools/WatchdogSpike` is an isolated .NET 10 process harness; it does not install a task/service, modify registry state, or launch the real WPF app. Run:

```powershell
dotnet run --project tools/WatchdogSpike/WatchdogSpike.csproj -- --demo
```

The demo starts a child process, starts a second watchdog to verify the single-instance guard, kills the child three times, and checks that the watchdog records two restart events with increasing backoff before opening its retry circuit. A separate clean-exit run confirms that a normal exit is not restarted. Output records UTC JSON events named `WatchdogRestart`, `WatchdogCircuitOpen`, and `WatchdogCleanExit`.

Observed in this environment: duplicate instance rejected; two restarts recorded; backoff increased from 100 ms to 200 ms in the accelerated demo; retry circuit opened after the configured limit; clean exit produced no restart. This is a deterministic process-level prototype, not a Task Scheduler, reboot, WPF recovery, or administrator-tamper test.

## Current repository behavior and gap

The WPF app currently uses a named mutex to avoid a second UI instance. It also adds/removes an HKCU `Run` value while a focus session is active. That auto-start entry only helps at the next sign-in; there is no watchdog, persisted clean-shutdown marker, scheduled task, or `WatchdogRestart` database write in the current app. These existing mechanisms must not be described as crash recovery.

## Phase 7 follow-up

1. Persist `ProtectionEnabled`, last heartbeat, and an explicit clean-shutdown timestamp before implementing recovery.
2. Register a task only while protection/recovery is configured; scope it to the intended user and least privilege.
3. Implement a stable product-specific mutex and ensure a task invocation activates the existing process instead of opening another UI.
4. Add bounded retries, clean-exit suppression, uninstall cleanup, and an integration test that kills the app and observes recovery without counting the downtime.
5. Verify reboot/logon and standard-user behavior on supported Windows versions. Document that local administrators can still disable or remove the task.

## Verification

- `dotnet run --project tools/WatchdogSpike/WatchdogSpike.csproj --no-restore -- --demo`: `DEMO_RESULT: PASS`.
- The prototype ran on .NET SDK 10.0.102 on Windows. It used short test delays; production timing remains a configuration/design recommendation, not a measured SLA.

## References

- [Task Scheduler restart-on-failure settings](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-restartonfailure-settingstype-element)
- [Task Scheduler trigger repetition](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-repetition-triggerbasetype-element)
- [Windows services and user interaction](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services)

# Checklist E2E bảo mật & quyền riêng tư (issue #42)

Tài liệu này là cổng kiểm thử bảo mật trước khi phát hành MVP. Nó gom **bằng chứng tự động**
đã có trong test suite và **checklist thủ công trên VM sạch Windows 10/11**. Kết quả từng lần
chạy được ghi vào release checklist (issue #43) theo [release-process.md](./release-process.md) §6.

Liên quan: [SECURITY.md](../SECURITY.md), [installer.md](./installer.md) §7,
[use_cases.md](./use_cases.md) §6.3, [system_architecture.md](./system_architecture.md) §6.2.

---

## 1. Phạm vi

| Nhóm | Nội dung kiểm thử |
|---|---|
| Onboarding & PIN | Consent bắt buộc, PIN 6–8 số, hash PBKDF2, recovery code dùng một lần, lockout chung 5 lần/15 phút |
| Protection | Bật/tắt bằng PIN, rollback khi hosts/engine lỗi, reconcile sau crash, lịch bảo vệ |
| Web/app blocking | Hosts marker domain exact, block page loopback, process watcher, allowlist, tắt Secure DNS Chrome/Edge khi bảo vệ |
| Toàn vẹn hosts | Restore đúng byte gốc, giữ chỉnh sửa ngoài marker, self-heal sau crash, dọn backup |
| Quyền riêng tư camera | Không lưu frame/landmark/embedding, dispose frame trước khi phát sample, camera chỉ theo consent |
| Dữ liệu & log | ACL `%ProgramData%\EduFocus`, không có PIN/recovery code trong DB/WAL/backup/log, redaction URL/token |
| Mã hóa DB | SQLCipher cho DB/WAL/backup, khóa DPAPI `LocalMachine` không plaintext, fail-closed khi thiếu/sai/hỏng khóa, backup/restore cùng khóa, quy trình mất khóa |
| Chống injection | XSS trong block page, CSV formula injection, Host header/path/query không bị phản chiếu |
| Gỡ cài đặt | Gỡ hosts marker, block page listener, scheduled task, runtime; tùy chọn xóa dữ liệu |

**Ngoài phạm vi MVP:** bypass bằng quyền Administrator/SYSTEM, DoH/DoT của Firefox hoặc ứng dụng khác,
VPN/proxy, sửa registry. Secure DNS của Chrome/Edge được tắt bằng machine policy khi bảo vệ ON và phải
khôi phục khi OFF/gỡ (SEC-E2E-29).
Các điểm còn lại là giới hạn đã biết trong [SECURITY.md](../SECURITY.md#giới-hạn-đã-biết-không-phải-lỗ-hổng).

---

## 2. Nguyên tắc an toàn khi kiểm thử

- **Không dùng dữ liệu camera thật của trẻ.** Trên VM, dùng cảnh giả lập (bàn trống, ảnh test
  pattern) hoặc kiểm tra nhánh `CameraUnavailable`; không ghi hình, không chụp ảnh màn hình có
  người thật. Không đưa frame/ảnh vào issue, PR hay artifact.
- **Không ghi PIN/recovery code thật vào bằng chứng.** Dùng PIN kiểm thử riêng và che (mask)
  trong ảnh chụp; không dán recovery code vào issue/CHANGELOG/artifact.
- **Không dán URL đầy đủ có query/token** vào báo cáo. Chỉ ghi domain đã làm sạch.
- Chạy trên **VM sạch** có snapshot trước khi cài; gỡ cài đặt/xóa dữ liệu là thao tác không thể
  hoàn tác trên máy đó.
- Mỗi bước ghi **bằng chứng tối thiểu**: ảnh chụp đã che secret, output lệnh, mã build/hash.
- Không thay đổi/không tắt cơ chế bảo vệ của Windows hay antivirus để "làm test dễ hơn"; ghi lại
  nếu môi trường chặn (ví dụ chính sách cấm scheduled task).

---

## 3. Bằng chứng tự động

Chạy toàn bộ suite Release trước mỗi lần phát hành:

```powershell
dotnet test EduFocus.slnx --configuration Release
```

Các test dưới đây là bằng chứng hồi quy cho từng kiểm soát. Tên đầy đủ có thể chạy riêng bằng
`--filter "FullyQualifiedName~<tên lớp>"`.

| Kiểm soát | Bằng chứng tự động | Trạng thái |
|---|---|---|
| Consent chặn thiết lập PIN và hoàn tất onboarding | `OnboardingServiceTests.Pin_setup_is_rejected_until_consent_is_recorded`, `Successful_onboarding_persists_consent_preferences_pin_and_completion` | Đạt |
| PIN/recovery chỉ lưu hash; lockout chung | `ParentAuthPersistenceTests.Setup_login_and_recovery_survive_a_process_restart`, `Lockout_survives_a_restart_and_expires_after_fifteen_minutes`; `ParentAuthUseCaseTests.Fifth_wrong_pin_locks_for_fifteen_minutes_and_audits_lockout`, `Recovery_code_failures_share_the_pin_lockout` | Đạt |
| Không có PIN/recovery code dạng plaintext trong DB, WAL, backup | `ParentAuthPersistenceTests.Database_wal_and_backup_files_never_contain_plaintext_secrets` | Đạt |
| Bật/tắt bảo vệ rollback đúng khi hosts/engine lỗi | `ProtectionEngineTests.Apply_rolls_back_when_hosts_fails`, `Remove_throws_when_hosts_restore_fails_but_stops_adapters`; `ProtectionServiceTests.Engine_apply_failure_keeps_protection_off`, `Engine_remove_failure_keeps_protection_on` | Đạt |
| Tắt Secure DNS Chrome/Edge, backup/khôi phục chính xác, lỗi chỉ cảnh báo | `BrowserDnsPolicyTests.Apply_sets_off_for_every_target_and_view`, `Restore_puts_back_the_previous_values_and_removes_added_ones`, `Apply_is_idempotent_and_keeps_the_first_backup`, `Apply_leaves_a_policy_of_an_unexpected_type_untouched`; `ProtectionEngineTests.Apply_disables_browser_secure_dns`, `Apply_keeps_os_protection_when_secure_dns_policy_fails`, `Apply_rolls_back_secure_dns_policy_when_hosts_fails`, `Remove_restores_browser_secure_dns` | Đạt |
| Hosts chỉ sửa trong marker, domain exact, ghi IPv4 + IPv6 | `HostsFileBlockerTests.Apply_writes_marker_section_with_ipv4_and_ipv6_entries`, `Apply_rejects_invalid_domains_without_touching_hosts` | Đạt |
| Restore đúng byte gốc, giữ sửa đổi ngoài marker | `HostsFileBlockerTests.Restore_returns_the_original_bytes`, `Restore_keeps_edits_made_outside_the_block_section` | Đạt |
| Self-heal sau crash bằng journal; journal/backup được dọn | `HostsFileBlockerTests.SelfHeal_restores_the_original_hosts_after_a_crash`, `Restore_returns_the_original_bytes` (assert không còn `.bak`) | Đạt |
| Không xóa file ngoài thư mục backup | `HostsFileBlockerTests.Restore_does_not_delete_a_file_outside_the_backup_directory` | Đạt |
| Log hosts không chứa domain/nội dung hosts | `HostsFileBlockerTests.Apply_logs_counts_but_not_domain_values` | Đạt |
| Block page chống XSS/Host injection, host exact | `BlockPageServerTests.Host_header_injection_returns_generic_not_found`, `Path_and_query_injection_are_not_reflected`, `Host_matching_is_exact_for_the_blocked_domain` | Đạt |
| Block page có CSP/security header, không tài nguyên ngoài | `BlockPageServerTests.Blocked_response_has_static_security_headers`, `Blocked_page_does_not_reference_external_resources` | Đạt |
| Block page/log không lộ domain, path, query | `BlockPageServerTests.Blocked_request_raises_hit_with_domain_only`, `Logs_never_contain_blocked_domain_or_target` | Đạt |
| Cổng 80 bận không tắt bảo vệ | `ProtectionEngineTests.Apply_returns_warning_when_block_page_port_is_busy`, `BlockPageServerTests.Port_conflict_returns_warning_without_throwing` | Đạt |
| App bị chặn: WM_CLOSE trước, kill sau timeout, bỏ qua system/allowlist | `ProcessWatcherTests.Closes_blocked_windowed_process_gracefully`, `Kills_process_and_warns_when_close_times_out`, `Protected_system_and_edufocus_processes_are_skipped`, `Allowlisted_process_is_never_touched` | Đạt |
| Process watcher không log window title/path/danh sách process | `ProcessWatcherTests.Start_and_stop_are_logged_without_process_details`, `Process_observation_writes_app_attempt_without_window_data` | Đạt |
| Camera: frame dispose trước khi phát sample, không lưu ảnh | `AttentionPipelineServiceTests.Sample_once_disposes_the_frame_before_publishing_the_sample`, `Loop_samples_repeatedly_until_stopped` | Đạt |
| Schema không có cột ảnh/BLOB/landmark/embedding | `SchemaTests.Schema_has_no_binary_or_camera_data_columns` | Đạt |
| ACL thư mục dữ liệu và file con chỉ Administrators/SYSTEM | `DataDirectorySecurityTests.Apply_restricts_directory_and_children_to_administrators_and_system`, `Files_inside_a_secured_directory_carry_only_the_restricted_acl`, `EnsureSecure_creates_a_missing_directory_with_the_restricted_acl` | Đạt |
| Redaction PIN/recovery/token/URL trong log (kể cả không dấu phân cách) | `LogSanitizerTests.Redact_RemovesSecretValue`, `Redact_RemovesSecretValueWithoutSeparator`, `Redact_KeepsHarmlessTextThatMentionsPins`; `FileLoggerTests.Write_redacts_whitespace_secrets_nested_exceptions_and_url_details` | Đạt |
| CSV formula injection, BOM, không ghi đè, không thoát thư mục | `ReportExportServiceTests.Export_escapes_cells_that_start_with_formula_characters`; `LocalCsvExportStoreTests.Save_writes_utf8_bom_so_excel_reads_vietnamese`, `Save_never_overwrites_an_existing_file`, `Save_rejects_names_that_escape_the_export_directory` | Đạt |
| Crash/khởi động lại: reconcile, interrupt session cũ, phục hồi bảo vệ | `ProtectionServiceTests.Reconcile_with_protection_on_interrupts_stale_session_and_recovers`, `AppLifecycleServiceTests.Startup_after_system_shutdown_recovers_protection` | Đạt |
| Shutdown/logoff không chặn OS, giữ Protection ON | `AppLifecycleServiceTests.System_shutdown_interrupts_the_session_and_keeps_protection`, `System_shutdown_swallows_database_failures` | Đạt |
| Thoát bằng PIN, gỡ chặn thất bại thì không thoát | `AppLifecycleServiceTests.Exit_with_pin_cleans_state_and_records_a_clean_shutdown`, `Exit_without_removing_the_engine_keeps_the_app_running` | Đạt |
| Watchdog restart có budget, tôn trọng clean exit, không log dữ liệu người dùng | `WatchdogServiceTests.Tick_does_not_restart_after_a_clean_exit_marker`, `Tick_gives_up_after_the_budget_and_audits_once`, `Tick_audits_only_reason_attempt_and_pids` | Đạt |
| CLI gỡ cài đặt chỉ xóa dữ liệu khi được yêu cầu | `WatchdogCommandLineTests.Uninstall_mode_deletes_user_data_only_when_requested` | Đạt |
| IPC chỉ phục vụ client đúng ảnh tiến trình; client lạ bị từ chối, không chạy lệnh | `ServiceControlServerTests.A_rejected_client_is_logged_and_no_command_runs`; `ServiceControlClientTests.A_pipe_server_that_is_not_the_installed_service_is_rejected`, `An_unavailable_service_is_reported_without_falling_back_locally` | Đạt |
| IPC chống request quá lớn/malformed/thiếu frame và timeout; lỗi trả về đã làm sạch | `ServiceControlFramingTests`; `ServiceControlServerTests.An_oversized_request_is_rejected_without_dispatch`, `A_malformed_json_request_is_rejected_without_dispatch`, `A_handler_error_becomes_a_sanitized_internal_error`, `A_command_that_exceeds_the_server_timeout_is_reported` | Đạt |
| Lệnh cha cần Parent Mode; lệnh hệ thống không cần; PIN không đi qua pipe | `ServiceControlClientTests.A_parent_command_without_parent_mode_never_reaches_the_pipe`, `Schedule_commands_do_not_require_parent_mode`; `ServiceCommandHandlerTests.Parent_commands_run_inside_an_authorized_scope`, `System_commands_run_without_parent_mode`, `A_parent_authorization_failure_maps_to_unauthorized` | Đạt |
| Service stop giữ bảo vệ, interrupt session, audit; restart reconcile lại | `ServiceRuntimeTests.Stop_interrupts_the_active_session_but_never_disables_protection`, `A_restarted_service_reuses_the_database_and_reconciles_again`, `A_failed_start_leaves_no_running_runtime_or_pipe` | Đạt |
| Extension chống XSS/URL injection, redirect chain và chỉ gửi URL khớp rule | `browser-extension/tests/matcher.test.mjs` (`HTML-looking paths...`, `server redirect chains...`), `static.test.mjs` (`the block page never reads or renders...`, `the service worker reports only blocked URLs...`); `BrowserExtensionBridgeTests.Verified_blocked_message_raises_attempt_with_sanitized_url`, `Blocked_message_that_does_not_match_the_snapshot_is_ignored` | Đạt |
| MSI đăng ký native messaging và native host đọc đúng manifest | `NativeMessagingRegistrationTests`, `NativeHostOptionsTests`; kiểm tra COM trong quy trình install | Đạt |
| Migration lỗi giữ DB và backup, restore được | `InitializationTests.Failed_upgrade_rolls_back_and_the_backup_restores_the_previous_data`, `BackupRestoreTests.Restore_rejects_missing_or_corrupt_backup_without_touching_database` | Đạt |
| DB được mã hóa, không đọc được nếu không có khóa | `EncryptionTests.Database_file_is_encrypted_and_unreadable_without_the_key`, `Wal_and_database_files_contain_no_plaintext` | Đạt |
| Khóa DPAPI không chứa plaintext và log không lộ khóa | `EncryptionTests.Key_file_stores_a_dpapi_protected_key_and_no_plaintext_key_material`, `Logs_never_contain_the_encryption_key` | Đạt |
| Fail-closed khi thiếu/sai/hỏng khóa, không ghi đè dữ liệu/khóa | `EncryptionTests.Missing_key_file_fails_closed_and_is_not_recreated`, `Damaged_key_file_is_reported_and_never_overwritten`, `Wrong_key_is_rejected_and_the_database_and_key_file_are_kept` | Đạt |
| Nâng cấp plaintext giữ dữ liệu; lỗi giữ file gốc | `EncryptionTests.Plaintext_database_is_upgraded_in_place_with_data_preserved`, `Corrupt_plaintext_database_upgrade_keeps_the_original_file` | Đạt |
| Backup/restore mã hóa, backup khóa khác bị từ chối, backup legacy được mã hóa | `EncryptionTests.Encrypted_backup_cannot_be_read_without_the_key_and_restores_data`, `Backup_encrypted_with_a_different_key_is_rejected`, `Legacy_plaintext_backup_is_encrypted_during_restore` | Đạt |

> Các test trên chạy trên Windows runner/local. Chúng chứng minh logic và adapter trong sandbox,
> **không thay thế** kiểm thử trên máy sạch với hosts file, port 80 và scheduled task thật.

---

## 4. Checklist trên VM sạch (bắt buộc trước phát hành)

Chuẩn bị: VM Windows 10 22H2 x64 và Windows 11 x64, snapshot sạch, tài khoản Administrator để cài
và một tài khoản standard user để chạy thử. Ghi mã build/MSI SHA-256 vào cột bằng chứng.

| ID | Thao tác | Kết quả mong đợi | Bằng chứng | Win10 | Win11 |
|---|---|---|---|---|---|
| SEC-E2E-01 | Cài MSI bằng Administrator | Cài thành công; shortcut Desktop/Start Menu; `%ProgramData%\EduFocus` tồn tại; `icacls` chỉ Administrators/SYSTEM | Ảnh chụp `icacls`, Apps & Features | ⬜ | ⬜ |
| SEC-E2E-02 | Mở app bằng standard user | UAC xuất hiện (độ lệch elevation đã biết); sau khi nâng quyền app chạy, không lỗi quyền dữ liệu | Ảnh chụp UAC + app | ⬜ | ⬜ |
| SEC-E2E-03 | Onboarding: từ chối consent, thử thiết lập PIN; sau đó đồng ý và hoàn tất | Không thể qua bước PIN/hoàn tất khi thiếu consent; recovery code chỉ hiển thị một lần; PIN không hiển thị lại | Ảnh chụp đã che PIN/code | ⬜ | ⬜ |
| SEC-E2E-04 | Nhập sai PIN 5 lần; thử recovery code sai trong lúc khóa | Khóa 15 phút, đếm chung cho PIN/recovery; hết hạn đăng nhập lại được | Ảnh chụp thông báo khóa (không có mã) | ⬜ | ⬜ |
| SEC-E2E-05 | Bật bảo vệ | Hosts có marker `EDUFOCUS BLOCK START/END`; block page lắng nghe `127.0.0.1:80`; service `EduFocusProtection` **Running**; marker `HKLM\SOFTWARE\EduFocus\ServiceMode=1`; audit `ProtectionOn` | `Get-Content hosts`, `Get-NetTCPConnection -LocalPort 80`, `Get-Service EduFocusProtection`, `Get-ItemProperty` | ⬜ | ⬜ |
| SEC-E2E-06 | Truy cập domain bị chặn qua HTTP và HTTPS | HTTP hiển thị trang chặn; HTTPS báo lỗi kết nối/SSL nhưng domain vẫn bị chặn; không có path/query trong `BlockedAttempts`/log | Ảnh chụp trang chặn; query DB/log đã lọc | ⬜ | ⬜ |
| SEC-E2E-07 | Mở app bị chặn có dữ liệu chưa lưu | App nhận `WM_CLOSE` trước; quá 2 giây mới bị kill; cảnh báo mất dữ liệu được ghi trong tài liệu | Ảnh chụp + log không có window title | ⬜ | ⬜ |
| SEC-E2E-08 | Thêm app vào allowlist khi đang bị chặn | Allowlist thắng blocklist; process hệ thống/EduFocus không bị tác động | Quan sát + DB `BlockedAttempts` | ⬜ | ⬜ |
| SEC-E2E-20 | Cài extension, bật rule URL/path, mở URL khớp rule (kèm query/fragment) | Tab chuyển tới trang chặn không lộ URL; `BlockedAttempts` ghi URL đã bỏ query/fragment/credentials; rule allow/protected domain thắng | Ảnh chụp popup/trang chặn; query DB đã lọc | ⬜ | ⬜ |
| SEC-E2E-21 | Tắt bảo vệ hoặc thoát EduFocus khi extension đang chạy | Không còn chặn (fail-open), popup **Chưa kết nối EduFocus**; bật lại thì chặn có hiệu lực trong vài giây, không cần khởi động lại trình duyệt | Ảnh chụp popup + timeline | ⬜ | ⬜ |
| SEC-E2E-22 | Kiểm tra service sau khi cài | `EduFocusProtection` **Running**, StartType Automatic, chạy LocalSystem; `EduFocus.Service.exe --health` healthy; marker `ServiceMode=1`; log service nằm trong `%ProgramData%\EduFocus\logs` và không có secret | `Get-CimInstance Win32_Service`, `--health`, `Select-String` log | ⬜ | ⬜ |
| SEC-E2E-23 | Đóng UI khi Protection ON | Hosts/block page/process watcher vẫn hoạt động; không có engine cục bộ thứ hai; mở lại UI thấy trạng thái từ service | Thử chặn web/app + `Get-Process` + ảnh chụp UI | ⬜ | ⬜ |
| SEC-E2E-24 | Kill `EduFocus.Service.exe` khi Protection ON; thử client lạ kết nối pipe | SCM restart trong khoảng 60 giây; session cũ `Interrupted` ghi chú `ServiceStopped`, audit `ProtectionServiceStopped`/`SessionInterrupted`; client lạ bị từ chối và ghi log Security, không lệnh nào chạy | Timeline + audit + log service | ⬜ | ⬜ |
| SEC-E2E-25 | Nâng cấp MSI khi service đang chạy | Service dừng rồi khởi động lại; DB/settings giữ nguyên; chỉ một entry Apps & Features; không còn process/pipe mồ côi | Timeline + `Get-Service` + `Get-Process` | ⬜ | ⬜ |
| SEC-E2E-26 | Chạy portable ZIP trên máy chưa cài | Không có service row/marker; app chạy standalone với watchdog, không gọi pipe và không tạo orphan service | `Get-Service EduFocusProtection`, `Get-ItemProperty` | ⬜ | ⬜ |
| SEC-E2E-27 | Kiểm tra DB mã hóa và file khóa sau onboarding | 16 byte đầu `edufocus.db` khác `SQLite format 3\0`; `edufocus.key` tồn tại, chỉ Administrators/SYSTEM đọc, không có chuỗi hex/`x'...'` khóa; backup trong `backups/` cũng mã hóa; standard user không đọc được DB/WAL/backup/khóa | PowerShell đọc 16 byte header; `icacls`; thử đọc bằng standard user; `Select-String` trên file khóa | ⬜ | ⬜ |
| SEC-E2E-28 | Mô phỏng mất/hỏng/sai khóa (trên VM snapshot) rồi khôi phục | Thiếu khóa: app báo lỗi an toàn, không tạo khóa mới; hỏng khóa: không ghi đè; khôi phục bản sao `edufocus.key` trên cùng máy + backup thì mở lại được, dữ liệu còn nguyên. Copy DB sang VM khác không mở được | Ảnh chụp lỗi; so sánh hash file khóa trước/sau; timeline restore | ⬜ | ⬜ |
| SEC-E2E-29 | Bật bảo vệ khi Chrome đang bật Secure DNS; mở domain bị chặn; tắt bảo vệ; gỡ cài đặt | Khi ON: `DnsOverHttpsMode=off` dưới policy máy Chrome/Edge (cả 64/32) và backup trong `HKLM\SOFTWARE\EduFocus\BrowserDnsPolicy`; sau khi khởi động lại Chrome, domain bị chặn dù trước đó DoH hoạt động. Khi OFF/gỡ: value policy cũ được khôi phục đúng (hoặc bị xóa nếu trước đó không có) và backup key biến mất; lỗi chỉ là cảnh báo, không tắt bảo vệ | `Get-ItemProperty` policy + backup; ảnh chụp Chrome trước/sau; log không có secret | ⬜ | ⬜ |
| SEC-E2E-09 | Bản portable/source: kill app khi Protection ON; sau đó kill watchdog | App được khôi phục ≤ 30 giây; watchdog được task khôi phục trong tối đa ~60 giây; audit `WatchdogRestart` không có dữ liệu người dùng | Timeline + audit | ⬜ | ⬜ |
| SEC-E2E-10 | Sửa hosts ngoài marker, sau đó tắt bảo vệ | Chỉ mất khối marker; chỉnh sửa ngoài marker còn nguyên; `hosts-backup\journal.json` và `.bak` không còn | Diff hosts trước/sau, `Get-ChildItem hosts-backup` | ⬜ | ⬜ |
| SEC-E2E-11 | Ép crash giữa lúc apply (kill app sau khi có journal) rồi khởi động lại | `SelfHealAsync` khôi phục đúng byte gốc; không còn marker/journal/backup | So sánh hash hosts | ⬜ | ⬜ |
| SEC-E2E-12 | Bật monitoring với cảnh giả lập/không người; dừng phiên | Không tạo file ảnh/video; camera được nhả; `AttentionEvents` chỉ có số liệu, không frame/landmark | `Get-ChildItem` tìm ảnh/video; query schema | ⬜ | ⬜ |
| SEC-E2E-13 | Kiểm tra ACL và file sinh ra sau khi chạy | DB, `-wal`/`-shm`, backup đều chỉ Administrators/SYSTEM; standard user bị từ chối đọc/ghi | `icacls` từng file; thử đọc bằng standard user | ⬜ | ⬜ |
| SEC-E2E-14 | Tạo lỗi PIN/recovery và URL có query để sinh log | Log không có PIN/recovery/token/URL đầy đủ; chỉ có domain đã làm sạch | `Select-String` trên log (mẫu đã che) | ⬜ | ⬜ |
| SEC-E2E-15 | Xuất CSV báo cáo có ô bắt đầu `=`, `+`, `-`, `@` | Ô được escape; file UTF-8 BOM; không ghi đè; không có window title/URL đầy đủ | Mở CSV bằng Excel + hex BOM | ⬜ | ⬜ |
| SEC-E2E-16 | Gỡ cài đặt, **giữ dữ liệu** | Không còn hosts marker, block page listener, service `EduFocusProtection`, marker `ServiceMode`, watchdog/scheduled task cũ, `runtime`, `hosts-backup`; `edufocus.db` còn | `Get-Content hosts`, `Get-NetTCPConnection`, `Get-Service`, `Get-ScheduledTask`, `Get-ChildItem` | ⬜ | ⬜ |
| SEC-E2E-17 | Gỡ cài đặt, **xóa dữ liệu** | `%ProgramData%\EduFocus` không còn; không thể khôi phục | `Test-Path` | ⬜ | ⬜ |
| SEC-E2E-18 | Shutdown/logoff khi Protection ON, sau đó đăng nhập lại | Windows tắt bình thường; lần đăng nhập sau session `Recovery`, Protection ON như cũ | Timeline + audit | ⬜ | ⬜ |
| SEC-E2E-19 | Rà soát artifact/bằng chứng trước khi lưu | Không có PIN, recovery code, ảnh camera, URL đầy đủ, DB/log trong artifact | Checklist thủ công trên thư mục bằng chứng | ⬜ | ⬜ |

> Đánh dấu ✅/❌ và ghi chú vào cột tương ứng. Mọi ❌ phải có finding ID ở §5 hoặc issue mới.
> Kết quả cuối cùng chép vào release checklist (issue #43).

---

## 5. Findings

Mức độ: **P0** chặn phát hành ngay; **P1** phải sửa trước phát hành; **P2** có kế hoạch theo dõi;
**P3** hardening/nhỏ.

| ID | Mức | Trạng thái | Mô tả | Xử lý / theo dõi |
|---|---|---|---|---|
| F-42-01 | P2 | Mở | `ActivitySamples`/`BlockedAttempts` lưu window title đã sanitize và truncate (không có trong báo cáo/CSV). Đây là dữ liệu nhạy cảm tiềm tàng (tên tài liệu) trong retention 30/90 ngày. | Ghi rõ trong thông báo quyền riêng tư và DPIA (#44); cân nhắc tắt lưu title ở bản sau nếu không cần cho mục đích học tập. |
| F-42-02 | P3 | Đã sửa (#42) | Trước đây restore chỉ xóa `journal.json`, để lại file `.bak` trong `hosts-backup`. | `HostsFileBlocker` xóa backup sau restore/self-heal thành công, chỉ khi đường dẫn nằm trong thư mục backup; có test path containment. |
| F-42-03 | P3 | Chấp nhận | Log kỹ thuật có thể chứa đường dẫn `%ProgramData%\EduFocus`/`%LocalAppData%` nhưng không có secret hay nội dung học tập. | Không cần sửa; đã nằm trong ACL/redaction review. |
| F-42-04 | P3 | Chấp nhận | Hosts có thể bị bypass bằng DoH/DoT, VPN/proxy, quyền Administrator hoặc antivirus khóa file. | Là giới hạn đã biết trong [SECURITY.md](../SECURITY.md); không phải lỗ hổng theo mô hình mối đe dọa. |
| F-42-05 | P2 | Mở | Chưa có thông báo quyền riêng tư đầy đủ và DPIA chính thức trước phát hành. | Theo dõi ở issue #44; không chặn #42 nhưng **chặn phát hành thương mại**. |

**Kết luận:** tính đến 2026-10-30, review mã + test tự động **không phát hiện P0/P1**. Các mục thủ công
SEC-E2E-01…29 là điều kiện bắt buộc; nếu phát hiện P0/P1 khi chạy VM, phát hành bị chặn cho đến khi
sửa và chạy lại.

---

## 6. Cổng phát hành và sign-off

Chỉ coi là đạt khi:

- [ ] `dotnet test EduFocus.slnx --configuration Release` pass toàn bộ, build 0 warning.
- [ ] Toàn bộ SEC-E2E-01…29 đạt trên **Windows 10 22H2** và **Windows 11** (hoặc có ngoại lệ được
      ghi rõ lý do môi trường).
- [ ] Không còn P0/P1 mở; P2 có issue theo dõi và rủi ro được ghi trong release notes.
- [ ] Bằng chứng không chứa PIN/recovery code/ảnh camera/URL đầy đủ; artifact release không có
      `.db`/`.log`/backup hay chứng chỉ/private key.
- [ ] Sau gỡ cài đặt: hosts sạch marker, listener đóng, scheduled task biến mất, `hosts-backup` và
      `runtime` bị xóa (khi giữ dữ liệu, chỉ DB/settings còn).
- [ ] Kết quả được chép vào release checklist (#43) kèm version, commit, MSI SHA-256, ngày, người chạy.

| Version | Commit | MSI SHA-256 | Win10 22H2 | Win11 | Người chạy | Ngày |
|---|---|---|---|---|---|---|
|  |  |  | ⬜ | ⬜ |  |  |

---

## 7. Lệnh hỗ trợ thu thập bằng chứng

```powershell
# Phiên bản và hash gói cài
Get-FileHash .\EduFocus-<version>-x64.msi -Algorithm SHA256
Get-AuthenticodeSignature .\EduFocus-<version>-x64.msi | Format-List Status, SignerCertificate

# Hosts và scheduled task sau khi bật/gỡ bảo vệ
Select-String -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Pattern "EDUFOCUS BLOCK"
Get-ScheduledTask -TaskName "EduFocus*" -ErrorAction SilentlyContinue
Get-ChildItem "$env:ProgramData\EduFocus\hosts-backup" -ErrorAction SilentlyContinue

# Service bảo vệ và marker service mode
Get-Service EduFocusProtection | Format-List Status, StartType
Get-CimInstance Win32_Service -Filter "Name='EduFocusProtection'" |
  Format-List StartName, State, ProcessId
Get-ItemProperty "HKLM:\SOFTWARE\EduFocus" -Name ServiceMode -ErrorAction SilentlyContinue
& "$env:ProgramFiles\EduFocus\EduFocus.Service.exe" --health

# Block page listener (chỉ nên thấy khi Protection ON)
Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 80 -ErrorAction SilentlyContinue

# ACL dữ liệu và file con
icacls "$env:ProgramData\EduFocus"
Get-ChildItem "$env:ProgramData\EduFocus" -Recurse -File | ForEach-Object { icacls $_.FullName }

# DB mã hóa: header phải khác "SQLite format 3\0" (in 16 byte hex)
[Convert]::ToHexString([IO.File]::ReadAllBytes("$env:ProgramData\EduFocus\edufocus.db")[0..15])
# File khóa chỉ nên có magic EDUFKEY1 + blob DPAPI; không in nội dung chi tiết
Test-Path "$env:ProgramData\EduFocus\edufocus.key"
(Get-Item "$env:ProgramData\EduFocus\edufocus.key").Length

# Không có ảnh/video camera trong dữ liệu ứng dụng
Get-ChildItem "$env:ProgramData\EduFocus","$env:LocalAppData\EduFocus" -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object Extension -in ".jpg",".jpeg",".png",".bmp",".mp4",".avi",".mkv",".webm"

# Log: tìm secret đã biết trong bản chụp đã che (dùng PIN kiểm thử, không dùng PIN thật)
# Select-String -Path "$env:LocalAppData\EduFocus\logs\*.log" -Pattern "<pin-test>","<recovery-test>"
```

Với DB/WAL/backup, bằng chứng tự động là test
`ParentAuthPersistenceTests.Database_wal_and_backup_files_never_contain_plaintext_secrets`; khi cần
kiểm tra thủ công, chỉ tìm chuỗi PIN/recovery kiểm thử đã biết và ghi kết quả, không sao chép DB ra
ngoài máy.

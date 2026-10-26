# SQLite migration recovery

`DatabaseInitializer` creates timestamped snapshots in `%ProgramData%\EduFocus\backups` before applying a migration to a non-empty database. It keeps the three newest snapshots. Migration errors are logged and thrown to the caller; the current database is never deleted or automatically replaced.

To restore a snapshot after reviewing the error log:

1. Exit EduFocus and confirm its process has stopped so SQLite has no open connections.
2. Copy the entire `%ProgramData%\EduFocus` directory to a separate safe location.
3. Move `edufocus.db` and any matching `edufocus.db-wal` / `edufocus.db-shm` sidecar files into a quarantine folder. Do not delete them until recovery is verified.
4. Copy the selected `backups\edufocus-<timestamp>.db` snapshot to `%ProgramData%\EduFocus\edufocus.db`.
5. Start EduFocus and inspect the log. Keep the quarantined files until the restored database is confirmed.

Do not point EduFocus at a `DontBeLazy` 1.x database. Databases with application tables but no EF migration history are refused; they are never converted or cleaned up automatically.

## Verify access with a standard user

After an Administrator has launched EduFocus once, inspect the ACL from an elevated PowerShell prompt with `icacls "$env:ProgramData\EduFocus"`. It should show only `BUILTIN\Administrators` and `NT AUTHORITY\SYSTEM` with Full Control. Then sign in as a separate standard Windows user and run `tools\verify-data-directory-acl.ps1`; both directory listing and file creation must be denied. Do not run this check while logged into the same Windows account used by the parent.

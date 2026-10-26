# EF Core 10 upgrade notes

**Scope:** .NET 10 / EF Core target upgrade, issue #6. No schema or migration was added to the product. The existing migrations were applied to a disposable development database to verify EF Core 10 SQLite compatibility.
**Verified against:** EF Core and SQLite provider 10.0.13; .NET SDK 10.0.102, 26 October 2026.

## Upgrade result

All application, library, test, and spike projects now target `net10.0` or `net10.0-windows`. The WPF project remains Windows-only. EF Core runtime, SQLite provider, and design packages are all pinned to 10.0.13; `dotnet-ef` is pinned to 10.0.13 in the local tool manifest. Microsoft.Extensions packages used by the solution are aligned to 10.0.13. CI and release workflows use the .NET 10 SDK on `windows-latest`.

The verified machine used .NET SDK 10.0.102 and ran the existing migrations against a disposable database. The restore/build/test check can be repeated with:

```powershell
dotnet --info
dotnet tool restore
dotnet restore EduFocus.slnx
dotnet build EduFocus.slnx --no-restore --configuration Release
dotnet test EduFocus.slnx --no-build --configuration Release --verbosity minimal
```

## Compatibility items to monitor

- The SQLite provider is published as part of the EF Core project and supports EF Core 10. EF providers generally must match the EF Core major version. See [EF Core providers](https://learn.microsoft.com/en-us/ef/core/providers/).
- EF Core 10 introduces changes documented by Microsoft. Review those before modifying LINQ or migration code; use a framework selector with EF tools if a project becomes multi-targeted. See [EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes).
- `Microsoft.Data.Sqlite` 10 changes SQLite date/time interpretation: text timestamps without an offset are treated as UTC by `GetDateTimeOffset`; timestamps with an offset read through `GetDateTime` are returned as UTC. The current `SessionHistory` entity records local `DateTime.Now` values and uses SQLite persistence. Test timestamp display and ordering on machines with different local time zones before release. There is no conversion or data migration in this issue.
- EF Core 9 and later use a database lock while applying migrations. With SQLite, an interrupted migration may leave the `__EFMigrationsLock` row behind and block later migrations. Because the app applies migrations at startup, exercise interruption/restart recovery on a disposable database before production rollout; do not automatically delete the lock from a user database without checking migration state. See [SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations).
- The project still applies existing migrations at startup. This change adds no migration and intentionally does not transform the old 1.x database. The app now uses `%LocalAppData%\EduFocus\edufocus.db`; it no longer copies a legacy `dontbelazy.db` from the executable directory. Existing 1.x data is intentionally not migrated.
- Third-party UI and native camera packages were not upgraded. The successful restore/build validates their compile-time compatibility with the .NET 10 target. A short process launch in this environment did not expose a WPF window or create the expected data directory, so the interactive UI smoke test remains unverified. Build also retains WFO0003 because the current manifests declare PerMonitorV2 DPI awareness; keep that setting until a Windows UI check validates the recommended configuration change.

## Sources

- [EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)
- [EF Core SQLite provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite)
- [SQLite provider limitations and migration locking](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- [Microsoft.EntityFrameworkCore.Sqlite 10.0.13 on NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/10.0.13)
- [dotnet-ef 10.0.13 on NuGet](https://www.nuget.org/packages/dotnet-ef/10.0.13)
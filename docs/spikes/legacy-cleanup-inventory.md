# Issue #7 — 1.x cleanup inventory

**Review date:** 26-10-2026
**Scope:** tracked source/configuration/assets. Git history and user data are preserved.

## Removed

| Component/files | Reason |
|---|---|
| `Domain/Entities`, `Domain/Enums`, `Domain/ValueObjects` and their non-attention unit tests | Task, profile, quote, and task-linked focus-session models belong to the predecessor product and do not match the EduFocus 2.0 schema. |
| `EduFocus.UseCases` project and its task/profile/session/analytics/quote/settings use cases | These implement the removed 1.x behavior; no remaining solution project references them. New use cases are deferred to their feature issues. |
| `EduFocus.SqliteDataAccess` project, old `AppDbContext`, repositories, migrations, and snapshot | These migrations create the old schema, including Gemini and quote changes. Keeping them in the active solution risks opening or mutating old databases. No new database is created by the shell. |
| `GeminiAiServices`, AI ports, `WindowsStrictEngine`, `LoggingStrictEngineDecorator`, `LocalRedirectServer` | Old task/profile-specific AI and enforcement engines are not registered or used by EduFocus 2.0. Blocking remains an isolated manual spike until its implementation issue. |
| WPF `Views`, `ViewModels`, tray/updater services, converters, and migration/auto-start startup code | These expose old task/profile management, Pomodoro, analytics, legacy update behavior, and old database migrations. The app now opens a neutral EduFocus shell without activating these features. |
| `give_up_quotes.txt`, `app.ico`, and references to that icon | Quote/guilt-trip data and the predecessor icon are removed from source and publish output. The UI and MSI use the EduFocus name without shipping a replacement logo. |
| Old product name, executable, registry key, icon and shortcut references in `installer/Product.wxs` and release artifact metadata | MSI now packages `EduFocus.exe` and has no reference to the removed icon or predecessor UI. |

## Kept

- `Domain/Attention` and attention classifier/state-machine tests: these are the current camera-analysis spike foundation and do not activate monitoring in the WPF app.
- `Ports` attention analyzer, logger, monotonic clock, and notification contracts; `Infrastructure` file logger/clock; `Infrastructure.Attention` camera adapter.
- `AttentionPipelineSpike`, `BlockingPipelineSpike`, and `WatchdogSpike`: standalone engineering spikes, not projects in `EduFocus.slnx` and not launched by the app or release workflow. `fetch-models.ps1` remains because the attention spike documentation uses it to obtain its model assets.
- BA, architecture, schema-design and historical changelog documents. References to the predecessor in historical/product-transition documentation are intentional.

## Verification boundary

The current solution contains only the foundation and attention projects. It has no task/session/profile database context, 1.x migrations, AI client, blocking engine, tray, updater, auto-start hook, or predecessor icon in build output. SQLite schema and new feature behavior remain future work. This cleanup does not delete any user database or alter Git history.
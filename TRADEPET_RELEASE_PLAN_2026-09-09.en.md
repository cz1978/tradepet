# TradePet 1.0 unified implementation and release plan

English | [简体中文](TRADEPET_RELEASE_PLAN_2026-09-09.md)

Written: 2026-09-09. Target: `1.0.0`, acceptance candidates `1.0.0-rc.N`.

Status: **Plan written; the final-release work below has not completed acceptance under this plan.** This historical specification is not a claim that the current candidate provides all future 1.0 features.

One version contract: fix confirmed data/interaction defects, complete product workflows, architecture, testing, install/upgrade/support, and accept one commit/package. Work IDs indicate dependencies, not partial delivery labeled final.

Carries forward [review specification](TRADEPET_REVIEW_UPGRADE_PLAN_2026-09-06.en.md) R01–R12/V01–V20. Retain/regress existing features; new findings follow this plan. Historical progress belongs in [TODO.md](TODO.md); this document defines specifications, gates and reasons.

## 1. Final-release scope and completion

### 1.1 Product commitments

| Item | 1.0 scope |
| --- | --- |
| Positioning | Local-first Windows MT5 read-only desktop pet/journal/review |
| Platform | Actually accepted, vendor-supported Windows 11 x64 builds, exact OS in manifest; no formal Windows 10/ARM64/macOS claim |
| MT5 | Remove WeTrade-specific paths; two broker distributions, multiple terminals, cross-account position-ID isolation; complete projection for hedging |
| Other modes | Explicit netting/exchange account/position monitoring and review limitation; full identity/projection deferred, never use hedging assumptions |
| Offline | Synced trades/journals/strategies/search/backup/local replay usable without terminal/network |
| Review | Existing six areas; protected drafts, chart annotations, global search, import/report completeness and metric explanations |
| Migration | Current database, valid original schema 1–8 fixtures and old full backups; no silent empty replacement |
| External data | Generic mapped/previewed CSV, separately sourced, no live-session/alert/unproven-process contamination |
| Delivery | Signed installer, offline runtime, versions/dependencies, update checks, recovery/diagnostics/manual |
| Privacy | Local default, explicit redacted sharing, encrypted workspace/backups, no automatic diagnostic upload |

Excludes cloud/mobile/coach collaboration/subscriptions/trading automation/independent commercial data. Scope choices do not waive safety/reliability. Full netting reviews require separate real-execution identity evidence, not last-minute acceptance additions.

### 1.2 Completion

WP01–WP28 have commits/evidence; G01–G36 plus old V01–V20 pass with current evidence, not old reports alone. No unresolved S0/S1 or workflow-affecting S2. E01–E05 fulfilled; absent certificates/distribution/desktop acceptance means candidate/awaiting release, not a released final. Test final package for install/upgrade/restore/signature/performance; changed business/runtime/native/package contents repeat affected gates.

S0: account contamination, irreversible loss, sensitive disclosure or trading writes. S1: startup/save/restore/upgrade failure or important wrong statistics. S2: local functionality, substantial performance/accessibility. S3: nonblocking text/appearance. These categories do not imply live reproduction.

## 2. Existing foundation and required corrections

Already present: Core/Application/Infrastructure/WPF, composite account identity, optimistic revisions, supervised restarts, bounded queues, backup hashes, paged/saved filters, small-sample hints and read-only replay. Reuse them and still test boundaries.

Historical TODO recorded 201 .NET/16 Python passes, not final acceptance. The second review reran one export/two backup tests successfully, but lacked actual behavior IDs and mid-switch failure cases.

| Defect | Evidence/impact at plan time | Work |
| --- | --- | --- |
| F01 Switch retains edits | [Switch](src/TradePet.App/Runtime/TradePetRuntime.cs:810) clears lists only; [save](src/TradePet.App/Runtime/TradePetRuntime.cs:2537) uses current account, risking old drafts under new account | WP02/03 |
| F02 Receipt clears newer edits | [MarkReviewSaved](src/TradePet.App/ViewModels/Review/ReviewWorkspaceViewModel.cs:867) cancels save/clears dirty unconditionally despite typing during save | WP04 |
| F03 Day/period drafts overwritten, exit inconsistent | [Form refill](src/TradePet.App/ViewModels/Review/ReviewWorkspaceViewModel.cs:680), [journal switch](src/TradePet.App/Runtime/TradePetRuntime.cs:2934), [exit](src/TradePet.App/Runtime/TradePetRuntime.cs:580) | WP04/11 |
| F04 Restore rollback gap | [Swap](src/TradePet.Infrastructure/Persistence/ReviewArtifactStore.cs:369) moves old data before marking switch, finally deletes old copy; mid-failure may not restore | WP07 |
| F05 Public export leaks account | [Behavior IDs](src/TradePet.Core/Trading/BehaviorAnalyticsCalculator.cs:361) contain accounts and are [exported raw](src/TradePet.Application/Review/ReviewServices.cs:1208) | WP06 |
| F06 Export scope mismatch | [Export](src/TradePet.App/Runtime/TradePetRuntime.cs:3645) iterates page only; [opportunity attachments](src/TradePet.Infrastructure/Persistence/AppDatabase.ReviewWorkspace.cs:91) query whole account | WP05/06 |
| F07 Inconsistent reads | [Workspace](src/TradePet.Infrastructure/Persistence/AppDatabase.ReviewWorkspace.cs:25) multiple connections/queries can cache mixed versions | WP05 |
| F08 Details scan whole account | [Detail](src/TradePet.Infrastructure/Persistence/AppDatabase.ReviewWorkspace.cs:113) loads/filter whole account, repeated per export trade | WP05/24 |
| F09 Rebuild loses manual data | [Recovery](src/TradePet.Infrastructure/Persistence/AppDatabaseRecovery.cs:18) retains/rebuilds using MT5, which cannot recreate notes/image links/strategies | WP07/08/10 |
| F10 Incomplete delivery | Git then lacked commits; [release](scripts/build-release.ps1) only published; [bridge](scripts/build-bridge.ps1) checked existence, risking stale EX5; [paths](src/TradePet.App/Runtime/RuntimePaths.cs) depended on local Python | WP01/20–23 |
| F11 Frequency trigger/text mismatch | [Trigger](src/TradePet.Core/Trading/BehaviorAnalyticsCalculator.cs:337) absolute count can trigger below personal baseline; [baseline](src/TradePet.Core/Trading/BehaviorAnalyticsCalculator.cs:440) uses top 20 historical daily peaks; [bubble](src/TradePet.App/Runtime/TradePetRuntime.cs:1539) always says usually only, producing unexplained 7 versus usual 10 | WP17 |

Identified paths do not prove no other defects. WP26 independently checks remaining boundaries within this version.

## 3. File scope and target architecture

Proposed directories may follow existing naming with minor changes; responsibilities/acceptance cannot shrink.

| Files/directories | Role / change |
| --- | --- |
| Core/Domain | Editing identity, export scope, recovery/import/provenance/annotation/support contracts |
| Core/Session, Trading, Review | Generations, trade/time correctness, metric/replay boundaries |
| Application/Review | Query/edit/export/replay/goals; explicit account in every command |
| New Application/Editing, Recovery, Import | Draft commits, restore machine, CSV preview/idempotence |
| Infrastructure/Persistence | Connection factory, snapshot reads, transactions/migrations/backups/fault injection/encryption |
| Infrastructure/Mt5, python, mt5 | Capabilities, processes, read-only integration, traceable builds |
| App/Runtime/TradePetRuntime.cs | Extract account/ingestion/rules/reviews/recovery/lifecycle; no concrete SQL/packaging |
| App.xaml.cs, Runtime/AppLog.cs | Error containment, coordinated exit, redacted logs/diagnostics |
| ViewModels/Review, Views, Controls | Editors/save state/scope preview/onboarding/annotations/accessibility |
| New Infrastructure/Diagnostics, Security | Health/diagnostic package/key wrapping/encrypted attachments/cleanup |
| tests, tools/TradePet.ReviewBenchmark | Concurrency/file faults/process integration/export/performance |
| New App.Tests, TradePet.Ui.Tests | Controlled scheduling/native interactive Windows tests |
| scripts, new packaging/.github/workflows | Locked SDK/dependencies, offline Python, signed MSIX/update index/gates |
| docs, README, CHANGELOG | Manual/metrics/support/recovery/licenses/evidence |

Keep `App → Application → Core`, `Infrastructure → Application/Core`, with App composition. Core never depends on WPF/SQLite/MT5. Splitting files into partials while sharing all mutable state is not architectural completion.

## 4. Invariants and API contracts

1. Read-only trading: whitelist excludes writes; mocks/recordings/isolated data instead of real acceptance orders.
2. Identity: `AccountKey + PositionId + SessionGeneration + EditorInstanceId`; other entities also account+ID. Old reads/receipts never alter new editors.
3. Draft truth: immutable snapshot, ExpectedRevision/EditSequence; clear dirty only when receipt covers current sequence. Conflict/cancel/failure is not success.
4. Facts versus edits: immutable MT5 executions; separate import/plan/observation/retrospective recording times. Rule corrections trigger rereview retaining conclusions.
5. Consistent reads: trades/deals/docs/metrics/version from one snapshot; export pins account/filter/version/entity/attachment lists.
6. Atomic writes: changes/audit/version in one transaction; staged verified assets before linking; collect failed staging, not referenced assets.
7. Exclusive maintenance: stop writes and drain all database users, including queries/export/replay/background work, before restore/migrate/encrypt/delete; one coordinator prevents lock inversion.
8. Resumable restore: persist state before switch; restart identifies old/new sets and validation. Do not delete last valid old set before verifying new.
9. No invented values: unknown risk/FX/sample/history/unsupported modes stay unknown, never zero/guessed conversion/strategy advantage.
10. Allowlisted redaction: public DTOs, package-local temporary IDs; removing only AccountKey is insufficient.
11. Awaitable lifecycle: Task commands and observed async entries; explicit stop/cancel/timeout/crash, no swallowed-success claims.
12. Verifiable versions: commit/app/bridge/Python/native/schema/protocol/export; old binaries reject newer schemas.

## 5. Work packages

Original workflow: one active TODO, inspect callers/types/tests, failing reproduction first, contracts → core → adapters → UI → tests/docs, targeted acceptance before next work. Preserve behavior during refactors without simultaneous accounting changes. G IDs map subscenarios; complete cross-package gates only at WP28, neither prematurely check a whole gate nor block a package on future features.

### WP01 · Traceable baseline

Scope `.gitignore`, versions, root/release manifests. Exclude real databases/screenshots/account logs/certificates/keys/machine paths; stage reviewed files, initial commit/tag, record missing Git identity as external condition rather than inventing it. Hash runnable package/schema/dependencies/sanitized fixtures and consistent data backup before migrations. Deliver rollback source baseline/defect ledger/version policy. Remote publication is not an automatic local-baseline action. No dependency; G01.

### WP02 · Editing/maintenance contracts and injectable tests

Core/Application/runtime composition/new App tests: full editing identity, sequence, results, session cancellation/maintenance leases, replaceable clock/scheduler/repository/files. Tests suspend any await/control account/receipt order without MT5. Update all callers with contracts. Depends WP01; G02/03.

### WP03 · Cross-account/stale responses

Retain old-identity drafts, cancel timers/requests, increment generation, clear account editors/selected IDs. Save from editor account, never current-account substitution. Validate target/source, reject orphan documents. Old committed writes may remain in old account but receipts cannot touch new UI. Test A→B→A ordering, repeated clicks, unsupported modes and imported/live accounts. Depends WP02; G03/04.

### WP04 · Shared save/navigation protection

Trade/day/period/rating/opportunity/goal/annotation editors share dirty/save/error state and per-entity drafts; refresh never overwrites unsubmitted input.

Typing during save confirms only submitted sequence, retains newer dirty/queue. Conflicts offer reload/local copy/manual merge, never silent overwrite. Save before normal navigation; failure stays or explicitly retains draft. Exit stops edits/drains queue, with retained/exported draft and exit choices on timeout/read-only. After typing stops, submission within one second, clear result within two seconds on normal disk. Crash guarantees only persisted drafts, not every keystroke surviving power loss. Depends WP03; G05/06.

### WP05 · Snapshot reads and query cost

One connection/read transaction for related data; isolated long-export snapshots avoid holding production WAL. Exact account/position detail/deal queries and batch export loads, no repeated whole-account scans; indexes/actual query plans. Separate PagedTrades/full filtered/aggregates; opportunity attachments scoped by selected IDs. Full version/filter cache keys; old contents never cached under new versions. Depends WP02–04; G07/08/09.

### WP06 · Public export, scope and integrity

All filtered/current selected scopes with date/trade/opportunity/asset preview, default all filtered, never page silently. Remap internal behavior/plan/trade IDs, remove account/server/path/nested references via allowlist. CSV formula defense includes whitespace/tab/newline prefixes; escape all HTML user text. Raw assets excluded by default because text/images/names may disclose accounts; explicit selection/redaction/preview without claiming selected images automatically anonymous. CSV/JSON/HTML/MD share a frozen set/count/P&L/revision/manifest. Missing/changed assets fail or explicitly produce a marked incomplete package, never silently skip. Streaming/cancellation/temp cleanup/string IDs; public sharing versus local full backup distinct. Depends WP05; G09/10/11.

### WP07 · Restore machine and file-switch atomicity

Unified maintenance stops/drains DB users and handles WAL/SHM; ClearAllPools does not stop active connections. Validate package → current consistent backup → persist operation → stage → stepwise switch → initialize/verify → commit → delayed old cleanup. Record every recoverable step and old DB/assets moves separately, not one boolean after both moves. On failure/cancel restore/verify first and report actual result. Restart resumes/rolls back. Temporary-workspace faults: full disk, denied access, occupied file, second move failure, validation failure. Depends WP02/04–06; G12/13.

### WP08 · Migration and corruption salvage

Min/max readable/writable schema, checksums, exclusive locks, pre-migration backup; no partially migrated operation. Distinguish wrong key/unsupported format/permissions/disk failure/corruption. Salvage valid tables/records from copies, reporting recovered counts, failed tables/assets and affected entities. Prefer verified recent backup/readable manual content; create new workspace only after explicit notice of unrecoverable manual material. Retain original/side files for traceability.

Support schema 1–8/current fixtures, repeat starts and interruption at every migration. Binary rollback is not data rollback: retain new records and offer restored copies/forward repair when incompatible. Old distributed portable binaries cannot gain retroactive schema guards; migrate final release into a registered separate workspace, preserving the old-path database and explaining ownership/backups/return migration. Depends WP07; G14/15.

### WP09 · Workspace encryption/key recovery

Maintained SQLCipher-compatible native build through Microsoft.Data.Sqlite.Core/connection factory. Default pinned-source/build verification/updating, not discontinued free bundles; supported purchased binaries are an alternative, not automatic purchase. Random data keys, Windows DPAPI local wrapping plus portable recovery key/passphrase wrapping. Maintained authenticated attachment encryption covers filenames/temp files. Check DB/WAL/temp/assets for plaintext; wrong key enters unlock/recovery, never corrupt→empty rebuild. Convert plaintext via verified backup/staging/switch; identify legacy plaintext backups and do not promise physical SSD/system-backup erasure. Maintained authenticated backup containers, pinned format/KDF/tamper checks, no custom cryptography; local DPAPI backup is not portable recovery. Depends WP08; G16/17.

### WP10 · Automated backup/retention/drills

Manual, first-valid-use daily and before upgrade/restore/bulk import/delete; runtime checks due dates, no new resident service. Retain seven daily/four weekly/three monthly backups; rotate only after verified new backups and keep last valid copy. Configurable cross-disk destination; same-disk backup is not disaster recovery. Show success time/failure/size/encryption/path and actionable disk-space failures. Every candidate restores in isolation and compares manual docs/strategies/links/hashes, with recovery point/time expectations. Depends WP09; G17/18.

### WP11 · Startup/exit/crash/diagnostics

Earliest startup logs/global exception boundary distinguishing recoverable and fatal errors, not blanket Handled-and-continue writes. Safe mode disables ingestion/complex views and exports diagnostics. Exit waits for WP04/supervised tasks and covers logout/shutdown/repeated launch. Diagnostics: versions/event IDs/stacks/anonymized sessions/health/schema/backups, no default database/body/images/account; user previews/provides manually. Minimal startup failure record with fallback-path guidance when logs fail; levels/limits/rotation/repetition control. Depends WP04/10; G19/20.

### WP12 · Collectors/protocol/live recovery

Retain supervision/bounded queues; add backoff/repeated-failure health/heartbeat/frame limits/rejection/version capabilities. Account switches, terminal closure/upgrade, killed Python, bridge reattachment, sleep/wake and multiple terminals do not duplicate facts/replay alerts. History startup is timed and finally-cleaned; cancellation leaves no child process and replay changes no live state. Distinguish connected/stale/backfilling/fault/unsupported; missing chunks remain incomplete without advancing watermarks.

Depends WP02/11; G21/22.

### WP13 · Accounting/time/risk acceptance

Sanitized gold fixtures: partial/add/overnight/duplicate/corrected deals, late fees, cash/credit/type changes and same IDs across accounts. Reconcile execution cash, complete net P/L and equity drawdown separately. Tolerance follows source/currency precision, not universal 0.01 for every currency. Segmented time rules cover DST, server/PC clock changes and midnight, never current offset applied to all history. Risk uses then-provable stop/FX with slippage/fee/sample coverage explicit. Totals stable after repeats/upgrades/reprojection. Depends WP05/12; G23/24.

### WP14 · Actual replay and annotations

Requests/views carry account/trade/request/generation; stale markets never overwrite selection. No future information, particularly final OHLC before close; bars-only mode is completed-bar/event steps, not fake ticks. Execution/stop overlays plus price/time-anchored text/lines, created/modified/retrospective times, edit/delete/undo. Correct gaps/weekends/missing terminal history/same-ms ticks/late fees/precision degradation; source/version and on-demand retry. Depends WP04/05/12/13; G25/26.

### WP15 · Generic CSV/idempotence/corrections

Wizard for source/separate account, encoding/delimiter/timezone/currency/mapping/units/types; saved templates. Separate deal-level versus complete summaries; summaries cannot fabricate process/MAE/MFE/initial R. Source namespaces never merge live accounts based on a login number. Preview rows/errors/dates/fees/P&L; missing currency/time semantics require completion/rejection. Source account+record ID/fingerprint idempotence; changed same-ID records preview corrections. Batch/source digest retained; undo only batch data, preserving later manual material and reporting orphan links. Depends WP04/05/10/13; G27.

### WP16 · Onboarding/offline/review workflows

Verify directory/encryption recovery/terminal/mode/bridge/chart/history, retry failures and offline existing workspace. Clearly separate demo workspace with one-click exit, no real data/alerts. Visible account/workspace; load local accounts without requiring `_account` live snapshot to edit. Account-wide trade/journal/opportunity/strategy search with highlights/scope/navigation, reusing saved filters. Consistent empty/loading/access denied/save failure/missing/rereview and actionable retry/details. Editable day/week/month templates with facts, quality, actions/evidence in preview. Depends WP03/04/11/14/15; G06/28.

### WP17 · Metric dictionary and trustworthy statistics

Each important metric exposes formula/denominator/currency/date/scope/missingness; reuse and unify small-sample rules. Frequency baseline uses explicit historic dates, distinguishes relative/absolute triggers and states the crossed threshold, never “usually only” when actual is below baseline.

Ratios include uncertainty intervals; means/comparisons show count/skew effects with pure/numeric-reference validation, no unjustified significance. Correlated same-account trades are not assumed independent; unverified correlation/multiple comparisons yield descriptive comparisons, not misleading advantage scores. Deduplicated behavior P/L, frozen baselines/goals, Unknown not Pass/no opportunity not improvement. Shared time/P&L/CSV/UI dictionary; recheck V05–V13. Depends WP13/16; G23/29.

### WP18 · Data/attachment security/audit

Validate extension/actual format/pixels/file-count-size/hash; prevent traversal/reparse escape/default file execution. Transactional reference counts; preview orphan cleanup, retain active import/restore/export assets and only managed paths. Account export/delete, batch undo, logs/cache/storage views with explicit account/count/last backup. Delete derivatives/search/asset links/drafts/cache; external backups separately managed, no physical-erasure promise. Readable local audit for edit/batch/import/correct/restore/delete, no purposeless sensitive duplication; applicable undo/revisions. Depends WP09/10/15/16; G11/17/30.

### WP19 · Architectural/testability completion

Move tested Runtime ownership into account, ingestion, rules, review, notification and lifecycle services via composition, one state owner each. Separate query/edit/cache/migration/backup repositories, only necessary shared connection/transaction helpers. Split ReviewServices/large VMs by use case/editor, no WPF in services. Observable state/commands replace mutable delegates/implicit current accounts. Dependency/cancel/disposal checks, not arbitrary class counts/line limits or unnecessary buses/microservices. Depends WP03–18; G02/20/31.

### WP20 · Supported runtime, pinned dependencies and bridge artifacts

Plan .NET 10 LTS across projects/tests/benchmark with matching WPF TFM, global.json/locks and reverified security patches. The [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) cited by the plan lists .NET 8 ending 2026-11-10 and .NET 10 ending 2028-11-14. Audit SkiaSharp/SQLite/encryption natives/direct/transitive compatibility/vulnerabilities; TFM changes alone are insufficient. Isolated Python 3.13 x64 with tested MT5/dependencies, pinned wheel hashes/interpreter/source; test before major upgrades. [CPython 3.13 wheel availability](https://pypi.org/project/metatrader5/) is not worker acceptance. Assemble on build host, no user pip/PATH/fixed Python path. [Embedded Python](https://docs.python.org/3/using/windows.html#the-embeddable-package) lacks pip, so old venv scripts cannot simply carry over. Compile bridge in isolation with current logs/source/hash/MetaEditor build and discovered configurable paths, not stale EX5/WeTrade hardcoding.

If tools cannot guarantee byte-identical artifacts, document nondeterminism/pre-sign hashes/provenance; Deterministic=true alone is not whole-package reproducibility. Depends WP19; G01/21/31/32.

### WP21 · Installation/repair/uninstall

Signed full-trust MSIX+App Installer primary, equivalent-runtime portable offline/support fallback. Proposed packaging/msix pins identity/publisher across versions. Verify tray/startup/named pipes/Python/MT5 access/bridge under package identity, explicit dependencies/permissions, no developer environment. Read-only package directory/unified StorageLocator, MSIX virtualization and portable migration without an apparent empty second database. Standard-user install, visible Start menu/icon/version, second launch activates existing instance. Repair preserves data; uninstall removes binaries/startup with clear workspace retention/export. Clean-machine tests without .NET/Python/source, Chinese/spaced usernames, other drives/offline/missing or policy-blocked App Installer yield explicit results. Depends WP09/11/20; G28/32/33.

### WP22 · Secure updates/compatible rollback

HTTPS with pinned identity/signatures, stable/candidate channels separated and stable default. Index declares minimum OS/data/protocol compatibility/notes. Checks show notes/defer; no forced termination during monitoring, default startup check/apply after exit, no unconditional forced downgrade. Interrupted/wrong-signature/version downloads never overwrite. Drain drafts/back up/mark state; failed first migration enters recovery. App Installer binary update versus WP07/08 data restore are separate transactions. Keep support packages; incompatible old binaries never write new schema, prefer forward fixes. Drill portable→1.0, RC upgrades, bad/offline/signature change/first-start migration failures without losing post-upgrade records. Depends WP08/10/21; G14/33/34.

### WP23 · CI/supply chain/release gates

Windows clean checkout/locked restore/.NET+Python tests/WPF/audit/format/static/read-only/content/evidence pipeline. Shared bin/obj builds serialize or isolate to prevent false locks. Native UI requires interactive Windows hosts, not headless visual claims. Controlled bridge tools/protected signing credentials; ordinary PRs cannot read keys and keys never enter repo/ZIP. SBOM/licenses/pre-post-sign hashes/commit/provenance, direct/transitive/final Python/native scan. Unevaluated exploitable high/critical issues block release, as do failing tests, unsigned/incomplete runtime/license/hash or real-account residue. Depends WP01/20–22; G01/31/35.

### WP24 · Performance/capacity/soak

Reference ≥4 cores/16 GB RAM/SSD/Windows 11 x64, exact hardware/OS/build/dataset hashes. Thresholds are acceptance targets, not existing guarantees. Dataset: 100,000 trades/300,000 executions/multiple accounts/rules/notes/50 GB attachments. Routine benchmarks do not copy assets every time; separate large-file/small-file capacity tests.

P95 interactive cold start ≤5 s, cold query ≤3 s, warm filter ≤200 ms, detail ≤300 ms, ≥30 measurements/category with defined caches. 4-Hz processing P95 ≤250 ms, added review/export P95 ≤50 ms, cancellation feedback ≤500 ms/eventual stop. Simulated 72-hour ingest/edit/switch/disconnect plus each of two terminals ≥24-hour read-only observation; distinguish external/app failures, recovery/counts. Stable 100k workload private memory ≤1 GB, extra export ≤300 MB, no linear memory/thread/handle growth; record p50/95/99, CPU/queues/DB-WAL/decode peaks. Streaming 50 GB backup/restore each ≤30 minutes; backup faults neither freeze UI nor fill disk indefinitely. Diagnose/fix before changing thresholds with documentation. Depends WP19–23; G08/18/22/36.

### WP25 · Native WPF/accessibility/compatibility

Separate desktop automation: setup → connected/offline → filters → edit → journal → ratings → replay/annotations → import → export → backup → restart. Synthetic workspace, never real trading buttons. 100/150/200%, mixed-screen DPI, minimum window, light/dark/high contrast, Chinese/English regional formats. Stable AutomationId/names/tab order, visible focus/dialog return, Narrator save/errors; color not sole result/risk signal. Mouse/keyboard workflows, tray recovery after pet passthrough/hide/lock; startup/exit/update retain placement/no ghost tray. Include pending V20, archive screenshots/recordings/environment; unavailable native control is unaccepted, not XAML compilation proof. Depends WP16–24; G06/25–28/32/33/36.

### WP26 · Independent review/full regression

Review F01–F10 fixes beyond test passes: call chains/state/receipt application/fault coverage. Recheck R01–R12/V01–V20, especially plan links/dedup/frozen goals/time/read-only/future data/deleted-asset restore. Isolated concurrent/random cancel/bad frames/full disk/permissions/killed process; failures must match actual state. Number/severity/test/fix new defects until no workflow S0/S1/S2; unimplemented is not pass because untested. Depends WP01–25; G01–34/36, old V01–20 and G35 engineering portion; WP27 supplies legal/docs, WP28 final gates.

### WP27 · Documentation/rights/support

Manual covers setup/offline/metrics/backups/moving computer/update failure/logs/delete/limits and actual UI. Changelog/product license/third-party notices/privacy/security contact/dependency-update policy; owner confirms attribution/assets, no automatic license selection. Verify redistribution rights for pet images/audio/fonts/Python/MetaTrader5/SQLCipher/OpenSSL/SkiaSharp/tools, not mere download availability. Working support/template, user-initiated redacted diagnostics only. Separate security/normal fixes; no unowned response-time promises. Depends WP20/25/26; G35/E01–05.

### WP28 · Final candidate and handoff

Freeze one commit, full acceptance, source tag/signed installer/portable/update index/SBOM/checksums/notes/report. Final clean install/update/restore smoke uses the same signed artifacts, never post-test manual assembly from unverified directories. Missing external conditions are precise with candidate outputs; self-signed test certificates cannot claim production signatures, no buying certificates/licenses for the user. After all gates, hand off publishable package/manifest. Public upload/stable-source switch is the final publishing action with verified destination/existing authorization; writing this plan performs no upload. Depends WP26/27/E01–05; all gates/manifest.

## 6. Data and recovery specifics

### 6.1 Versions/audit

Separate schema/business source/rules/document revision/protocol/file formats. Suggested source_namespace/original ID/recorded/event time/migration-import batch/operation ID, concrete columns defined by WP02/08, no manual fields overwriting raw MT5. DB checks account/entity references, nonnegative revisions/enums; report/isolate dirty legacy data, never delete unrecognized manual docs to enforce constraints. Recompute repeatable derivatives while retaining original/manual facts; initial migration/recovery never replays old alerts.

### 6.2 Recovery guarantees for final release

| Fault | Required behavior |
| --- | --- |
| Save/full disk | Dirty/error/draft export, never Saved |
| Normal/update exit | Await persistence, failure retain/cancel exit |
| Crash | Recover persisted drafts/operation state with results, no claims for unpersisted input |
| Corrupt DB | Original retained, copy salvage/backup restore/missing report; no hidden manual loss |
| Interrupted restore | Resume/rollback from journal; verify before cleanup |
| Wrong/lost key | Unlock/recovery, not corruption; no key means explicit inability to decrypt |
| Incompatible schema after update | Block old writes, retain new material, forward fix/copy restore |
| Lost computer/disk | Valid backup on separate media plus credentials only; local backup cannot guarantee recovery |

## 7. Unified acceptance matrix

Record requirement/WP, dataset hash, commit/package hashes, environment, steps/expected/actual/logs/images/date. Automation proves only covered scenarios.

| Gate | Required result |
| --- | --- |
| G01 | Clean checkout builds, locked/tagged provenance, no real accounts/keys in source/package |
| G02 | Core/Application independent of WPF/concrete storage; injectable clocks/scheduling/repositories/files |
| G03 | Same position ID in A/B, switch while editing; A draft stays A, clean B, no stale receipt |
| G04 | A→B→A late details/empty/netting/offline switches show no wrong identity |
| G05 | Paused save/new input/old receipt/next save persists latest text; conflict/failure stays dirty |
| G06 | All editors preserve valid drafts across switch/refresh/exit/restart; keyboard/screen-reader states |
| G07 | Concurrent corrected deals/doc saves yield consistent version/content; correct cache invalidation |
| G08 | Exact detail queries at 100k; no per-trade full-account export scan; WP24 plans/latency |
| G09 | With at least 251 trades and a 100-row page, full export contains exactly 251 trades, selected export contains exactly the selected set, and totals agree across formats. |
| G10 | Test account/server identifiers are absent from public paths, exports, packages and diagnostics where privacy rules require removal. |
| G11 | Out-of-scope opportunity assets are not exported. Missing/tampered assets, CSV formulas, HTML, path traversal and oversized inputs are handled safely. |
| G12 | Inject failures before and after each database/asset move; verify old hashes and readability. Cleanup never deletes the last valid set. |
| G13 | Kill the process at every replacement stage and recover or roll back on restart. Queries, replay and writes never observe a partially switched workspace. |
| G14 | Migrate schema 1–8 to the final schema; repeated startup and interrupted migrations recover. Older software refuses writes to newer schemas. |
| G15 | Distinguish corrupt pages, invalid WAL, readable tables, invalid JSON and full disks. Manual salvage counts are auditable. |
| G16 | Encrypted database, WAL, assets and temporary files contain no sensitive plaintext. A wrong key never rebuilds the database; interrupted key rotation recovers. |
| G17 | Restore encrypted backups across Windows users/machines; reject wrong passwords and tampering; import older backups. |
| G18 | Automatic backup, retention, capacity limits and unreachable destinations have visible outcomes; isolated restore meets WP24 capacity requirements. |
| G19 | Startup, WPF, background-task and logging failures are diagnosable. Default diagnostics exclude notes, accounts and images. |
| G20 | Exit completes within a bound without unobserved tasks or leftover processes/handles; restart recognizes an abnormal exit. |
| G21 | Protocol mismatch, oversized/invalid frames and missing or terminated Python produce accurate health states and cleanup. |
| G22 | Disconnect, sleep, account switching, missing chunks and retries cause no lost/duplicated facts or replayed alerts; stress tests remain bounded. |
| G23 | Golden datasets cover trades, cash, equity, fees, cash flows, currencies and unknown risk; repeated processing leaves the ledger stable. |
| G24 | DST, server clocks, PC clocks and cross-day partial closes use correct dates; the current offset is not applied to all history. |
| G25 | Replay never exposes future bars/evidence; cancellation and account switching are isolated; missing bars reduce confidence. |
| G26 | Annotation price/time anchors survive zoom and restart; annotations are hidden before their creation time in replay. |
| G27 | CSV encoding, time zones, currencies, invalid rows, corrections, repeat import and undo have explicit, auditable behavior. |
| G28 | Stored data remains editable offline without MT5; demo data is isolated; onboarding can be completed. |
| G29 | Metric definitions, denominators and intervals are correct; overlapping samples and correlations do not imply unsupported causality/significance. |
| G30 | Account deletion covers links, indexes, drafts and caches, preserves assets referenced elsewhere, and provides preview/audit. |
| G31 | Full .NET/Python tests, builds, static checks and native compatibility pass; a failed current bridge compile cannot be replaced with an old binary. |
| G32 | Validate on two clean supported Windows machines without development dependencies, including Chinese paths, standard users, offline use and signatures. |
| G33 | Verify portable migration, repair, uninstall/reinstall and actual upgrade/restore; package identity must not create a second database. |
| G34 | Invalid signatures, offline/interrupted updates and first-migration failures preserve the workspace and support compatible fallback with newly written records. |
| G35 | SBOM, licenses, support/privacy information, release notes and every hash are complete; signing credentials are protected. |
| G36 | Supply WP24 performance/capacity/72-hour-run evidence and WP25 DPI, keyboard and Narrator evidence from native Windows. |

## 8. Release toolchain and external conditions

### 8.1 Toolchain baseline

- .NET 10 LTS, self-contained `win-x64`, an isolated Python runtime and maintained SQLite/encryption native libraries. Lock the actual patch versions at acceptance.
- MSIX as the primary distribution, App Installer update checks and a complete portable fallback. Verify MT5 integration, paths and standard-user permissions under the actual package identity.
- Native Windows test machines perform WPF, MT5, installation and upgrade validation. Without native window tools, do not claim those checks are complete.
- Use targeted tests during ordinary development and full regression, capacity and long-running checks for release candidates. Monitor dependency lifecycles and maintain security patches after release.

Technical basis: externally distributed MSIX packages require signatures trusted by the target machine; a test self-signed certificate cannot substitute for production trust. See [Microsoft distribution and signing guidance](https://learn.microsoft.com/en-us/windows/msix/app-installer/installing-windows10-apps-web). Update policies can configure startup checks, prompts and enforcement; system package updates do not constitute database rollback. See [App Installer update settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings).

Database encryption requires a dedicated SQLite implementation; Microsoft's example bundle is not a supported-product commitment. See [Microsoft SQLite encryption guidance](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/encryption). SQLitePCLRaw's maintainer has deprecated the older free encryption bundle, so WP09 requires a maintainable build/update path instead of copying an old tutorial. See [maintainer guidance](https://github.com/ericsink/SQLitePCL.raw/wiki/SQLite-encryption-options-for-use-with-SQLitePCLRaw).

### 8.2 Conditions that must be secured, never invented

| Condition | Required details | Status when unavailable |
| --- | --- | --- |
| E01 Publisher and asset authorization | Product name/publisher identity, product license choice, and pet/third-party distribution credentials and licenses. | Candidate development can proceed; do not publicly distribute assets or binaries with unresolved rights. |
| E02 Production signing | User-controlled valid signing certificate or trusted signing service, timestamping and key-use procedure. | Produce unsigned artifacts and scripts; label self-signed packages for testing only. |
| E03 Distribution address | User-controlled HTTPS download/update address, publishing account, stable channel and maintained support contact. | Local candidates can be accepted; the public update chain remains unverified. |
| E04 Real acceptance environment | Two clean supported Windows machines, interactive desktops and read-only observation environments for two brokers' terminals. | Associated G gates remain incomplete; simulated results cannot replace all real-machine evidence. |
| E05 Repository and build identity | Actual Git author, version repository/CI target, legally available MetaEditor build machine and dependency maintainer. | Local manifests/scripts can be prepared; never invent missing identities. Controlled release builds remain pending. |

Register these conditions early and continue independent engineering work without repeatedly seeking confirmation for ordinary fixes. Address the relevant condition when a purchase, authorization material or public publishing action is actually needed.

## 9. Final deliverables

The suggested candidate release directory is `artifacts/release/1.0.0/`, containing at least:

| File/evidence | Contents |
| --- | --- |
| `TradePet-1.0.0-win-x64.msix` | Signed primary installer, complete runtimes and verified Bridge. |
| `TradePet.appinstaller` | Production identity and HTTPS update address consistent with the release channel. |
| `TradePet-1.0.0-win-x64-portable.zip` | Portable fallback from the same commit/runtimes, with explicit data paths and update instructions. |
| `checksums.sha256`, `release-manifest.json` | Package hashes, source commit, toolchain, protocol/schema/format versions and supported platforms. |
| `sbom.spdx.json`, `THIRD-PARTY-NOTICES.txt` | Final dependencies and distribution licenses, including Python/wheels and native libraries. |
| `RELEASE_NOTES.md`, user manual | Fixes, features, compatibility, upgrade/recovery procedures and support channels. |
| `acceptance-report.md` | Mapping of WP, F, G and historical V identifiers, with paths/results for native-machine, regression, long-run and upgrade evidence. |
| `performance-report.json`, diagnostic examples | Reference machine, raw measurements, percentiles and redacted examples. |
| Signature verification and build logs | Verification of these final artifacts; historical logs from an older release directory cannot substitute. |

The final report distinguishes implementation complete, candidate acceptance passed, production signing complete and official publication complete. None substitutes for another.

## 10. Execution and synchronization rules

- Next is WP01: establish a traceable baseline. Follow dependencies, prioritizing account isolation, draft protection, read snapshots, export and recovery.
- Deliver all work in this plan as one formal version. Do not reclassify remaining gates as future improvements after fixing only a few issues.
- For each completed TODO item, record changed files, reasons, targeted validation, remaining risks and the next item. Leave unfinished items unchecked.
- Specification changes update this document and associated G gates, explaining their reasons and effects on release commitments. Budget/schedule pressure does not justify lowering data-correctness or privacy gates.
- This plan does not promise defect-free software. It promises traceable support scope, known risks, executable acceptance criteria and failure-recovery capabilities.

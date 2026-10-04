# Changelog

English | [简体中文](CHANGELOG.md)

## 1.0.0-rc.9 — 2026-10-04

- Simplified Chinese / English UI preference, saved across restart. Pages, pet cards, tray menus, dynamic messages, field descriptions and new reports use the selected language. Raw trade data, handwritten reviews and existing reports retain their original content.
- Chinese/English counterparts for all repository Markdown documentation; portable package includes bilingual overview, installation and changelog.
- Saving a quick review immediately counts towards completion without a second confirmation in the full form. Reopening retains saved content; revision conflicts do not overwrite newer records.
- Repairs older quick reviews saved as drafts, using the old saver's explicit format and retaining content/timestamps. Ordinary manual drafts remain unchanged.
- After-loss entries use the actual losing streak before each entry and reset on a win or breakeven. Below a valid threshold, the count is zero; reaching it once no longer includes all later entries that day.
- Save/query refreshes current-date facts/completion. Failed history reads clear stale statistics/export previews, retain unsaved edits and show failure.
- Overview rebuilt with trade drill-down from return, drawdown and account-equity curves; monthly performance, distributions and multidimensional attribution. Missing samples/risk stay unknown with explicit accounting.
- Aligns daily bars with date ticks, shows the date for a single day and avoids duplicate ticks over short ranges.
- Navigable monthly P/L calendar with server-date trades/journals.
- Classification analysis, structured strategy rules/validation and opportunity links to saved strategy versions.
- Field descriptions, empty states and interactions; `TradePet.exe --review` opens reviews directly.
- Updates Windows x64 portable package and stable local launch directory; retains schema 11 and existing protocols.

Validation: 397 .NET and 23 Python collector tests passed, covering quick-review save/reload, legacy repair, completion refresh, streak reset, failure recovery, chart drill-down, distributions, calendar, rule validation, strategy links, language preference persistence and Chinese/English rendering. Seven console pages' button bindings and placeholder integrity across 2,486 English resources were checked.

## 1.0.0-rc.8 — 2026-10-04

- Quick review becomes a pet-side card rather than a centered/focus-stealing window. Saving goes directly to Trade archive → Saved reviews, reading all saved records for the account. Long reviews/evidence are optional; failed saves retain input and allow retry.
- Actual entry-reason capture: select, write or skip after a new entry, stored by account/position and shown in archive/period exports. Entry and review prompts default on with independently persisted switches; backfilled history does not prompt.
- GitHub release checks on launch/every six hours, manual check/open page, one notification per new version, no automatic installation or trading-data transmission.
- Daily profit goal accepts amount/percentage against the day's baseline balance. Consistent intraday risk inputs; removes mandatory handwritten structured plans/strategy/setup fields from the main UI while retaining chart-object capture/existing data.
- Daily reports add actual M5 context/coverage. Unlinked plans are not violations; missing strategy/setup does not generate classifications. Insufficient process evidence stays unknown; P/L does not establish psychological entry/exit reasons.
- Period MD exports all queried filtered results with dates/count, removing the current-page selection choice. Daily/full MD retain local accounts; public ZIP hides account/server/path, with manual screenshot redaction.
- Bundles .NET 8, Python/dependencies, MT4/MT5 bridges, installation/changelog and SHA256. Schema 11/protocol unchanged.

Validation: 377 .NET tests for switches/reload, version comparison/failures, pet cards/focus, retry, account-isolated entry reasons, full-filter export, market context and percentage goals. Settings/card layouts and a local read-only bridge connection checked. No real order, close or modification was performed; simulated tests are not live execution acceptance.

## 1.0.0-rc.7 — 2026-10-01

- Seven-step console and four-step review guides: automatic first display, skip, persisted completion and manual revisit. Units/requirements in hover hints.
- Separate daily-plan persistence with positive/integer validation; same-direction limit means same symbol/direction total, loss-zone radius uses price ticks.
- Structured plan checks symbol, zone, reference entry, stop/target; failures do not report success. Chart imports require connected account/current bridge chart; account switching clears old links.
- New queries reset page while explicit paging remains; invalid custom dates and all-tab save/conflict failures are explicit.
- Manual historical database repair outputs a new copy only, archives duplicate versions, verifies integrity/original records, never automatically replaces user data.
- Complete Windows x64 ZIP/SHA256 with .NET 8, Python, collectors, bridges and docs; database/protocol unchanged.

Validation: 154 relevant .NET tests, guide layout/navigation and three historical-repair tests passed. Local MT5 read-only connection/continuous samples checked; user confirmed pet visibility. More terminal/execution scenarios remain needed; automated samples are not real execution acceptance.

## 1.0.0-rc.6 — 2026-09-29

- Bundles official Python 3.13.15, MetaTrader5 5.0.6147 and NumPy 2.4.3; first MT5 setup needs no Python installation/dependency download.
- Prioritizes the bundle over old user environments. Wizard identifies bundled status and asks for complete extraction if damaged.
- Build verifies Python download/dependency/collector imports; embedded collectors use explicit UTF-8 and unbuffered output.

Validation: three runtime-path and 23 Python tests. ZIP reextracted into a Chinese/space-containing path; system Python excluded and pip downloads disabled. Bundled imports, both collector entry points and Windows PowerShell 5 environment checks passed. Damage reports failure without installing user dependencies. Database/trading logic unchanged.

## 1.0.0-rc.5 — 2026-09-28

- Account-isolated persistent MT4 raw order archive, recheckable after scope reduction, restart or offset change.
- Validates partial closes using broker `from #` / `to #` tickets and attributes, groups original positions, restores initial total volume, preserves each close's P/L/fees/ticket and counts complete only after full closure. Snapshot/history position signatures avoid mistaking replacement tickets for new trades.
- Schema 11 regroups old independent-ticket projections, retains review documents/revisions, carries notes/attachments to grouped trades and marks rereview.
- Actual MT4 quote archive/replay: attached-symbol OnTick and one-second samples elsewhere; actual second timestamps/gaps, no invented milliseconds/full coverage.
- Forex/CFD initial risk uses contract size, P/L currency and conversion quotes; base-currency accounts convert at stop. Futures retain broker tick-value calculation; missing parameters stay unknown.
- M5 requests select symbols automatically and reject invalid symbols, exposing MT4/MT5 suffix differences. Report accounting/support/install docs updated.
- Stale snapshots no longer terminate MT4 collection; remain disconnected until valid data returns.

Validation: 351 .NET tests for partial-close chains/reductions, idempotence, reduced history, offsets, legacy note migration, quote isolation/limits, currency conversion and reconnect. MT4 compiled with zero errors/warnings. Read-only MT5 `order_calc_profit` verified EURUSD, XAUUSD and USDJPY stops in both directions: six cases agreed at account-currency precision; no trading calls.

Post-release hardware integration (2026-09-28): after reattachment, valid snapshot/history and position signatures persisted. Checked broker UTC+3, one actual position, 11 chart objects/plan mapping, 275 M5 bars and 1,162 quotes. Official replay wrote/read quotes through an isolated test database; ViewModel used broker time. Four position P/L samples saved. Without SL, initial risk stayed unknown; open trade excluded from complete counts; repeated sync was idempotent. No order, close or modification, production migration or full UI acceptance.

Remaining nonequivalence: unread MT4 history, pre-collection ticks, partial closes lacking broker links and historical DST cannot be reliably reconstructed. Calendar remains public weekly data. The real test account had no closed trades; real partial-close/closed-P&L/stop-risk reconciliation still lacked samples. Automated examples do not replace real execution validation.

## 1.0.0-rc.4 — 2026-09-28

Source, self-contained Windows x64 ZIP and SHA256 files were released together; README/install links pointed to `rc.4`.

### MT4 feature coverage

- Updated read-only bridge synchronizes terminal-loaded history, commission, swap and cash flows into statistics, behavior, loss zones, quick reviews and daily reports. Reconnect, repeated sync and order corrections are idempotent.
- Chart-plan import/recording and loss-zone chart display, scoped to selected terminal/account, without trading operations.
- M5 candles from the selected terminal through the shared review/replay flow; request/account/symbol/time validation.
- Position samples for MAE/MFE/giveback. Risk estimates use broker tick step/value, account currency, size and stop distance. Only reliable entry samples qualify for initial risk/R.
- Forex Factory weekly calendar and high-importance advance reminders, refreshed about every 15 minutes. Fetch failures explicit; requests contain no trading account/positions.
- Old plugin, uncalibrated time, failed/expired history explicit; dependent home metrics show “—”, not zero trades.

### Time and reports

- MT4/MT5 trading days and UI times consistently use broker server time, fixing mixed PC-local/UTC trade lists, timelines, quick reviews, behavior details and replay.
- Resizable report with Overview/actions, Trades/risk and Full Markdown tabs; scrollable trade table.
- Rechecks trades, behavior and equity by server day, excludes adjacent-day records and uses consistent overview/detail data.
- Cooldown violations count actual risk-triggering records only, excluding normal evaluations so violations cannot exceed risk alerts.

### MT5 differences and upgrade requirements

- MT4 needs All history selected; only terminal-loaded records are readable and full coverage cannot be confirmed. Uncollected process data is not invented.
- MT4 history uses order tickets; MT5 uses executions. At this release each closed MT4 ticket counted independently; partial-close remainder tickets were not guessed/grouped. Fees belong to closing day; ticket volume is not original maximum position size.
- MT4 cannot natively backfill historical ticks. M5 retains gaps/coverage. Public weekly calendar lacks live actuals and MT5-equivalent post-release notifications.
- MT4 raw times are server times; internal UTC uses current calibration. Historical DST cannot be recovered from order APIs, limiting UTC alignment/holding duration across DST. PC-local time does not define trade dates.
- Install/reattach the updated bridge after upgrading. During closed markets without a known offset, set `InpServerUtcOffsetMinutes` or wait for a quote. Do not clear the database.

Validation: 339 .NET tests for mapping, isolation, midnight boundaries, persistence/reports, idempotence, candle protocol and calendar; chart-plan create/change/delete/dedup also verified. Both bridges compiled with MetaEditor and self-contained Windows x64 output built.

Post-release integration (2026-09-28): updated EA on logged-in WeTrade MT4 demo verified UTC+3, cash records, empty-account reviews/reports and repeat-sync consistency. Eleven real chart objects entered plan processing; XAUUSD returned 275 M5 bars; weekly calendar returned 141 server-time events. Isolated local database, no orders/closes/modifications. No real closed trades were available; real closed P/L, partial-close and position-risk scenarios remained unverified. Not all UI flows were covered, and equivalence across brokers/MT5 was not claimed.

## 1.0.0-rc.3 — 2026-09-25

- Adopts standard [MIT License](LICENSE), `Copyright (c) 2026 cz1978`.
- Repository/package contain the license; README/install explain commercial use, modification and redistribution.
- Repository renamed to `cz1978/tradepet`; download/install/clone links updated.
- Licensing, docs, packaging and version only; functionality identical to `rc.2`.

## 1.0.0-rc.2 — 2026-09-25

Still a release candidate, focusing on reduced intraday interruptions, simpler setup and MT4 live monitoring.

### Quiet quick reviews

- Closes silently join a review queue instead of opening a centered topmost window.
- Pet/tray Quick review opens one trade at a time; repeated access returns to the existing window.
- No forced topmost/focus. Later requeues after ten minutes without reopening automatically.
- Generated exit/giveback/improvement clues with take-profit/stop-loss and manual profit/loss/breakeven presets and free editing.
- Analysis is an inference from retained evidence. Missing/stale/conflicting samples have explicit limits; an inferred reason is not terminal-confirmed execution.

### Initial setup and connection

- Four-step platform/terminal, connection preparation, status and pet-preferences wizard.
- Detect/repair MT5 dependencies or choose installed Python 3.13.
- Discovers running, registered and portable terminals. Missing saved terminals do not silently switch.
- Successful saving suppresses future automatic wizard display; Set up later remains incomplete. Restart after platform/terminal/environment changes.

### Read-only MT4 monitoring

- Local MQL4 plugin/file adapter for accounts, positions, pending orders and floating losses.
- No Python, DLL or trading permission; attach to one chart.
- Stale after ten seconds without update, removal or disconnect.
- At this release, history, full reviews, chart plans, calendar and reports were not connected; history-dependent metrics showed “—”.

### Installation/upgrades

- Self-contained Windows x64 ZIP with .NET 8, both plugins and Chinese instructions.
- Source release script accepts explicit compilers/output directory.
- Exit and back up `%LOCALAPPDATA%\TradePet` before extracting to a new directory. See [INSTALL.en.md](INSTALL.en.md).
- Queue is process-local; historical trades remain accessible after restart and saved reviews/notes persist in the database.

## 1.0.0-rc.1 — Initial public source

- MT5 read-only collection, desktop pet/mini positions, intraday alerts, chart plans and loss zones.
- Review workspace, macro calendar, daily reports, local attachments and backup/restore.

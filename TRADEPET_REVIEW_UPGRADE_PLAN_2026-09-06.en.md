# TradePet unified review upgrade specification

English | [简体中文](TRADEPET_REVIEW_UPGRADE_PLAN_2026-09-06.md)

Date: 2026-09-06.

Status: R01–R12 implemented, with automation, 100,000-trade performance, real-terminal read-only market data and Release delivery verified. V20 multi-scale real-window screenshots remain a manual visual check because that Codex session exposed no native Windows control interface; this was not claimed as an automated pass. The specification remains one complete delivery scope, not batches, phases or an MVP.

## 1. Objectives and decisions

Replace statistics/cards/classification with an end-to-end workspace: **trade facts → contemporaneous reasons/evidence → execution assessment → repeated problems → personal improvement rules → subsequent execution checks**.

Retain Windows WPF, read-only MT5 and local SQLite. Reuse win rate, profit factor, expectancy, grouped analysis, plan matching, behavior rules, R and excursion calculations. New features share account/trade identity, filters, provenance and metric definitions.

- One scope: trade archive, calendar/journal, versioned strategy rules, execution ratings, trade ideas, behavior evidence, improvement tracking, actual replay, opportunity observations, export and full backup.
- Raw executions, intraday observations, manual entries, historical backfill and rule recalculation have separate provenance. Later explanations never overwrite original facts.
- The pet alerts during trading; reviews explain/verify. Enabled personal improvement rules share their version with the pet.
- Existing isolated replay becomes diagnostic **Rule demo**. Actual **Trade replay** uses history/executions with separate entry/storage.
- Netting/exchange complete projection remains unsupported; existing monitoring-only boundary is explicit, not claimed resolved.
- Excludes cloud sync, social/mentor collaboration, trading automation, generic matching/backtesting and unconstrained AI evaluations. Structured traceable day/week summaries link conclusions to trades.

## 2. Product evidence and choices

Official feature descriptions checked 2026-09-06; no paid-account live review or marketing return promises. TradePet choices below are design judgments.

| Product | Official capability | TradePet decision |
| --- | --- | --- |
| TradeZella | Executions, images, trade/day notes in one [trade page](https://help.tradezella.com/en/articles/5860216-understanding-the-trade-page) | Unified archive; shared daily journal, not duplicated per trade |
| TradeZella | Linked strategy rules, reviewed status, daily Progress Tracker [workflow](https://help.tradezella.com/en/articles/13863136-getting-started-with-tradezella) | Strategy versions, checklist, completion and goals; freeze pre-session rules/version post-session ratings |
| Tradervue | Time/symbol/tag/risk [reports](https://help.tradervue.com/article/3423-reports-and-statistics), execution [split/merge](https://help.tradervue.com/article/3480-split-merge-trades) | Filters, comparisons/drill-down; separate trade ideas without rewriting raw MT5 deals/position projection |
| Edgewonk | Entry/management/exit decisions linked to performance, notes/missed opportunities [psychology journal](https://edgewonk.com/trading-psychology) | Separate execution quality/P&L, self-reported emotion, separate untraded opportunities |
| TraderSync | Replay from [trade details](https://tradersync.com/support/how-do-i-replay-an-existing-trade/) | Synchronize actual market/events, never call fixed examples historical replay |

Do not mechanically accumulate competitors' features. Prioritize local traceability and turning review findings into pet rules checked by later reviews.

## 3. Existing code and gaps at specification time

This section records then-current source; subsequent sections specify the design.

| Existing capability / source | Gap / treatment |
| --- | --- |
| Manual strategy/setup/tag/plan classification: [ReviewModels](src/TradePet.Core/Domain/ReviewModels.cs), [Rows](src/TradePet.App/ViewModels/Rows.cs) | Add notes, attachments, status and unified details while retaining classification |
| Performance/time/symbol/strategy/R/excursions: [calculator](src/TradePet.Core/Trading/ReviewAnalyticsCalculator.cs) | Linked charts/trades, two-group comparison, consistent drawdown/coverage |
| ReviewFilter strategy/setup/single tag: [models](src/TradePet.Core/Domain/ReviewModels.cs), [UI](src/TradePet.App/Views/MainWindow.xaml) | Expand date/symbol/side UI and multi-tags, reuse grouping |
| Behavior and separate timeline: [calculator](src/TradePet.Core/Trading/BehaviorAnalyticsCalculator.cs), [runtime](src/TradePet.App/Runtime/TradePetRuntime.cs) | Unified event–trade evidence/drill-down |
| TradeExcursion extrema/coverage and EquitySample: [persistence](src/TradePet.Infrastructure/Persistence/AppDatabase.Review.cs) | Full position P/L series; extrema alone cannot produce real process curves |
| Versioned structured plans: [matcher](src/TradePet.Core/Trading/TradePlanMatcher.cs) | Separate one-off plans from reusable strategy templates; versioned per-rule evidence |
| Fixed ReplayCoreScenarioCoreAsync: [runtime](src/TradePet.App/Runtime/TradePetRuntime.cs) | Rule demo, not user-history replay |
| Schema 1–3/recovery/backup: [migrations](src/TradePet.Infrastructure/Persistence/SchemaMigrations.cs), [paths](src/TradePet.Infrastructure/Persistence/TradePetPaths.cs) | Consistent manual-data/attachment backup, no second account database |

Correct misleading metrics: (1) drawdown percentage divides by cumulative realized-profit peak and tests reproduce ~153.85%; this is not unlabeled account-capital drawdown ([tests](tests/TradePet.Core.Tests/ReviewAnalyticsTests.cs)). (2) Separate excursion coverage from initial-risk coverage. (3) Unify locally formatted trade times with server-offset groups and expose historical time-source reliability. (4) Move multi-collection/long queries out of Runtime; browsing reviews never replays real alerts.

## 4. Pages and shared interaction

Retain top-level navigation. Review analysis has six areas; trade details use a right panel/separate detail view, not extra scattered top-level menus.

| Area | Default content | Actions |
| --- | --- | --- |
| Overview | Compact metrics, P/L/drawdown, pending count, coverage | Select dates; drill into anomalous points |
| Calendar/journal | Month, daily cash/process, pre/post notes | Journal, events, complete day review |
| Archive | Raw trades, ideas, pending/reviewed | Search, classify, details, groups |
| Analysis | Group performance, execution/results, evidence/comparison | Saved filters, samples, rule versions |
| Strategy/improvement | Templates, checklists, goals, weekly/monthly reviews | Version, reminders, goal checks |
| Opportunities | Untraded observations/deliberate skips | Reasons, images, strategy links |

Shared toolbar: account, dates, symbol, direction, strategy, setup, tags, status, ratings, idea and search; advanced filters collapsed. Default one account, never directly sum differing currencies. Export/data status are shared actions.

Common rules: select complete trades by final-close server day; calendar cash by execution/fee occurrence. A calendar day opens the full account day; additional filters must label it Filtered results. Every metric/behavior drills into exact samples and restores filters/scroll on return. Switching trades preserves drafts; show saving/saved/failure/conflict and only mark saved after commit. Keyboard previous/next/save/complete; charts have text/tables and do not rely only on color. Separate blank/no samples/file access denied/history gaps/offline; unknown is missing data, not zero. New UI acceptance requires actual WPF screenshots/interactions, not model tests alone.

## 5. Complete functional specification

### R01 · Shared filters, search and review queue

- Strategy/setup/tags with all/any combinations, deduplicated tags and consistent case rules.
- Search symbol, position ID, ticket and note body; named filters retain account/accounting scope.
- Pending, Draft, Reviewed, Needs rereview for trades/days separately; open positions can only be drafts.
- Closed trades enter queue; largest loss/giveback, rule events, oldest pending or default time ordering.
- Completion requires at least one rating and summary; explicit normally ended within plan is allowed. Emotion/images optional.
- Completion retains source/rule versions. New fees, corrected executions or assessment-basis changes trigger rereview without erasing conclusions.
- Batch strategy/tags/status; preview affected count before completion and never silently classify unrated trades compliant.

### R02 · Individual trade archive

| Area | Fields |
| --- | --- |
| Execution facts | Account/currency, symbol/side, initial/add/partial/final events, quantities/prices, fee breakdown, complete net P/L |
| Original plan | Plan/strategy version, intended entry zone, initial stop/target, initial risk/value provenance |
| Process | Size, observed SL/TP, behavior, delivered alerts, gaps |
| Manual review | Entry/exit reasons, did well/improve/next action; optional emotion/environment |
| Evidence | Local PNG/JPEG/WebP paste/select, captions, observed/retrospective times, linked event, thumbnail/zoom |

Pre-session/intraday/post-session timestamps differ; users can specify occurrence, never overwrite recorded time. Import images into managed storage; no automatic screenshots of other windows. Replay charts and contemporaneous screenshots have different provenance. Show observed stop changes, never reconstruct unseen edits. Suggested tags may be accepted/revoked, retaining raw events and separate disagreements. Notes cannot edit MT5 prices/fees/quantities; corrections require resync/versioning.

### R03 · Calendar, journal and daily process summary

- Month shows cash P/L, initial entries, pending reviews, notes/gaps; month totals/date navigation.
- Pre-session, intraday and post-session sections; no-trade days can record deliberate rest/no suitable opportunity.
- Timeline of deals, positions/rules; cross-day positions labeled carried over; partial-close cash belongs to occurrence day.
- Expandable factual cards: before/after goal, entries after losses, cooldown entries, fees, completion.
- Net P/L of new trades after goal counts complete trades initially opened after first goal hit, listing open trades separately; trades opened before the goal are excluded.
- Execution cash P/L after goal is separately titled and counts post-hit executions/fees.
- First hit requires then-valid goal version/reliable intraday records; unavailable time stays indeterminate, never inferred from final day state.
- Manual day-end summary; later trades/fees mark updated/review again.
- Summary: facts, one successful action, one improvement, linked trades, next check.

### R04 · Strategy templates and ratings

Reusable Playbook is distinct from a dated concrete plan. Include name, symbols/environment, entry/risk/management/exit rules, images and invalidation. Edits create versions; trades bind pre-entry versions and retrospective links are marked, not pre-session evidence. Rule outcomes Pass/Fail/Unknown/Not applicable retain automated/manual provenance. Emotion/exit motivation are self-report only, never inferred from losses. Separate entry/management/exit compliance, no opaque overall score. Cross execution outcomes with win/loss/breakeven; incomplete evidence is pending, not compliant. Any failed critical rule means violation; only all known applicable passes mean compliance; no assessable rules means unknown. Show version-specific counts/performance/compliance and explicitly label mixed versions.

### R05 · Trade-idea groups

Separate review entity linking positions of one idea. Select same-account/exact-symbol trades, allowing cross-day/opposite-side members with an explicit direction-switch label. A position belongs to at most one active idea; log changes/unlink/regroup. Show thesis, time span, ordered members, first/reentries, P/L, fees and ratings. Default counts positions; idea mode counts groups plus ungrouped single units, never mixed. Deduplicate member P/L; reconstruct simultaneous exposure from execution times, not summed maxima. Do not average opposing entries or sum R; insufficient data shows amounts/member R. Explainable candidate groups need user confirmation; manual groups cannot bypass unsupported netting projection.

### R06 · Charts, risk, fees and comparisons

Drill-down charts: final-close ordered cumulative net P/L/drawdown; daily cash bars/calendar with separate equity layer; symbol/side/strategy/setup/tag/weekday/entry-hour/duration distributions; MAE/MFE versus realized R scatter, giveback/process; two equally defined groups with overlap counts. Missing samples stay listed without fabricated points.

Groups show count/net/win rate/average/expectancy/R/risk coverage/fees with independent valid denominators. Below 30 trades show a small-sample hint; 30 is not significance, and correlations are never causes. Compare amounts alongside entry-size/known-risk distributions. Commission/swap/fee retain signs and unallocated account fees remain separate. Planned-entry versus fill is planned-price deviation, not true slippage without request/verified quote evidence. Save custom sessions; named zones apply dated DST, unconfirmed broker offsets are labeled estimates.

### R07 · Behavior, evidence and associated results

Reuse reentry, loss-zone persistence, overtrading, post-loss size, cooldown, plan deviation and giveback. Retain rule ID/version/threshold, occurrence/first observation/account/server day, triggering/context trades/window, facts/missingness/live-or-recomputed provenance, delivered/suppressed reason and manual explanation.

Show matched-trade associated P/L and unmatched comparison, not proven avoidable losses. Trades appear once per rule; overview union deduplicates and group amounts cannot be added. Account giveback is not assigned a single culprit; associate positions/subsequent trades with roles. Behavior uses event day while trade performance uses exit day, explicitly showing differing scopes without dropping prior evidence. Live events and current-rule historical recomputation remain separate; recomputation writes review records only, no pet alert. Cards open exact evidence; insufficient-evidence annotations supplement immutable original observations.

### R08 · Period summaries, goals and pet integration

- Weekly/monthly/custom summaries include execution, repeated behavior, samples, coverage and one chosen next action.
- Goals specify rule, account/symbol, start, target, window, baseline snapshot and end conditions.

Automatically checkable goals reuse existing rules (cooldown-entry/outside-plan rates); subjective goals require manual checks, not fabricated results. Freeze measurement versions; changed thresholds create a new version/baseline retaining old observations. Pet reminders require explicit enablement with trigger/frequency preview; archiving stops future reminders, not history. Existing-risk/personal-goal matches yield one visible reminder with both sources. Period summaries show compliance denominators, violations and comparable before/after results; no eligible samples means no comparison. No trigger opportunity is Not applicable, neither 100% compliant nor failure. Generated conclusions state linked statistical facts; edits create separate versions.

### R09 · Actual trade/market replay

Start from trade, idea or day; play/pause, previous/next events, bar steps, speed and timeline. Show available M1/M5/M15/H1, executions, then-known plans/SL/TP, alerts/notes; optional available historical ticks with actual intervals, without universal tick-history promises. Position P/L series are separate and break at gaps; legacy extrema never become invented curves. Bar backfill is market reconstruction, not screen recording, full indicator state or verified slippage.

MT5 chart history limits bars; Python APIs take UTC and may return None ([bars](https://www.mql5.com/en/docs/python_metatrader5/mt5copyratesrange_py), [ticks](https://www.mql5.com/en/docs/python_metatrader5/mt5copyticksrange_py)), requiring explicit scope/precision/failure. OHLC cannot establish intra-bar high/low order. Bar mode reveals only completed candles; tick mode builds only from arrived quotes. Hide future executions/results and retrospective notes/plans until Full review is enabled. Backdating occurrence does not make a later note contemporaneous. Missing quotes allow execution-only replay, distinguishing weekends from failures. Explicit terminal/account/request identity, no login changes or long queries in live collection. Replay state never alters live session, ledger, alert dedup or positions. No hypothetical matching; opportunities and actual performance stay separate.

### R10 · Untraded opportunities and deliberate skips

Record valid skips alongside missed opportunities. Fields: observation/record times, symbol/side, strategy version, conditions, reference entry/stop/target, image, reason and later review. Reasons include rule mismatch, insufficient risk budget, rest, hesitation, unnoticed or custom. Separate beforehand observation from retrospective discovery; retrospective records cannot count as contemporaneous recognition. No actual execution means no real P/L/R/win rate/discipline result and no hypothetical maximum profit added to performance. Subsequent-trade links do not turn observations into additional trades. Report counts/reasons/then-valid rules, not what the user supposedly should have earned.

### R11 · Export, drafts and full backups

- UTF-8 CSV for filtered trades/deals/behavior, Markdown/HTML journals/summaries, ZIP attachments.
- Include accounting, account display/currency, zone, range, versions, generation time and coverage. Lossless JSON stores large position/ticket IDs as strings.
- Spreadsheet-safe CSV handles formula prefixes/long IDs; lossless backups preserve raw values and CSV is never the only recovery source.
- Short-delay draft autosave and immediate explicit commit; revision checks retain both conflicting versions.
- Full backup: consistent SQLite, revisions, attachments, strategies, goals and manifest hashes using existing backup/recovery mechanisms.
- Validate format/paths/hashes/integrity/identity in temporary storage, stop writes, preserve a full current backup before switching; failures leave current data usable.
- Queries/exports cancel; partial outputs never claim success.
- Public package hides account/local paths by default, warns images may contain saved private information, never auto-uploads.

### R12 · Data quality and legacy compatibility

Show execution scope, position sampling, initial risk, market, pre-session-plan and manual-review coverage. Expand numerators/denominators/missing reasons/samples; empty is not full coverage. Preserve old classifications; new status starts Pending and ratings Unknown. Legacy extrema remain labeled legacy values, never invented process curves. Uncertain historic offsets retain prior date attribution with provenance; time corrections create analysis versions without changing raw UTC. Read-only storage still permits committed queries; unsaved drafts remain, retry with revision checks, no premature saved status. Preserve failed-versus-empty contracts and silent recovery/backfill.

## 6. Metric definitions and examples

### 6.1 Definitions

| Metric | Basis |
| --- | --- |
| Complete-trade net P/L | Same account/position source profit+commission+swap+fee; final-close day selects performance |
| Calendar realized cash | Server-day deals/trading-cost cash, including partial closes, excluding capital flows; unallocated fees separate |
| Win rate | Wins/(wins+losses), breakeven separate; empty denominator has no sample |
| Cumulative realized drawdown | Historical peak minus current complete-trade cumulative net P/L, in currency |
| Profit-peak giveback ratio | Drawdown/positive cumulative profit peak; full label in details, not account-capital drawdown |
| Equity drawdown rate | Unitized continuous equity around verified external flows, using curve peak; missing pre/post-flow samples split segments/gaps, never use cumulative profit as denominator |
| Initial-entry realized R | Final net P/L/verified initial-entry risk; additions labeled initial-risk denominator, not lifetime risk; retrospective estimates separate |
| MAE/MFE | Since initial entry, realized cash net plus remaining floating P/L, reconciling fees/swap to avoid duplication; adverse/favorable extrema of this process |
| Compliance | Pass/(Pass+Fail), alongside assessment coverage; Unknown/Not applicable excluded without hiding missing assessments |
| Review completion | Reviewed complete trades/complete trades requiring review in scope; Draft/rereview are incomplete |
| Behavior-associated P/L | Specified-role matching trades, deduplicated by account+position, never causal losses |

Incomplete equity shows observed curve/drawdown with possible missed intra-sample moves. Uncorrectable flow intervals have no full drawdown rate. Different currencies have no implicit conversion.

### 6.2 Distinct coverage

Initial-risk coverage counts complete trades with entry evidence; excursion coverage independently counts qualifying sampling and shows duration, longest gap and interval. Reliable R remains despite unrelated excursion gaps; absent initial risk means unknown R. Store algorithm versions and never average legacy remaining-floating extrema with new values. Retain ≥90% duration as a minimum plus sample within two seconds of first entry, verified completion and no gap over five seconds. These versioned parameters indicate sampled extrema, not proof of continuous coverage.

### 6.3 Synthetic acceptance examples

- D1 partial +40, D2 final +60: calendar 40/60, complete performance D2=100.
- One trade hits cooldown and size rules, -100: both cards -100, union -100, never -200.
- +10,-5,+8,-20: peak 13, drawdown 20; 153.85% is profit-peak giveback, equity drawdown needs capital/equity.
- Initial risk 100, net 150: R=1.5 even if process gaps invalidate MAE/MFE.
- Deposit 1,000/no trades: trading cash P/L zero, no strategy profit or false recovery.
- No opportunity/no violation: Not applicable, never infer 100% excellent execution.

## 7. Data, storage and revisions

### 7.1 Identity, time and provenance

Retain account+position and encapsulate `TradeKey(AccountKey, PositionId)`. Ideas/templates/journals have stable IDs; every query/link carries account, not position alone. Editable objects retain revision/created/updated UTC; evidence adds event/recorded UTC, source/version/time basis. Pre-session snapshots/raw events are immutable.

Suggested provenance: MT5 executions, intraday samples, contemporaneous plans, manual contemporaneous/retrospective notes, historical backfill, rule recomputation. Trusted original execution/quote times can replay despite late import. Plans/judgments/notes/recomputed rules require both occurrence and recording times; do not invent then-known information.

### 7.2 Entities

Logical/storage targets; rebuildable charts need not all become tables.

| Entity/table | Content/constraints |
| --- | --- |
| trade_review_documents | Unique account+position; status/notes/revision/reviewed source+rule versions |
| daily_journals / period_reviews | Account+date/period; pre/intra/post notes, summaries, completion/basis versions |
| review_revisions | Document+revision; old content, change source/time, conflict recovery |
| attachment_assets / attachment_links | Hash/format/size/managed relative path/provenance times; links to trade/day/strategy/opportunity, no cross-account reuse |
| playbooks / playbook_versions | Templates/immutable rules, scope/effective time; concrete plans link versions |
| trade_rule_assessments | Trade+template+rule, four-state result/evidence/manual explanation/revision |
| trade_campaigns / trade_campaign_members | Same account, unique active membership |
| trade_observations / position_pnl_samples | Initial/stop observations, coverage and process values, then-known facts/algorithm version |
| behavior_occurrences / behavior_trade_links | Stable event/rule/provenance; triggering/prior-evidence/exposure roles |
| improvement_goals / goal_observations | Version/enabled/frozen baseline/window/opportunity counts/results; existing alerts linked |
| opportunity_records | Independent untraded identity, strategy, observed/recorded times, reason/evidence/actual-trade link |
| market_data_ranges / market_bars / market_ticks | Terminal/broker/exact symbol, UTC range/precision/coverage/failure/version |
| server_time_segments | Account/terminal, known/estimated offset intervals, inference/version |
| review_saved_filters | Account/full filter JSON/sort/version, no live-session state |
| review_data_versions | Deals/fees, metadata, samples, rules/time versions for consistency/cache/rereview |

Never reuse caches across brokers using shorthand symbols; preserve source terminal even with server identity. Tick dedup needs same-millisecond sequence/fingerprint, not timestamp alone.

### 7.3 Migration and saving

Append migrations after existing 1–3 using the latest actual version at implementation; never edit executed migrations. Retain deal/trade keys and manual metadata; composite foreign keys/transactional account checks and account+date/position indexes. Index old strategy/tag text without losing originals or pretending retrospective text is a pre-entry template. Source backfill, derived versions/rereview use explicit transactions; expectedRevision rejects stale edits. Attachments stage/verify/atomically move before reference commit; collect unreferenced files but never delete referenced assets during backup. Freeze manual writes/pin snapshot for consistent manifest/references; failed restore never overwrites healthy data. Only rebuildable cache is disposable, never notes/rules/screenshots/goals.

## 8. Application architecture and API contracts

### 8.1 Boundaries

Keep pure rules in Core; add WPF-free `TradePet.Application` for review use cases/testable orchestration.

```text
TradePet.App → TradePet.Application → TradePet.Core
TradePet.App → TradePet.Infrastructure → TradePet.Application / TradePet.Core
```

Application does not depend on Infrastructure; adapters implement repository/history interfaces and WPF startup composes them. No interfaces for every pure calculator.

| Responsibility | Content |
| --- | --- |
| ReviewQueryService | Account/filter/versioned snapshot, paging, charts/sample drill-down |
| JournalService | Trades/days/period summaries, drafts/revisions/attachments/completion |
| PlaybookService / CampaignService | Strategy versions, ratings and unique idea membership |
| BehaviorReviewService / ImprovementService | Evidence, deduplicated samples, goals/actual alert links |
| TradeReplayService | Market requests/precision/gaps, cursor/visibility |
| ReviewExportService | Snapshot exports, attachments/full backup/restore |
| Core DataQualityCalculator and peers | Metrics/coverage, four-state ratings, event groups/comparisons |

### 8.2 Use cases

- `QueryReview(request, cancellationToken)`: immutable snapshot; account/range/unit/filters/generation/query ID.
- `LoadTradeDetail(TradeKey, revisionToken)`: complete evidence, never implicit UI-selected account.
- `SaveJournal(command, expectedRevision)`: new version, validation error, conflict or storage failure; failures never treated as success.
- `MarkReviewed(TradeKey, sourceVersion, ruleVersion)`: validate completeness/freeze basis.
- `BuildBehaviorReport(request, ruleVersion)`: events/identities/gaps, no real notifications.
- `EnableGoal(goalVersion)`: explicit rule/frequency through existing priority/dedup gates; historical replay cannot call.
- `LoadReplay(request)`: request/source/requested and actual coverage/precision/failure; account changes invalidate old responses.
- `ExportReview(snapshotToken, destination)`: pinned snapshot, never mixed old/new filters.

### 8.3 Scheduling/state

UI collects parameters/renders snapshots, never enumerates mutable Runtime dictionaries. Queries use background database read snapshots/independent cancellation; cache charts by version. Account/filter/replay changes invalidate old queries; check account/sessionGeneration/queryId before commit. Live gate only copies/commits briefly, never holds history/attachment/export/market IO. Supervise/cancel new tasks; saves wait for commit, exit handles drafts with bounded cleanup. Notification preflight/commit/display/delayed hide share state ownership; goals cannot reintroduce enumeration/bubble races. Historical reads do not upsert real alerts/trades/live ledgers; persisted analysis caches have separate IDs/algorithm versions.

### 8.4 Historical market-data protocol

Dedicated `python/tradepet_mt5_history_worker.py`/C# client for on-demand bars/ticks; no long live-worker queries. Explicit terminalPath/current-account validation, no account switch. Reuse envelope, with dedicated-process capabilities/versioned payloads that old live/bridge consumers cannot confuse. Requests carry requestId/expectedAccountKey/terminalId/symbol/timeframe/UTC range/type/max count. Responses carry chunk order/source identity/actualRange/coverageStatus/precision/payloadVersion/error; complete only after all chunks persist. Maximum 5,000 bars/10,000 ticks per chunk, boundary dedup, overall concurrency one. Cancellation/timeout can end only the dedicated process, never user terminal/live worker. MT5 contention still requires section 10 latency checks; shrink windows/pause requests if needed, not claim isolation eliminates all contention. No new bridge screenshot capability; images import locally, existing objects/time remain supported.

## 9. File scope and implementation dependencies

Historical file plan: paths marked new were not yet present when planned, not a claim they were already edited.

| Files | Role / planned change |
| --- | --- |
| [ReviewModels](src/TradePet.Core/Domain/ReviewModels.cs), new Domain/Review | Compatible documents/status/templates/groups/provenance/quality |
| [Analytics](src/TradePet.Core/Trading/ReviewAnalyticsCalculator.cs), [query engine](src/TradePet.Core/Trading/ReviewQueryEngine.cs) | Denominators/samples/units/chart data |
| [Behavior](src/TradePet.Core/Trading/BehaviorAnalyticsCalculator.cs), new Core/Review | Evidence/version, quality/ledger/ratings/comparison |
| New src/TradePet.Application/Review and project | Services/repositories/notification intents/cancellation/versioning |
| [Persistence](src/TradePet.Infrastructure/Persistence/AppDatabase.Review.cs), [migrations](src/TradePet.Infrastructure/Persistence/SchemaMigrations.cs), new Persistence/Review | Migrations/revisions/links/indexes/snapshots |
| [Paths](src/TradePet.Infrastructure/Persistence/TradePetPaths.cs), new attachment/export adapters | Managed assets/manifests/restore/temp lifecycle |
| [Envelope](src/TradePet.Core/Protocol/ProtocolEnvelope.cs), new historical payload | Dedicated capabilities/protocol, live compatibility |
| New history worker / Infrastructure/Mt5/Mt5HistoryClient.cs | Identity/read-only/chunk/cancel, independent live loop |
| [Runtime](src/TradePet.App/Runtime/TradePetRuntime.cs) | Extract review orchestration; short snapshots/evidence/unified intents |
| [MainViewModel](src/TradePet.App/ViewModels/MainViewModel.cs), [Rows](src/TradePet.App/ViewModels/Rows.cs), new ViewModels/Review | Independent state/save/conflicts/filter/detail navigation |
| [MainWindow](src/TradePet.App/Views/MainWindow.xaml), new Views/Review | Six areas/details/charts/journal/replay/export, existing SkiaSharp |
| [Solution](TradePet.sln), csproj | Application/non-WPF integration tests/reference direction |
| [Core tests](tests/TradePet.Core.Tests), [Infrastructure](tests/TradePet.Infrastructure.Tests), new Application.Tests | Metrics/isolation/revisions/recovery/Fake MT5/real orchestration |
| [README](README.en.md), [release script](scripts/build-release.ps1) | Feature/protocol boundaries/history-script/assembly packaging |

Dependency order: shared models/repositories → Core → Application → SQLite/market/attachments → WPF → use-case/UI acceptance. This is dependency ordering, not split delivery. Original implementation follows one active TODO; only unified acceptance permits an Implemented status.

## 10. Unified acceptance

Stable contract. As of 2026-09-09 V01–V19 had automated or real read-only evidence. V20 checked six areas/details/edit/replay/export/keyboard/scrolling/PerMonitorV2; multi-scale native screenshots still required manual Windows confirmation due session UI limitations.

### 10.1 Requirement matrix

| ID | Requirement | Required outcome |
| --- | --- | --- |

| V01 | R01 | Strategy/tags AND/OR/body combinations, restart persistence, exact drill-down samples |
| V02 | R01/R02 | Failed save not marked saved; revision conflicts; committed notes/assets survive switch/restart |
| V03 | R01/R12 | Added fees trigger rereview retaining conclusions; recomputation preserves classification |
| V04 | R02/R09 | Execution/stop/plan/retrospective provenance; no future evidence in replay |
| V05 | R03 | Partial closes in D1/D2 cash; complete trade once on final day |
| V06 | R03 | Post-goal new trades/cash distinct; unknown hit time yields no assertion |
| V07 | R04 | Unknown/not-applicable not passes; compliant loss/noncompliant win; versions preserve history |
| V08 | R05 | Account isolation, duplicate-member prevention; grouping keeps total P/L/accounting explicit |
| V09 | R06 | Giveback versus equity drawdown, deposits not profit; flow gaps downgrade intervals |
| V10 | R06/R12 | Reliable R remains with excursion gaps; algorithms never mixed |
| V11 | R06 | Group/comparison samples/overlap, small-sample hints, no invented zero-denominator/all-win values |
| V12 | R07 | Two-rule union counts P/L once; prior evidence opens; recomputation no real alerts |
| V13 | R08 | Frozen goals/baselines; no opportunity not success; two sources one alert; archived reminders stop |
| V14 | R09 | Real historical trade with synchronized bars/ticks/events; event-only/gap modes |
| V15 | R09/R12 | None/empty/truncated/same-ms ticks/cancel/account-switch faults preserve coverage/identity |
| V16 | R10 | Retrospective versus prior observation; opportunities excluded from actual P/L/R/win/compliance |
| V17 | R11 | CSV special characters/long IDs, escaped HTML/assets, safe ZIP paths, cancel not success |
| V18 | R11/R12 | Repeatable legacy migration/no loss; restored notes/images/rules/links/hashes agree |
| V19 | R12/API | Late A query/save cannot overwrite B; explicit read-only/recovery status |
| V20 | R01–R12 | Complete WPF flow, keyboard; no critical obstruction at 100%/150%/200% |

### 10.2 Performance and live path

Environment: Windows 10.0.26200, .NET 8.0.30, 32 logical processors, ~34.18 GB available RAM. Synthetic 100,000 complete trades, 300,000 executions, 365 days, ~95.4 MB SQLite. [Raw results](artifacts/review-benchmark-2026-09-09.json).

- Ingestion 2790.98 ms; 100 rows/page, no all-row VM instantiation.
- Cold query/comparison 2904.03 ms; five warm queries P95 0.2945 ms, meeting ≤3-second complex and ≤1-second cached goals.
- Cancellation observed in 85.67 ms. 4-Hz pulse P95 baseline 14.20 ms, concurrent uncached query 14.72 ms, extra 0.52 ms, meeting ≤200 ms.
- Cancel/account changes/stale queries stop commits quickly; bounded cleanup, no Dispatcher shutdown deadlock.
- Virtualized thumbnails, bounded/cleanable market cache without deleting notes/images.

### 10.3 Delivery conditions

R01–R12/V01–V20 need implementation/tests/live evidence, not skipping new paths because something partly existed. Core/Infrastructure/Application/Python history tests and WPF Release must pass. Read-only whitelist contains no order writes. Real terminals only read history/observe existing positions, never create acceptance orders; reproducible risks use synthetic events. Deliver a win-x64 package with new dependencies and verify import/query/notes/restart/replay/export using it. Document gaps/netting boundary and auditable historical TODO results before changing Defined to Implemented.

## 11. Implementation and known boundaries

R01–R12 delivered in one workspace: composite identity/provenance, document revisions/precise rereview, shared filters/queues, calendar/journal, strategy versions/ratings, ideas, cash/risk analysis, behavior/candidate tags with accept/revoke, goals, actual replay, opportunities, public export/full restore, quality drill-down and cache governance across Core/Application/Infrastructure/WPF.

Final automation on 2026-09-09: Core 128, Application 24, Infrastructure 49 and Python 16 tests passed; WPF Release zero warnings/errors. Read-only WeTrade history for one completed XAUUSD.s trade returned 27 M1 bars and 160 entry-area ticks, both complete; [evidence](artifacts/review-live-history-verification-2026-09-09.json). Static scan found no `order_send`, `OrderSend`, `TRADE_ACTION_DEAL`, `TRADE_ACTION_PENDING` or position-close/buy/sell calls.

Boundaries remain: MT5 hedging-only complete projection and actual terminal-retained market history, with gaps downgraded rather than invented. That session's Computer Use exposed browser surfaces only, no native WPF, so 100%/150%/200% screenshots could not be generated there. PerMonitorV2, vertical scroll containers and keyboard flows were checked; multi-scale visual confirmation remained a desktop task. These statements describe that historical delivery, not new current-release acceptance.

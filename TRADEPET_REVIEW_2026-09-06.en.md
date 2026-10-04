# TradePet functional and architectural review

English | [简体中文](TRADEPET_REVIEW_2026-09-06.md)

> Implementation status (2026-09-06): the body retains pre-fix evidence and counterexamples. Corresponding changes were implemented and regression-checked: F01–F06 and F08–F14 fixed; F07 resolved through an explicit support boundary, with complete-trade projection enabled only for MT5 hedging and position/account floating-loss monitoring retained for netting/exchange accounts. A01–A03 received their first restructuring. A04 extracted testable incremental/batch projection, query/date-range orchestration, server clock, worker recovery sessions, history planning and notification-delivery gates, with background-task supervision. Future features should continue moving out of `TradePetRuntime`. Final validation/file records were recorded in the historical `TODO.md`.

Review date: 2026-09-06. Scope: workspace source, main call chains, shared models, persistence, Python worker, MQL5 bridge, WPF bindings and tests.

The project has a usable layered foundation, but data integrity, daily P/L accounting and live recovery defects can miss statistics or alerts and generate incorrect alerts. Fix these before expanding review metrics/UI. Neither a rewrite nor microservices is necessary.

This review changed no business code, real database or portable output and created no trades on real accounts. Offline reproductions use current assemblies/FakeApi; static confirmations cite source. Concurrency, performance and desktop-lifecycle risks are separately identified rather than claimed to have occurred live.

Validation at review time: Core 61/61, Infrastructure 22/22, Python 6/6; WPF Release zero warnings/errors. Six synthetic scenarios are listed later. Passing existing tests does not validate uncovered paths below.

P1: data correctness, alert reliability or stability requiring priority fixes. P2: incomplete functionality, support limits or maintainability to address after stabilization. Shared causes are cross-referenced rather than counted as separate failures.

## Functional defects

### F01 · P1 · Failed MT5 queries treated as empty data [offline reproduction]

- Locations: [worker:59](python/tradepet_mt5_worker.py:59), [235](python/tradepet_mt5_worker.py:235), [369](python/tradepet_mt5_worker.py:369).
- `positions_get()` and `history_deals_get()` become empty via `or ()`, conflating failure with an empty account. Official API docs specify `None` on error: [positions_get](https://www.mql5.com/en/docs/python_metatrader5/mt5positionsget_py), [history_deals_get](https://www.mql5.com/en/docs/python_metatrader5/mt5historydealsget_py).
- Reproduction: positions return `None`, but `emit_snapshot()` reports success with `positions=[]`; persistent history failure still advances the cursor and eventually sets `isComplete=true, sourceCount=0`.
- Impact: positions may clear and excursion sampling end incorrectly; recovered positions can retrigger entry handling and history gaps appear complete.
- Recommendation: distinguish successful nonempty, successful empty and failed collection. Preserve the last valid state, record/retry failures, and do not advance cursor/completion.
- Acceptance: single/repeated position, order and execution failures preserve state/progress; recovery backfills missing intervals.

### F02 · P1 · Daily P/L mixes complete trades and daily executions [offline reproduction]

- Locations: [DailyStateCalculator:17](src/TradePet.Core/Trading/DailyStateCalculator.cs:17), [Runtime:2540](src/TradePet.App/Runtime/TradePetRuntime.cs:2540).
- Today's realized result totals complete trades finally closed today, while its high-water mark uses daily execution cash P/L: two different curves.
- Reproduction: open 2 lots, close 1 for +100, remaining floating P/L zero. The incomplete trade gives realized zero and giveback 100 from a high of 100, although the cash profit remains in the account.
- Cross-day partial-close profits and entry fees may be assigned to the final-close day instead of when they occurred.
- Recommendation: daily cash P/L uses that day's executions/classified fees; complete trades serve win rate, streaks, duration and review. Goals, loss limits, high-water marks and giveback must use one basis, with explicit overnight day-boundary treatment.
- Acceptance: partial/cross-day closes, entry commission, swap and cash flows reconcile item by item, with consistent current/high/giveback values.

### F03 · P1 · Annual completion hides gaps while the assistant is stopped [static confirmation]

- Locations: [Runtime:1914](src/TradePet.App/Runtime/TradePetRuntime.cs:1914), [worker:141](python/tradepet_mt5_worker.py:141).
- Once an annual import completes, it is not requested again, including the current year; a new worker looks back only seven days.
- Trigger: current-year import complete, assistant stopped for over seven days while trades occur. Restart cannot recover trades absent from both the old database and recent seven-day window, yet UI may show complete history.
- History is also limited to the current year plus four prior years; “all history” requires this boundary to be explicit.
- Recommendation: account-specific continuous confirmed coverage, overlapping backfill from the last successful point, month/interval coverage rather than permanently closed current years, and explicit date-range resync.
- Acceptance: returning after 20 days matches continuous execution sets; current-year, cross-year and corrected history can resync.

### F04 · P1 · Reconnect lacks a full recovery phase and may replay alerts [partial offline reproduction]

- Locations: [Runtime:538](src/TradePet.App/Runtime/TradePetRuntime.cs:538), [625](src/TradePet.App/Runtime/TradePetRuntime.cs:625), [666](src/TradePet.App/Runtime/TradePetRuntime.cs:666), [worker:148](python/tradepet_mt5_worker.py:148).
- Disconnect suppresses only the next floating-loss alert without resetting position/execution baselines. The first reconnect frame compares against stale state: offline entries, additions and stop changes may appear current; execution batches can replay old close feedback.
- Offline account A→B change: `connect()` overwrites the account before `emit_snapshot()` detects it, retaining A's seen tickets/history queue. Ticket collisions suppress B data and old jobs contaminate B sync.
- Worker crash/watchdog restart mainly emits diagnostics, without unified disconnect/session generation; the app may still regard old history requests as in flight.
- Recommendation: `Disconnected → Recovering → Live`, unified account reset, reconcile/baseline before enabling alerts, fresh process generation and redispatch unconfirmed jobs.
- Acceptance: offline entries/closes/stop edits, account switches and crashes backfill data without replaying old alerts.

### F05 · P1 · Bridge identity is unchecked and server dates never expire [static confirmation]

- Locations: [BridgePipeServer](src/TradePet.Infrastructure/Mt5/BridgePipeServer.cs:11), [Runtime:480](src/TradePet.App/Runtime/TradePetRuntime.cs:480), [1083](src/TradePet.App/Runtime/TradePetRuntime.cs:1083), [MQL5:198](mt5/TradePetBridge.mq5:198).
- Terminals compete for one pipe. Heartbeats directly replace date, offset and chart ID without checking selected terminal/account; object events also lack account validation.
- Reverse loss-zone commands carry account/date, but the EA checks only kind/revision. Wrong-terminal connections can supply another account's time, plans or drawings.
- After disconnect, `_serverDateAuthoritative` remains true and only heartbeats advance the date, potentially retaining yesterday across midnight. Without any bridge connection, worker-inferred offsets do not enable much daily/review processing.
- Recommendation: bidirectional normalized terminal/account/session handshake; time sample, confidence and expiry, with elapsed-time day-boundary progression and explicit invalid state instead of indefinite reuse.
- Acceptance: two terminals, EA on the wrong terminal, account switch and disconnected midnight never mix target data/drawings with other sources.

### F06 · P1 · Entry risk depends on snapshot ordering [static confirmation]

- Locations: [worker:283](python/tradepet_mt5_worker.py:283), [Runtime:625](src/TradePet.App/Runtime/TradePetRuntime.cs:625), [770](src/TradePet.App/Runtime/TradePetRuntime.cs:770), [720](src/TradePet.App/Runtime/TradePetRuntime.cs:720).
- Worker sends positions before executions; entry rules run only for snapshot `Opened` differences. Trades opening/closing between snapshots have no opening difference.
- A new snapshot may arrive before the preceding loss's close execution, so `_trades` lacks the loss and cooldown, size-escalation/revenge checks miss it. Later execution arrival does not reevaluate the entry.
- Recommendation: identity-bearing domain events from execution sequences; snapshots reconcile/value. Entry evaluation needs a consistent execution watermark, stable event IDs for wait/recheck deduplication, and live-late versus recovery distinction.
- Acceptance: intra-sample open/close, immediate reentry after loss, and both snapshot/execution orderings produce identical final facts.

### F07 · P1 (netting) · Reversal projection is incorrect [offline reproduction]

- Locations: [TradeProjector:39](src/TradePet.Core/Trading/TradeProjector.cs:39), [Models:63](src/TradePet.Core/Domain/Models.cs:63).
- `InOut` treats an entire deal as exit when a position exists instead of splitting old-position closure and new-direction remainder.
- Buy 1 lot then sell 2 should leave short 1. Current output closes the buy with zero remainder. Netting reversals retain `POSITION_IDENTIFIER`; changed position IDs cannot avoid this. [MT5 position properties](https://www.mql5.com/en/docs/constants/tradingconstants/positionproperties).
- Collected `MarginMode` does not select projection/support rules. Hedging-only support must explicitly reject/identify netting rather than silently output incorrect statistics.
- Recommendation: signed volume and reversal splitting; define position lifecycle versus directional segments and adjust trade IDs, metadata, loss-zone attempts and alert keys without duplicate fees.
- Acceptance: reversals, repeated reversals, reversals after partial reduction and hedging regression reconcile segments/current volume.

### F08 · P1 · Maximum count/lot settings do not execute rules [static; lot case reproduced]

- Locations: [Models:133](src/TradePet.Core/Domain/Models.cs:133), [Runtime:265](src/TradePet.App/Runtime/TradePetRuntime.cs:265), [DailyStateCalculator](src/TradePet.Core/Trading/DailyStateCalculator.cs:7).
- `MaximumTrades`/`MaximumLot` exist in settings/storage/UI, but repository search found no consuming rule.
- Maximum lot 0.01 with an actual 1-lot position produces no limit fact.
- `StopLossReminderEnabled/Seconds` and `MissingStopLoss` are contracts only, without persistent no-stop monitoring. A removed-stop alert is different.
- Recommendation: define count as entries/complete trades/orders and size as individual/same-direction/total exposure, then implement threshold crossings/deduplication. Remain read-only; alerts do not prevent MT5 orders.
- Acceptance: equal/above threshold, same-direction aggregation, additions, day reset, disabled rules and restart recovery.

### F09 · P2 · Hardcoded price zones do not generalize across symbols [offline reproduction]

- Locations: [BehaviorAnalyticsCalculator:578](src/TradePet.Core/Trading/BehaviorAnalyticsCalculator.cs:578), [MainViewModel:29](src/TradePet.App/ViewModels/MainViewModel.cs:29).
- Buckets round to one decimal; global loss tolerance defaults to 2 with input minimum 0.01, ignoring symbol point/tick size/configuration.
- EURUSD 1.06, 1.08 and 1.10 all bucket to 1.1, yielding fixation 100 and triggering a rule. Default tolerance 2 creates an enormous forex zone.
- Recommendation: symbol specifications and tolerance policy in points/ticks, shared by behavior/loss zones; show units/coverage for unknown symbols.
- Acceptance: gold, EURUSD, JPY pairs and differing precision produce consistent judgments for equal tick distances; decimal rounding does not randomly group boundary values.

### F10 · P2 · Plans and outside-plan rate lack an end-to-end workflow [static]

- Locations: [Runtime:1439](src/TradePet.App/Runtime/TradePetRuntime.cs:1439), [1462](src/TradePet.App/Runtime/TradePetRuntime.cs:1462), [BehaviorAnalyticsCalculator:114](src/TradePet.Core/Trading/BehaviorAnalyticsCalculator.cs:114).
- Automatic matching writes only `Matched`; failures skip. No normal UI creates `OutsidePlan/ManualOutside`, leaving the numerator absent and the rate often zero.
- Plans offer creation/list only, without editing, invalidation/archive or per-trade manual classification. Creation validates number parsing, not directional stop/target ordering or reference entry inside the zone.
- Recommendation: separate applicable-plan mismatch from no-plan/insufficient-information; add versioning/invalidation, manual within/outside confirmation and strategy/tag editing. Bind the version valid at entry.
- Acceptance: same-day inside/outside trades, no plan, wrong stop direction, later plan edits and resync after manual corrections.

### F11 · P2 · Historical filters change today's behavior card [static]

- Locations: [MainViewModel:399](src/TradePet.App/ViewModels/MainViewModel.cs:399), [MainWindow:458](src/TradePet.App/Views/MainWindow.xaml:458), [Runtime:1693](src/TradePet.App/Runtime/TradePetRuntime.cs:1693).
- Selected-period `BehaviorRiskText/BehaviorTriggeredText` are reused by today's overview without an independent daily snapshot.
- Querying past severe violations then returning to today can retain historical risk; an empty query can show no rules. The default 30-day window is not today.
- Recommendation: separate `TodayBehavior` and `ReviewBehavior`; today's card uses current account/server day/live facts, and review filters affect review pages only.
- Acceptance: changing review date/symbol/direction cannot alter today's risk card while today's facts stay fixed.

### F12 · P2 · Actual risk multiples lack a producer; total-volume label is incorrect [static]

- Locations: [Runtime:2115](src/TradePet.App/Runtime/TradePetRuntime.cs:2115), [ReviewAnalyticsCalculator:82](src/TradePet.Core/Trading/ReviewAnalyticsCalculator.cs:82), [MainViewModel:329](src/TradePet.App/ViewModels/MainViewModel.cs:329).
- `InitialRiskAmount`/`ActualRiskMultiple` are created null with no later assignment, so more sampling cannot make realized R or MAE/MFE R multiples appear.
- Total volume sums each trade's first entry only, excluding additions/exits and contradicting its label.
- Recommendation: freeze initial stop/reliable account-currency risk at entry, then calculate R; explain unavailable valuations. Distinguish initial-entry, cumulative-entry and two-sided volume.
- Acceptance: with/without stops, changed stops, additions/reductions, currencies and missing FX; initial 1 plus add 1 means initial=1 and cumulative entry=2, not one ambiguous metric.

### F13 · P2 · Full chart snapshots are ignored; reconnect imports may miss objects [static]

- Locations: [MQL5:405](mt5/TradePetBridge.mq5:405), [423](mt5/TradePetBridge.mq5:423), [Runtime:492](src/TradePet.App/Runtime/TradePetRuntime.cs:492).
- EA sends `chart_snapshot` on reconnect, but app switch has no handler; unchanged existing objects send no `chart_upsert`.
- Only tracked/recording objects persist. Restarting TradePet while EA stays active can omit unmoved, unimported objects from memory and current-chart imports.
- Recommendation: terminal/chart-scoped full snapshots form an authoritative atomic baseline before increments. Failed sends must not advance the EA's synchronized state.
- Acceptance: restarting only TradePet still imports all unmoved objects; also test reconnect deletion/send failure.

### F14 · P2 · Isolated replay may persist alerts under the real account [static]

- Locations: [Runtime:358](src/TradePet.App/Runtime/TradePetRuntime.cs:358), [2283](src/TradePet.App/Runtime/TradePetRuntime.cs:2283), [AppDatabase.Review:11](src/TradePet.Infrastructure/Persistence/AppDatabase.Review.cs:11).
- Replay uses `replay|1`, but shared `ShowAlertAsync()` writes `alert_deliveries` under current runtime `_account.Scope.AccountKey`.
- With a real connected account, demo alerts can contaminate real scope despite claiming no real writes. Fixed replay alert IDs also suppress later runs.
- Recommendation: separate in-memory storage/context or explicitly nonpersistent notification output; never implicitly use real account context.
- Acceptance: every real-scope table remains identical before/after replay and every requested replay can display.

## Architectural defects and recommendations

### A01 · P1 · Historical recomputation and live alerts share a congested path

Evidence: [Runtime:430](src/TradePet.App/Runtime/TradePetRuntime.cs:430), [720](src/TradePet.App/Runtime/TradePetRuntime.cs:720), [AppDatabase:214](src/TradePet.Infrastructure/Persistence/AppDatabase.cs:214). Every execution batch reprojects all in-memory executions and upserts all trades, including progress-only/duplicate batches. Each row opens a connection/SQL operation while holding `_stateGate`. Both inbound channels are unbounded.

New-deal costs grow with history; history/disk congestion queues live snapshots/bridge signals and unbounded channels retain stale messages. About four snapshots/second also replace accounts/positions and write daily state. This confirms complexity/queuing, not measured real-disk latency or maximum-account stress results.

Use affected position/trade IDs for incremental projection, batch transactions for history, source-first persistence then projection, and dedicated persistence scheduling. Stale valuation snapshots may coalesce; executions/domain facts must persist reliably. Observe queue length, data age, latency and batch write counts.

`Microsoft.Data.Sqlite` async ADO.NET methods execute synchronously; an async name does not guarantee a responsive UI. Query on background work paths and return immutable results. [Microsoft documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

### A02 · P1 · State ownership and task lifecycles are inconsistent

Evidence: [Runtime:43](src/TradePet.App/Runtime/TradePetRuntime.cs:43), [1433](src/TradePet.App/Runtime/TradePetRuntime.cs:1433), [2383](src/TradePet.App/Runtime/TradePetRuntime.cs:2383). Worker/bridge consumers lock state, but plan/settings saves, some reviews, pet life loops and delayed bubble hiding do not share that scheduler.

UI/other tasks may modify `_trades/_planItems` during background enumeration; account switch/query can interleave; low-priority chatter can overwrite critical alerts. Bridge consumer has `finally` but no per-event isolation, so mapping/storage/send failure can terminate it while connected status remains.

Shutdown: [App:77](src/TradePet.App/App.xaml.cs:77) synchronously waits `.GetResult()` for asynchronous disposal on the WPF thread; background work/await continuation may need Dispatcher, enabling deadlock. The user's running app was not forcibly closed to reproduce this.

Use one explicit state scheduler for UI commands, snapshot-based IO/calculation, and account/session generation on result commit. Supervise/cancel/report all background tasks. Perform bounded asynchronous cleanup before closing; OnExit only final synchronous cleanup. Notification service owns priority/display queues.

### A03 · P1 · Processed/completed state has no consistent persistence commit boundary

Evidence: [Runtime:518](src/TradePet.App/Runtime/TradePetRuntime.cs:518), [677](src/TradePet.App/Runtime/TradePetRuntime.cs:677), [697](src/TradePet.App/Runtime/TradePetRuntime.cs:697), [2661](src/TradePet.App/Runtime/TradePetRuntime.cs:2661). Memory sequence/state advances before business processing. `TryPersistLiveAsync()` swallows individual failures/returns false while callers continue; history completion may persist after partial execution-write failure.

Memory can claim synchronized while source deals are missing, causing year skips after restart or projection from incomplete sources. Other direct writes handle the same failure inconsistently: degrade, terminate or unhandled `async void` UI exception. Failed scope loading disables persistence indefinitely without a recovery machine.

Commit source events/deals and checkpoints transactionally. Derived projections may rebuild but retain version/successful watermark. Define notification-record/send failure semantics. Unify failure categories, retries, read-only/in-memory degradation and recovery probes. Show buffered/unpersisted data and history gaps instead of generic diagnostics.

### A04 · P2 · Runtime has too many responsibilities and no testable orchestration boundary

[TradePetRuntime](src/TradePet.App/Runtime/TradePetRuntime.cs:17) was about 2,774 lines, directly creating database, worker, bridge and calculators and owning UI, account, history, chart, plan, alert, sampling and recovery state. It directly uses static Dispatcher, current time and randomness. The two .NET test projects covered Core/Infrastructure, without App-runtime integration tests.

Retain the three-layer foundation and extract incrementally:

| Responsibility | Owned state | Initial issues |
| --- | --- | --- |
| AccountSession / ServerClock | Account, terminal, generation, trusted server time | F04, F05 |
| TradeIngestion / HistorySync | Source deals, coverage, retries, checkpoints | F01, F03, A03 |
| TradeProjection / DailyLedger | Lifecycles, daily cash accounting, incremental projection | F02, F06, F07, A01 |
| RuleEvaluation / Notification | Switches, deduplicated facts, recovery suppression, priorities | F04, F06, F08 |
| PlanService / ReviewQuery | Plans, manual reviews, independent historical queries | F10, F11, F12 |
| Page ViewModels | Immutable snapshots and validated command parameters | F11, A02 |

Interfaces should serve real boundaries—sources, repositories, clocks, notification outputs—not every pure calculator. Integration tests should inject fake MT5, temporary databases and controlled time to run real orchestration without windows/accounts.

## Recommended sequence

The historical implementation recommendation was one TODO item at a time, in dependency order: contracts/types → core → adapters/storage → application → UI → targeted tests/docs.

1. Establish reproducible fault replay with fake sources/database/clock/notifications, without changing accounting; cover failures, reconnect and ordering.
2. Prevent missing/mixed data (F01/F03/F04/F05/A03): query semantics, continuous coverage, session identity and transactional checkpoints. Preserve raw data/migration versions and recheck affected history.
3. Correct numbers/events (F02/F06/F07/F09): daily cash ledger, execution-driven events, netting and symbol specifications. If netting is deferred, restrict support explicitly. Historical projection rebuilds stay silent.
4. Improve responsiveness/shutdown (A01/A02): incremental/batch processing, queue control, state scheduling, supervision and asynchronous exit. Synthetic large histories verify new-event cost no longer grows linearly with total history.
5. Complete existing workflows (F08/F10/F11/F13/F14): thresholds, plan editing/classification, separate today/review state, full chart sync and isolated replay.
6. Add advanced metrics last (F12): reliable initial risk, R and correctly named volume. Before implementation, identify unavailable features rather than calling everything low coverage.

Invariants: no orders/closes/modifications; account/terminal isolation; no historical alert replay; empty differs from failure; event time differs from receipt time; source deals replay and versioned projections rebuild; historical queries never change live risk state. Bridge drawings are permitted chart side effects and must be a separate protocol capability.

## Offline reproductions

| Scenario | Input/fault | Actual output at review time |
| --- | --- | --- |
| Partial close / lot cap | Open 2, close 1 for +100, remainder floating 0; cap 0.01 | Realized 0, giveback 100, no limit fact |
| Netting reversal | Buy In 1; Sell InOut 2; same position ID | RemainingVolume=0, IsComplete=true, Side=Buy |
| Forex buckets | EURUSD entries 1.06/1.08/1.10 | PriceFixationScore=100, Triggered=true |
| Failed positions | Mock positions_get() returns None | emit_snapshot()=true, positions=[] |
| Failed history | history_deals_get() continuously returns None | Incremental cursor advances; annual isComplete=true, sourceCount=0 |
| Offline account switch | A seen tickets/history queue; switch to B and reconnect/snapshot | A tickets/jobs remain |

The first three loaded the newly built `TradePet.Core.dll` from PowerShell and called real domain classes. The last three used project FakeApi and `unittest.mock.patch`, replacing only MT5 query returns. No real MT5 collection/trading functions or new business tests were used. Reproduction assertions should become formal regression tests during implementation.

## Acceptance boundaries and synchronization record

- Checked: existing Core/persistence/Python tests, WPF compilation, six synthetic counterexamples, key call chains and external API return contracts.
- Not measured live: simultaneous terminals, real bridge disconnect across days, triggered WPF shutdown deadlock, database lock contention and large-history end-to-end latency. These are source-supported risks, not live incident reports.
- Changes during the original review: this report and historical `TODO.md` review status only; business code/real account data unchanged.
- Next historical recommendation: injectable collection/recovery integration boundary, then F01 first. The original body recorded these fixes as not yet implemented; the dated status note above records subsequent implementation.

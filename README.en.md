# TradePet

English | [简体中文](README.md#简体中文)

> Trade alongside you, never on your behalf.

**Current version: 1.0.0-rc.10** · [Download Windows portable release](https://github.com/cz1978/tradepet/releases/tag/v1.0.0-rc.10) · [Installation and upgrades](INSTALL.en.md) · [Changelog](CHANGELOG.en.md) · [MIT License](LICENSE)

`rc.10` keeps behavior input in pet popups: quick entry reasons and reviews include optional execution/emotion choices, alongside skipped-opportunity capture and seven automatically observed improvement goals. It aligns review completion and statistical scope across reports and calendars, highlights the selected exit reason, and groups the pet menu. The Windows package includes .NET, isolated Python, MT4/MT5 read-only plugins and bilingual documentation.

TradePet is a local Windows trading desktop pet. It connects to MT5 or MT4 in read-only mode and brings positions, risk, quick notes, reviews, macroeconomic events and daily reports into a desktop assistant.

It does not place, close or modify orders. It focuses on easily overlooked questions: did you follow your plan, repeatedly lose in the same zone, give back profits, and what did you do well today?

## Core features

### Chinese and English

Select **UI language** in Settings or the initial setup wizard, save, exit and restart to switch between 简体中文 and English. Pages, menus, pet messages, field descriptions and newly generated reports use the selected language. Raw trade data, handwritten notes and existing reports retain their original content. UI language does not change numeric input or accounting rules.

### Read-only connections

- Reads MT5 accounts, positions, orders, executions, market data and symbol specifications.
- The MQL5 bridge transmits chart objects, trade changes and MT5's built-in economic calendar without calling trading functions.
- MT4 uses a local MQL4 plugin for accounts, positions, pending orders, order history and charts. No Python, DLL imports or live-trading permission is required.
- Trading data stays local by default; no cloud account or third-party data key is required.

### Position overview beside your pet

- Starts in the lower-right corner; adjust scale, opacity, position lock and mouse passthrough.
- A floating position card shows live P/L. Collapse or hide it and restore it through the pet menu.
- New entries and closes show entry-reason and quick-review cards without stealing focus. History sync does not trigger cards. Both prompts default on, can be disabled independently, and remain manually accessible.
- Select or write an entry reason. Quick reviews use actual executions and reliable position samples and allow quick records and corrections. Longer handwritten reviews remain optional.
- Check GitHub updates automatically or manually. A new version is announced once, links to Releases and is never installed automatically.

### Intraday risk and discipline

- Tracks realized/floating P/L, intraday high-water marks and profit giveback.
- Daily profit goals accept amounts or percentages. Percentages use the day's baseline balance and retain the basis.
- Alerts cover daily entry counts, same-symbol/same-direction size limits, missing stops and cooldowns.
- Import entry, stop, target and combined-risk plans from MT5 chart objects.
- Alerts provide facts and review clues without changing positions.

### Loss-zone memory

Complete losing trades become traceable price zones with entry counts, cumulative losses and current-price context. Near a repeatedly losing zone, the pet provides context instead of an isolated red number.

### Complete review workspace

- Six areas: Overview, Calendar and journal, Trade archive, Statistical analysis, Strategy and improvement, Opportunities.
- Each trade retains raw executions, before/during/after-session notes, ratings, behavior tags, attachments and actual bar/tick replay.
- The archive offers All trades and Saved reviews; saved records are independent of query dates. Entry reasons are stored by account/position and exported.
- Saved filters, AND/OR tags, review queues, batch classification and rereview states.
- Complete trades, cash flows and equity use distinct accounting. Fees, realized R, MAE/MFE and coverage are separate; missing data is never zero.
- Versioned strategy rules and improvement goals prevent later edits from rewriting history.

### Daily reports

- Schedule reports using the current broker's trading-server time.
- Summarizes realized net P/L, fees, win rate, best/worst trades, review progress, alerts, timelines and macro events.
- Actual M5 candles provide symbol context and coverage. Missing data is explained rather than inferred from P/L. Unlinked plans are not violations; missing strategy/setup labels do not create classification conclusions.
- Overview and actions, Trades and risk, and Full Markdown tabs include symbol/direction/entry-hour/strategy breakdowns, holding times, initial risk, realized R, reliable MAE/MFE/giveback and a checklist linked to specific records.
- Separates complete-trade from daily cash P/L, and closed-trade cumulative drawdown from account-equity drawdown. Cash-flow effects are checked separately. Missing/unreliable data stays explicit; an alert association is not treated as causation.
- Automatically archives Markdown; save elsewhere or copy into an AI tool.

Default archive:

```text
%LOCALAPPDATA%\TradePet\reports\<account>\<yyyy-MM-dd>-trading-report.md
```

### MT5 macroeconomic calendar

- MT5's built-in country/currency, importance, previous, forecast and actual values.
- Times converted to the current broker's trading server.
- High-importance reminders 30 and 5 minutes before events and after release.
- Simultaneous events grouped with a total count instead of keeping only the first.

## Installation and first connection

The [illustrated guide](docs/images/tradepet-rc6-install-guide.png) shows extraction, bridge installation and chart attachment. Its screenshots use Chinese; [English instructions](INSTALL.en.md) describe the same actions. Python is bundled; the bridge still needs installation and manual chart attachment.

Download **`TradePet-1.0.0-rc.10-win-x64.zip`**, extract the entire ZIP and run `TradePet.exe`. GitHub **Source code** archives are not application installers. Do not move the EXE alone.

The package includes .NET 8, 64-bit Python 3.13, MetaTrader5/NumPy, read-only MT4/MT5 plugins and documentation. No separate Python or dependency download is needed. `rc.5` and earlier did not bundle Python; upgrading is recommended. MT4 needs no Python. See [installation, upgrades, checksums and troubleshooting](INSTALL.en.md).

### Platform support (rc.10)

| Feature | MT5 | MT4 |
| --- | --- | --- |
| Accounts, positions, pending orders, floating-loss monitoring | Supported | Supported |
| History, reviews, loss zones | Hedging accounts | Persistent archive; partial closes grouped only with broker ticket-link evidence |
| Chart plans and daily reports | Some features require bridge | Updated bridge required |
| Economic calendar | MT5 built-in | Forex Factory weekly calendar, advance reminders; no live actuals |
| Historical replay | Candles/ticks, depending on terminal coverage | M5 and local actual ticks; uncollected ticks not backfilled |
| Python | Bundled 64-bit Python 3.13 and dependencies | Not required |

Connect one terminal at a time. Missing MT4 history shows “—”, never zero. See Account modes and accounting below for MT5 netting/exchange limitations.

### Requirements

- Windows 10/11 x64.
- MT5 or MT4 with the corresponding read-only bridge; capabilities differ as above.
- Full source builds require .NET 8 SDK, MT4/MT5 MetaEditor and Python with pip. The build downloads official Python 3.13 x64 and pinned dependencies; end users need no separate Python.

### Build from source

```powershell
git clone https://github.com/cz1978/tradepet.git
cd tradepet
.\scripts\build-release.ps1
```

Output: `artifacts\TradePet-win-x64`, self-contained Windows x64 with `Runtime\python-runtime`. The script verifies Python's SHA256 and dependency/collector imports. Packaging failures stop the build.

MT5 MetaEditor defaults to `C:\Program Files\WeTrade MetaTrader 5 Terminal\MetaEditor64.exe`; MT4 is discovered under Program Files. Override using `-Mt5MetaEditorPath`, `-Mt4MetaEditorPath`, `-PythonPath`; select output with `-OutputDirectory`. See [build details](INSTALL.en.md#build-from-source).

### Initial wizard

First launch, including the first upgrade from older releases, opens four steps: platform/terminal, connection preparation, status, pet preferences. Set up later does not mark completion and the wizard returns next launch. Successful saving stops automatic display. Reopen through Settings → Open setup wizard.

1. Start/log in to the terminal. Select it or browse to `terminal64.exe` (MT5) / `terminal.exe` (MT4), one terminal at a time.
2. MT5: Check Python to verify the bundle. Development/old releases need Python and dependency repair. MT4 skips this.
3. Install / update read-only plugin; refresh Expert Advisors in Navigator; drag `TradePet / TradePetBridge` onto one chart and keep it open. MT4 needs no DLL/live-trading permission.
4. Check account/plugin status, choose preferences and save. Restart after switching platform/terminal or repairing Python. Saved settings do not prove connection.

MT4 needs no Python, API key, DLL or trading permission. Reattach the updated plugin after upgrades and select All history. Disconnect, removal or no live update for over 10 seconds marks data stale. Imported orders persist across reduced history scope and restarts. Partial closes use validated broker `from #ticket` / `to #ticket` comments and order attributes, retain original tickets and each close's fees, and become complete only after full closure. Without reliable links, orders remain independent; equal price/time is not guessed evidence. Unread history still depends on terminal scope. During a closed market on first connection, set verified `InpServerUtcOffsetMinutes` or wait for quote calibration.

Dates/replay use broker time. Raw MT4 times persist in terminal `MQL4/Files/TradePet/order-archive.db` and `tick-archive.db` and are reconverted after offset changes. Preserve both when moving terminals; review-backup ZIPs exclude these collector archives. Current-offset UTC conversion does not establish historical DST offsets, so cross-DST holding duration/external UTC alignment remain limited. Forex/CFD risk uses contract size, P/L currency and conversion quotes; base-currency accounts convert at stop price. Futures/older bridges use broker tick value. Missing parameters stay unknown; R requires reliable entry samples.

MT4 tick replay requires assistant and updated plugin running. `OnTick` collects the attached symbol; other open-chart/position symbols are sampled every second. Times have second precision; offline, pre-collection and overflow gaps are not invented and coverage remains partial. This differs from MT5 native historical tick downloads and the MT4 public calendar differs from MT5's built-in calendar.

For a .NET-only build using MT4, first run `scripts\build-mt4-bridge.ps1`, optionally with `-MetaEditorPath`. The full release script does this automatically.

## Data, privacy and backups

- Database, rolling logs, reports and attachments: `%LOCALAPPDATA%\TradePet`.
- Isolation by platform, broker server, login and server day.
- Public ZIP hides account keys/local paths. Full backups include SQLite, attachments and SHA-256 manifest.
- Period MD exports all queried filtered results and states dates/count. Daily/full MD retain local server/account information; inspect before sharing or use public ZIP. Screenshots are not automatically redacted; terminal names use local directories.
- Restore validates paths, hashes, database integrity and attachment references.
- Cache cleanup never deletes handwritten notes, screenshots, strategies or goals.

## Account modes and accounting

Complete-trade projection, loss zones and individual reviews target MT5 hedging (`ACCOUNT_MARGIN_MODE_RETAIL_HEDGING`). Netting/exchange accounts retain positions, floating P/L and account-risk monitoring, explicitly marked position monitoring only rather than generating misleading complete-trade statistics.

Missing data is never zero. Realized R, for example, requires a stop at entry and reliable initial-risk valuation through MT5.

## Development and validation

```powershell
dotnet test TradePet.sln --configuration Release
.\.venv\Scripts\python.exe -m unittest discover -s python -p "test_*.py" -v
dotnet run --project tools\TradePet.ReviewBenchmark\TradePet.ReviewBenchmark.csproj --configuration Release -- artifacts\review-benchmark.json
.\scripts\build-bridge.ps1
```

## Project status

An evolving personal tool, primarily verified on Windows x64, WeTrade MT5 and hedging accounts. Report other broker/build/account compatibility issues through GitHub Issues.

## License

[MIT License](LICENSE), with a [Chinese translation](LICENSE.zh-CN.md), `Copyright (c) 2026 cz1978`. Use, modification, commercial use and redistribution, including closed-source or paid versions, are allowed with copyright and permission notices retained. The full LICENSE governs. Provided as is, without warranties.

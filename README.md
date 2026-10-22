# TradePet

[English](#english) | [简体中文](#简体中文)

## English

> Trade alongside you, never on your behalf.

**Current version: 1.1.0 (stable)** · [Download Windows portable release](https://github.com/cz1978/tradepet/releases/tag/v1.1.0) · [Installation and upgrades](INSTALL.en.md) · [Changelog](CHANGELOG.en.md) · [MIT License](LICENSE)

`1.1.0` adds entry screenshots, optional pet interactions and sounds, interactive multi-timeframe replay, and sampled holding P/L analysis. It also includes offline account archives, portfolio risk checks, plan execution evidence, validated automatic backups and an updated SQLite engine. Windows packages bundle .NET, Python, both read-only bridges and bilingual documentation.

TradePet is a local Windows trading desktop pet. It connects to MT5 or MT4 in read-only mode and brings positions, risk, quick notes, reviews, macroeconomic events and daily reports into a desktop assistant.

It does not place, close or modify orders. It focuses on easily overlooked questions: did you follow your plan, repeatedly lose in the same zone, give back profits, and what did you do well today?

### 1.1.0 updates

- Paste or import entry screenshots and review them with the trade. Reason prompts remain optional and default off.
- Independently disable tap responses, save/review feedback, ambient animations or sounds. Sounds default off; quiet mode suppresses playful interactions.
- Replay supports zoom, pan, crosshair/OHLC, M1/M5/M15/H1 linked charts and entry/exit/event navigation. MT4 requires the updated bridge; missing market data is never synthesized.
- View sampled net/realized P/L, observed peaks/troughs and giveback, with reliable excursion summaries by strategy and server entry hour. Sampling gaps stay explicit; final results remain hidden until close unless revealed.
- Local source updates display their build time in the console title and Settings, record the running assembly, and repair shortcut targets, working directories, icons and launch arguments on every update.

- Priority-ordered reminders, per-position stop-alert delivery, and snoozed alerts revalidated against the account, positions and current risk.
- Complete trade plans can be entered or composed from one entry, stop-reference and target-reference chart object for the same symbol. Saved prices are frozen. Entry-range matching is separate from evidence-based stop, target and exit checks.
- Graphical replay displays actual candles, quotes, executions and stop/target levels on a synchronized timeline without filling gaps or exposing future prices.
- Open Offline account archive from the pet menu or review page to query, edit and replay cached data by account and date without starting or switching terminals.
- Overview displays account-currency stop-risk lower bounds, estimated drawdown to stops, margin fields and data completeness. Missing stops/valuations are unknown. MT4 margin fields require reattaching the updated bridge.
- Goal notification switches control goal reminders. Automatic targets assess observed-opportunity adherence (0–100%); unknown samples are not passes, and end conditions require manual review.
- Automatic daily migration backups are validated and retain the latest seven copies; disable in Settings. Packages include the database, attachments, reports and managed MT4 raw order/tick archives, with existing hash/path/integrity checks and rollback on restore.

### Core features

#### Chinese and English

Select **UI language** in Settings or the initial setup wizard, save, exit and restart to switch between 简体中文 and English. Pages, menus, pet messages, field descriptions and newly generated reports use the selected language. Raw trade data, handwritten notes and existing reports retain their original content. UI language does not change numeric input or accounting rules.

#### Read-only connections

- Reads MT5 accounts, positions, orders, executions, market data and symbol specifications.
- The MQL5 bridge transmits chart objects, trade changes and MT5's built-in economic calendar without calling trading functions.
- MT4 uses a local MQL4 plugin for accounts, positions, pending orders, order history and charts. No Python, DLL imports or live-trading permission is required.
- Trading data stays local by default; no cloud account or third-party data key is required.

#### Position overview beside your pet

- Starts in the lower-right corner; adjust scale, opacity, position lock and mouse passthrough.
- A floating position card shows live P/L. Collapse or hide it and restore it through the pet menu.
- Entry-reason and quick-review cards are available on demand without stealing focus. History sync does not trigger cards. Both automatic prompts default off and remain available under Quick note; enable either one in Settings if desired. Skip all clears the current account’s queue for that card type; new trades can still prompt, and skipped records do not become completed reviews.
- Select or write an entry reason. Quick reviews use actual executions and reliable position samples and allow quick records and corrections. Longer handwritten reviews remain optional.
- Check GitHub updates automatically or manually. A new version is announced once, links to Releases and is never installed automatically.

#### Intraday risk and discipline

- Tracks realized/floating P/L, intraday high-water marks and profit giveback.
- Daily profit goals accept amounts or percentages. Percentages use the day's baseline balance and retain the basis.
- Alerts cover daily entry counts, same-symbol/same-direction size limits, missing stops and cooldowns.
- Import entry, stop, target and combined-risk plans from MT5 chart objects.
- Alerts provide facts and review clues without changing positions.

#### Loss-zone memory

Complete losing trades become traceable price zones with entry counts, cumulative losses and current-price context. Near a repeatedly losing zone, the pet provides context instead of an isolated red number.

#### Complete review workspace

- Six areas: Overview, Calendar and journal, Trade archive, Statistical analysis, Strategy and improvement, Opportunities.
- Each trade retains raw executions, before/during/after-session notes, ratings, behavior tags, attachments and actual bar/tick replay.
- The archive offers All trades and Saved reviews; saved records are independent of query dates. Entry reasons are stored by account/position and exported.
- Saved filters, AND/OR tags, review queues, batch classification and rereview states.
- Complete trades, cash flows and equity use distinct accounting. Fees, realized R, MAE/MFE and coverage are separate; missing data is never zero.
- Versioned strategy rules and improvement goals prevent later edits from rewriting history.

#### Daily reports

- Schedule reports using the current broker's trading-server time.
- Summarizes realized net P/L, fees, win rate, best/worst trades, alerts, timelines and macro events. After trading ends, open Fill daily summary, confirm the generated facts and select or write one next action, then save. Daily-summary completion is shown separately from optional individual trade reviews.
- Actual M5 candles provide symbol context and coverage. Missing data is explained rather than inferred from P/L. Unlinked plans are not violations; missing strategy/setup labels do not create classification conclusions.
- Overview and actions, Trades and risk, and Full Markdown tabs include symbol/direction/entry-hour/strategy breakdowns, holding times, initial risk, realized R, reliable MAE/MFE/giveback and a checklist linked to specific records.
- Separates complete-trade from daily cash P/L, and closed-trade cumulative drawdown from account-equity drawdown. Cash-flow effects are checked separately. Missing/unreliable data stays explicit; an alert association is not treated as causation.
- Automatically archives Markdown; save elsewhere or copy into an AI tool.

Default archive:

```text
%LOCALAPPDATA%\TradePet\attachments\_system\reports\<account>\<yyyy-MM-dd>-trading-report.md
```

#### MT5 macroeconomic calendar

- MT5's built-in country/currency, importance, previous, forecast and actual values.
- Times converted to the current broker's trading server.
- High-importance reminders 30 and 5 minutes before events and after release.
- Simultaneous events grouped with a total count instead of keeping only the first.

### Installation and first connection

The [illustrated guide](docs/images/tradepet-rc6-install-guide.png) shows extraction, bridge installation and chart attachment. Its screenshots use Chinese; [English instructions](INSTALL.en.md) describe the same actions. Python is bundled; the bridge still needs installation and manual chart attachment.

Download **`TradePet-1.1.0-win-x64.zip`**, extract the entire ZIP and run `TradePet.exe`. GitHub **Source code** archives are not application installers. Do not move the EXE alone.

The package includes .NET 8, 64-bit Python 3.13, MetaTrader5/NumPy, read-only MT4/MT5 plugins and documentation. No separate Python or dependency download is needed. `rc.5` and earlier did not bundle Python; upgrading is recommended. MT4 needs no Python. See [installation, upgrades, checksums and troubleshooting](INSTALL.en.md).

#### Platform support (1.1.0)

| Feature | MT5 | MT4 |
| --- | --- | --- |
| Accounts, positions, pending orders, floating-loss monitoring | Supported | Supported |
| History, reviews, loss zones | Hedging accounts | Persistent archive; partial closes grouped only with broker ticket-link evidence |
| Chart plans and daily reports | Some features require bridge | Updated bridge required |
| Economic calendar | MT5 built-in | Forex Factory weekly calendar, advance reminders; no live actuals |
| Historical replay | Candles/ticks, depending on terminal coverage | M1/M5/M15/H1 candles and local actual ticks; uncollected ticks not backfilled |
| Python | Bundled 64-bit Python 3.13 and dependencies | Not required |

Connect one terminal at a time. Missing MT4 history shows “—”, never zero. See Account modes and accounting below for MT5 netting/exchange limitations.

#### Requirements

- Windows 10/11 x64.
- MT5 or MT4 with the corresponding read-only bridge; capabilities differ as above.
- Full source builds require .NET 8 SDK, MT4/MT5 MetaEditor and Python with pip. The build downloads official Python 3.13 x64 and pinned dependencies; end users need no separate Python.

#### Build from source

```powershell
git clone https://github.com/cz1978/tradepet.git
cd tradepet
.\scripts\build-release.ps1
```

Output: `artifacts\TradePet-win-x64`, self-contained Windows x64 with `Runtime\python-runtime`. The script verifies Python's SHA256 and dependency/collector imports. Packaging failures stop the build.

MT5 MetaEditor defaults to `C:\Program Files\WeTrade MetaTrader 5 Terminal\MetaEditor64.exe`; MT4 is discovered under Program Files. Override using `-Mt5MetaEditorPath`, `-Mt4MetaEditorPath`, `-PythonPath`; select output with `-OutputDirectory`. See [build details](INSTALL.en.md#build-from-source).

#### Initial wizard

First launch, including the first upgrade from older releases, opens four steps: platform/terminal, connection preparation, status, pet preferences. Set up later does not mark completion and the wizard returns next launch. Successful saving stops automatic display. Reopen through Settings → Open setup wizard.

1. Start/log in to the terminal. Select it or browse to `terminal64.exe` (MT5) / `terminal.exe` (MT4), one terminal at a time.
2. MT5: Check Python to verify the bundle. Development/old releases need Python and dependency repair. MT4 skips this.
3. Install / update read-only plugin; refresh Expert Advisors in Navigator; drag `TradePet / TradePetBridge` onto one chart and keep it open. MT4 needs no DLL/live-trading permission.
4. Check account/plugin status, choose preferences and save. Restart after switching platform/terminal or repairing Python. Saved settings do not prove connection.

MT4 needs no Python, API key, DLL or trading permission. Reattach the updated plugin after upgrades and select All history. Disconnect, removal or no live update for over 10 seconds marks data stale. Imported orders persist across reduced history scope and restarts. Partial closes use validated broker `from #ticket` / `to #ticket` comments and order attributes, retain original tickets and each close's fees, and become complete only after full closure. Without reliable links, orders remain independent; equal price/time is not guessed evidence. Unread history still depends on terminal scope. During a closed market on first connection, set verified `InpServerUtcOffsetMinutes` or wait for quote calibration.

Dates/replay use broker time. Legacy terminal `MQL4/Files/TradePet/order-archive.db` and `tick-archive.db` are imported read-only into `%LOCALAPPDATA%\TradePet\attachments\_system\collectors`. Raw timestamps remain isolated by account and are reconverted after offset changes. New migration backups include managed archives and restore them without moving terminal databases manually. Older backups may omit these files; legacy terminal files are retained. Current-offset UTC conversion does not establish historical DST offsets, so cross-DST holding duration/external UTC alignment remain limited. Forex/CFD risk uses contract size, P/L currency and conversion quotes; base-currency accounts convert at stop price. Futures/older bridges use broker tick value. Missing parameters stay unknown; R requires reliable entry samples.

MT4 tick replay requires assistant and updated plugin running. `OnTick` collects the attached symbol; other open-chart/position symbols are sampled every second. Times have second precision; offline, pre-collection and overflow gaps are not invented and coverage remains partial. This differs from MT5 native historical tick downloads and the MT4 public calendar differs from MT5's built-in calendar.

For a .NET-only build using MT4, first run `scripts\build-mt4-bridge.ps1`, optionally with `-MetaEditorPath`. The full release script does this automatically.

### Data, privacy and backups

- Database, rolling logs, reports and attachments: `%LOCALAPPDATA%\TradePet`.
- Isolation by platform, broker server, login and server day.
- Public ZIP hides account keys/local paths. Full backups include SQLite, attachments and SHA-256 manifest.
- Period MD exports all queried filtered results and states dates/count. Daily/full MD retain local server/account information; inspect before sharing or use public ZIP. Screenshots are not automatically redacted; terminal names use local directories.
- Restore validates paths, hashes, database integrity and attachment references.
- Cache cleanup never deletes handwritten notes, screenshots, strategies or goals.

### Account modes and accounting

Complete-trade projection, loss zones and individual reviews target MT5 hedging (`ACCOUNT_MARGIN_MODE_RETAIL_HEDGING`). Netting/exchange accounts retain positions, floating P/L and account-risk monitoring, explicitly marked position monitoring only rather than generating misleading complete-trade statistics.

Missing data is never zero. Realized R, for example, requires a stop at entry and reliable initial-risk valuation through MT5.

### Development and validation

```powershell
dotnet test TradePet.sln --configuration Release
.\.venv\Scripts\python.exe -m unittest discover -s python -p "test_*.py" -v
dotnet run --project tools\TradePet.ReviewBenchmark\TradePet.ReviewBenchmark.csproj --configuration Release -- artifacts\review-benchmark.json
.\scripts\build-bridge.ps1
```

### Project status

An evolving personal tool, primarily verified on Windows x64, WeTrade MT5 and hedging accounts. Report other broker/build/account compatibility issues through GitHub Issues.

### License

[MIT License](LICENSE), with a [Chinese translation](LICENSE.zh-CN.md), `Copyright (c) 2026 cz1978`. Use, modification, commercial use and redistribution, including closed-source or paid versions, are allowed with copyright and permission notices retained. The full LICENSE governs. Provided as is, without warranties.

---

## 简体中文

> 陪你交易，不替你交易。

**当前版本：1.1.0（正式版）** · [下载 Windows 便携版](https://github.com/cz1978/tradepet/releases/tag/v1.1.0) · [安装与升级说明](INSTALL.md) · [更新日志](CHANGELOG.md) · [MIT 许可证](LICENSE)

`1.1.0` 新增入场截图、可独立关闭的桌宠互动与声音、多周期交互回放和持仓盈亏过程分析，同时包含离线账户档案、组合风险检查、计划执行证据、自动校验备份及 SQLite 引擎更新。Windows 完整包内置 .NET、Python、MT4/MT5 只读插件和中英说明。

TradePet 是一款面向 Windows 的本地交易桌宠。它以只读方式连接 MT5 或 MT4，把持仓、风险、快速记录、复盘、宏观事件和每日报告收进桌面助手里。

TradePet 不下单、不平仓、不改单。它关心的是交易者最容易忽略的事：是否按计划执行、是否重复在同一区域亏损、盈利是否正在回吐，以及今天到底做对了什么。

### 1.1.0 更新

- 入场打卡支持粘贴或导入截图，并在交易复盘中查看；理由弹窗仍可选，默认关闭。
- 点击回应、记录反馈、日常动作与声音均可独立关闭；声音默认关闭，“安静陪我”暂停趣味互动。
- 回放支持缩放、拖动、十字光标/OHLC、M1/M5/M15/H1 同步双图及入场/离场/事件跳转；MT4 需重新挂载新版桥接，缺失行情不补造。
- 展示采样净盈亏、已实现盈亏、观测峰谷与盈利回吐，并按策略和服务器入场小时汇总可靠样本。采样缺口明确标出；平仓前默认隐藏最终结果。
- 本机源码更新在控制台标题和设置中显示构建时间，日志记录实际加载程序集；每次更新修复快捷方式目标、工作目录、图标与启动参数。

- 提醒按优先级排队；无止损提醒逐笔确认投递，延后提醒重新校验账户、持仓和当前风险。
- 交易计划页可手工建立完整计划，或将同品种唯一的入场、止损参考、目标参考对象组合为草稿。保存后冻结价格；“自动入场区匹配”不代表止损或退出已合规，交易详情分项显示执行证据。
- 图形回放展示真实 K 线、报价、成交点及止损/目标线，与时间游标同步；不补造行情缺口，不提前显示未来价格。
- 宠物菜单或复盘页可打开“离线账户档案”，按本地账户和日期查询、编辑及回放缓存，无需启动或切换终端。
- 总览增加账户币种的止损风险已知下限、从当前盈亏到止损的估计回撤、保证金字段和数据检查；缺止损或缺估值不按零处理。MT4 保证金字段需要新版桥接插件重新挂图。
- 改进目标的提醒开关参与执行；自动目标值为已知机会执行率（0–100%），未知样本不计通过，观察期和人工结束条件分别显示。
- 默认每日自动创建并校验一份迁移备份，保留最近 7 份，可在设置中关闭。备份含数据库、附件、日报以及助手管理的 MT4 原始订单/Tick 存档；恢复沿用路径、哈希、完整性检查及事务回滚。

### 核心特点

#### 中英双语

在设置中心或首次设置向导中选择“界面语言”，保存后退出并重新启动即可切换简体中文 / English。页面、菜单、桌宠提示、字段说明及新生成报告使用所选语言；原始交易数据、手写笔记和已有报告保持原文。数字输入与交易统计口径不随界面语言变化。

#### 只读连接，不触碰交易权限

- 只读取 MT5 账户、持仓、订单、成交历史、行情和品种规格。
- MQL5 Bridge 用于传递图表对象、交易变化和 MT5 内置经济日历，不调用交易函数。
- MT4 通过本地 MQL4 插件读取账户、持仓、挂单、订单历史与图表，不需要 Python、DLL 或实盘交易权限。
- 所有交易数据默认留在本机，无需云端账号或第三方数据密钥。

#### 桌宠式持仓快览

- 桌宠默认停在桌面右下角，可调整大小、透明度、固定和鼠标穿透。
- 浮动持仓卡展示实时盈亏；支持折叠、隐藏，并可从宠物右键菜单恢复。
- 入场原因与快速复盘可在宠物旁按需打开，不抢焦点；历史补同步不弹出。两个自动提示默认关闭，从宠物菜单“记一下”手动打开，也可在设置中心分别开启。“全部跳过”清掉当前账户对应类型的待提示队列，新交易仍可提示，跳过不计为已复盘。
- 入场原因可点选或补写；快速复盘展示实际成交和可靠持仓采样，支持快捷记录与修正。手写长复盘保留为可选入口。
- 设置中心可自动或手动检查 GitHub 新版本。同一新版自动提醒一次，下载入口指向项目发布页，不自动安装更新。

#### 盘中风险与纪律提醒

- 跟踪已实现盈亏、浮动盈亏、日内高水位和利润回吐。
- 每日盈利目标可选金额或百分比；百分比按当日基准余额折算，保留计算基数。
- 支持每日开仓次数、同品种同方向仓位上限、缺失止损和冷静期提醒。
- 可从 MT5 图表对象导入入场、止损、目标与组合风险计划。
- 所有提醒只提供事实和复盘线索，不会自动改变持仓。

#### 亏损区域记忆

TradePet 会把完整亏损交易投影为可追溯的价格区域，记录进入次数、累计损失和当前价格位置。当交易者再次接近曾经反复亏损的地方时，桌宠会给出上下文提醒，而不是只显示一个红色数字。

#### 完整的交易复盘工作台

- 总览、日历与日记、交易档案、统计分析、策略与改进、机会记录六个入口。
- 每笔交易保留原始成交、盘前/盘中/盘后笔记、执行评价、行为标签、附件和真实 bars/ticks 回放。
- 交易档案包含“全部交易”和“已保存复盘”；已保存复盘不受当前查询日期范围限制。入场原因随对应账户和持仓保存，并进入导出报告。
- 支持保存筛选、多标签 AND/OR、复盘队列、批量归类和需重审状态。
- 区分完整交易、现金流和净值口径；将费用、实际 R、MAE/MFE 与数据覆盖度分开展示，不用缺失数据补零。
- 策略规则和改进目标保留版本，避免日后修改规则时篡改历史。

#### 每日交易报告

- 可设定每日自动生成和归档时间，时间基于当前 broker 交易服务器；默认不弹窗，可在设置中开启。
- 汇总已实现净盈亏、费用、胜率、最佳/最差交易、行为提醒、时间线和宏观事件。当日交易结束后，点“填写日总结”，确认自动生成的事实，选择或填写一条下次行动并保存；日总结状态与可选的逐笔复盘分别显示。
- 按关联品种提供实际 M1/M5/M15/H1 K 线的行情背景与覆盖范围；缺失行情明确说明，不凭交易盈亏推断走势。未绑定计划不当作违规，人工策略/形态缺失不生成分类结论。
- 日报窗口可直接查看概览与建议、逐笔交易与风险、完整 Markdown；支持品种/方向/入场小时/策略盈亏拆分、持仓时长、初始风险与实际 R、可靠采样下的 MAE/MFE 与回吐，以及关联具体记录的下一交易日行动清单。
- 区分完整交易盈亏与当日现金盈亏、已平仓累计曲线回撤与账户净值回撤；出入金影响单独校验，缺失或不可靠数据明确标记，不补零、不把提醒关联当作亏损原因。
- 自动归档为 Markdown，可另存、留档或一键复制给 AI 做进一步分析。

默认归档位置：

```text
%LOCALAPPDATA%\TradePet\attachments\_system\reports\<account>\<yyyy-MM-dd>-trading-report.md
```

#### MT5 宏观日历

- 直接使用 MT5 内置经济日历，展示国家/货币、重要度、前值、预期和公布值。
- 时间统一换算为当前 broker 交易服务器时间。
- 高重要度事件在 30 分钟和 5 分钟前提醒，数据公布后再提醒。
- 同一时段多条事件会合并展示并标明总数，不会只保留第一条。

### 安装与首次连接

**[查看图文安装教程（含 Bridge 安装与挂图）](docs/images/tradepet-rc6-install-guide.png)**。Python 已内置，Bridge 仍需点击安装，再手动拖到交易终端的图表上运行。

普通用户请下载发布页中的 **`TradePet-1.1.0-win-x64.zip`**，完整解压后运行 `TradePet.exe`。不要下载 GitHub 自动生成的 `Source code` 源码包作为安装包，也不要单独移动 EXE。

`1.1.0` 便携包已包含 .NET 8、64 位 Python 3.13、MetaTrader5/NumPy 依赖、MT4/MT5 只读插件和安装文档，无需另装 Python 或联网下载依赖。旧版 `rc.5` 及更早版本未包含 Python，建议升级；MT4 无需 Python。首次配置、升级保留数据、校验安装包和排障步骤见 [INSTALL.md](INSTALL.md)。

#### 平台支持范围（1.1.0）

| 功能 | MT5 | MT4 |
| --- | --- | --- |
| 账户、持仓、挂单、浮亏监控 | 支持 | 支持 |
| 成交历史、交易复盘、亏损区域 | 对冲账户支持 | 支持订单持久存档；有 broker 票号关联证据的部分平仓按整笔持仓归集 |
| 图表计划、每日交易报告 | 支持，部分功能需要桥接插件 | 支持，需要新版桥接插件 |
| 经济日历 | MT5 内置日历 | Forex Factory 公开周历及事前提醒，不提供实时公布值 |
| 历史行情回放 | K 线 / Tick，取决于终端历史覆盖 | M1/M5/M15/H1 K 线及本地实际报价 Tick 回放；未采集的 Tick 不回补 |
| Python | 已内置 64 位 Python 3.13 及依赖 | 不需要 |

每次连接一个终端。MT4 缺少历史数据的指标显示“—”，不会用零代替；MT5 净额或交易所账户的限制见下文“账户模式与统计口径”。

#### 运行环境

- Windows 10/11 x64
- MetaTrader 5，或 MetaTrader 4（需挂载对应的只读桥接插件，能力差异见上表）
- 从源码构建完整发布包时需要 .NET 8 SDK、MT4 和 MT5 自带的 MetaEditor，以及带 pip 的 Python。构建时联网下载并打包官方 Python 3.13 x64 和固定版本依赖；用户运行便携包无需另装 Python

#### 从源码构建

```powershell
git clone https://github.com/cz1978/tradepet.git
cd tradepet
.\scripts\build-release.ps1
```

发布目录为 `artifacts\TradePet-win-x64`。该目录是自包含的 Windows x64 便携版，内含 `Runtime\python-runtime`，无需依赖用户电脑上的 Python。构建脚本会验证 Python 下载文件的 SHA256，并检查内置依赖和采集模块能否加载；打包失败会终止构建。

MT5 编译器默认路径为 `C:\Program Files\WeTrade MetaTrader 5 Terminal\MetaEditor64.exe`，MT4 编译器会从 Program Files 自动查找。其他安装位置可传 `-Mt5MetaEditorPath`、`-Mt4MetaEditorPath`、`-PythonPath`，也可用 `-OutputDirectory` 指定发布目录；完整命令见 [源码构建说明](INSTALL.md#从源码构建)。

#### 首次设置向导

首次启动（以及旧版本首次升级）会打开四步设置向导：选择 MT4 / MT5 和终端、准备连接、检查状态、设置桌宠偏好。选择“稍后设置”不会标为完成，下次启动继续提示；成功保存后不再自动弹出。随时可从“设置中心 → 打开设置向导”重新进入。

1. 先启动交易终端并登录账户，在向导中选择它；未自动识别时可以浏览 `terminal64.exe`（MT5）或 `terminal.exe`（MT4）。每次只连接一个终端。
2. MT5 用户点击“检测 Python”确认内置环境就绪。源码开发环境或旧发布包仍需安装 Python 并修复依赖。MT4 跳过此步骤。
3. 点击“安装 / 更新只读插件”，在对应终端导航器中刷新 EA 列表，把 `TradePet / TradePetBridge` 拖到一个图表，并保持该图表打开。MT4 插件无需 DLL 或实盘交易权限，只挂一个图表。
4. 检查账户与插件状态，选择桌宠偏好并保存。更换平台、终端或修复 Python 后，需要退出并重新启动 TradePet；保存设置本身不代表连接成功。

MT4 使用本地插件，无需 Python、API 密钥、DLL 或实盘交易权限；升级后需重新挂载新版插件，并在“账户历史”中选择“全部历史”。断线、插件被移除或实时数据超过 10 秒未更新时标记过期。已读取订单持续存档，终端缩小历史范围或重启助手不会删掉旧记录。部分平仓根据 broker 注释中的 `from #票号` / `to #票号` 和订单属性校验关联，归入最初持仓，全部平完才计为完整交易；原始票号及各次平仓费用保留。缺少可靠关联时保持独立并提示数据口径，不根据相同价格或时间猜测合并。尚未读到的历史仍依赖终端加载范围。首次连接休市且无已记录偏移时，可在插件参数 `InpServerUtcOffsetMinutes` 填写经纪商当前 UTC 偏移分钟数，或等待新报价自动校时。

日期归属、复盘和回放时间以 broker 服务器为准。旧版终端 `MQL4/Files/TradePet/order-archive.db`、`tick-archive.db` 会只读导入 `%LOCALAPPDATA%\TradePet\attachments\_system\collectors`。原始订单与报价按账户隔离、偏移变化后重新换算；新版迁移备份包含托管存档，恢复后无需手工搬运终端数据库。旧版备份可能不含这两份存档，原终端文件不会被删除。历史 UTC 换算不代表已知历史夏令时偏移；跨夏令时的实际持仓时长及外部 UTC 对齐仍有限制。外汇/CFD 风险金额优先按合约大小、盈亏币种和转换报价计算，账户币种为外汇基础币时按止损价折算；期货和旧桥接插件使用 broker 的 tick 价值。必要参数缺失时显示未知，仅有可靠开仓采样时参与 R 分析。

MT4 Tick 回放读取实际留存的报价，需同时运行助手和新版插件：挂图品种通过 `OnTick` 采集，其他打开图表和持仓品种每秒采样。数据只有秒级时间，不伪造毫秒精度；离线、采集前或缓冲溢出的区间不补造，覆盖状态保持“部分”。因此该能力不能宣称与 MT5 原生历史 Tick 下载完全相同。经济日历继续使用公开周历，与 MT5 内置日历存在差异。

从源码仅构建 .NET 项目时，如需使用 MT4，请先运行 `scripts\build-mt4-bridge.ps1`（可传 `-MetaEditorPath` 指定 MT4 MetaEditor）；完整发布脚本会自动编译并打包 MT4 插件。

### 数据、隐私与备份

- 数据库、滚动日志、日报和复盘附件保存在 `%LOCALAPPDATA%\TradePet`。
- 业务数据按平台、券商服务器、登录账号和服务器交易日隔离。
- 公开分享 ZIP 会隐藏账户键和本机路径；完整本地备份包含 SQLite、附件和 SHA-256 清单。
- 周期 MD 导出“复盘分析”中已查询的全部筛选结果，显示日期范围与总笔数；普通日报和完整 MD 保留服务器、账号等本地信息，向外分享前应核对内容或使用公开分享 ZIP。截图不会自动脱敏，终端名称取自本机安装目录。
- 恢复前会检查路径、文件哈希、数据库完整性和附件引用。
- 行情缓存可单独清理，不会删除人工笔记、截图、策略或改进目标。

### 账户模式与统计口径

完整交易投影、亏损区域和逐笔复盘目前面向 MT5 对冲账户（`ACCOUNT_MARGIN_MODE_RETAIL_HEDGING`）。净额和交易所账户仍可使用持仓、账户浮盈亏及账户级风险监控，界面会明确标记“仅监控持仓”，不生成可能失真的完整交易统计。

统计不会把缺失数据当成零：例如，实际 R 只统计开仓时已有止损且 MT5 能可靠估值初始风险的交易。

### 开发与验证

```powershell
dotnet test TradePet.sln --configuration Release
.\.venv\Scripts\python.exe -m unittest discover -s python -p "test_*.py" -v
dotnet run --project tools\TradePet.ReviewBenchmark\TradePet.ReviewBenchmark.csproj --configuration Release -- artifacts\review-benchmark.json
.\scripts\build-bridge.ps1
```

### 项目状态

TradePet 目前是持续迭代中的个人工具，主要在 Windows x64、WeTrade MT5 和对冲账户上验证。使用其他券商、MT5 构建或账户模式时，欢迎通过 GitHub Issues 反馈兼容性问题。

### 许可证

TradePet 采用 [MIT License](LICENSE)，提供[中文译文](LICENSE.zh-CN.md)，版权署名为 `Copyright (c) 2026 cz1978`。允许使用、修改、商用和再分发（包括闭源或收费版本），但须保留版权声明与许可声明；完整条款以 `LICENSE` 为准。软件按原样提供，不附带保证。

第三方依赖、运行时及第三方素材仍遵循各自的许可证，不因本项目采用 MIT 而改变。

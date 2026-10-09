# TradePet installation and upgrades

English | [简体中文](INSTALL.md)

For **1.0.0-rc.11 / Windows 10 or 11 x64**. Supports MT5 and MT4, including archived MT4 orders, evidence-based partial-close grouping and actual-quote replay. TradePet does not place, close or modify orders.

`rc.11` adds Skip all to entry and exit cards and lets you save a daily summary directly in the report after confirming the generated facts and one next action. Individual trade reviews remain optional. Exit execution and emotional-state reports are stored separately from entry and legacy whole-trade reports, including a No preset exit rules choice. The complete Windows package retains bundled runtimes, read-only MT4/MT5 bridges and bilingual documentation.

`rc.5` and earlier require manual Python setup; the complete `rc.11` portable package is recommended.

## Illustrated quick setup

**Python is bundled; the bridge still needs installation and chart attachment.** Install / update read-only plugin copies files only. Refresh Navigator in your terminal, drag `TradePetBridge` onto a chart without another EA and confirm.

![Download/extract, select terminal, install bridge, refresh Navigator, attach to chart, save and confirm connection](docs/images/tradepet-rc6-install-guide.png)

[Open full-resolution illustration](docs/images/tradepet-rc6-install-guide.png). Screenshots use Chinese and illustrate the workflow; terminal menu names vary by language. The following English instructions include copyable paths.

## Download and launch

1. Open [GitHub Releases](https://github.com/cz1978/tradepet/releases/tag/v1.0.0-rc.11).
2. Download `TradePet-1.0.0-rc.11-win-x64.zip` under Assets. Source code archives are for development, not runnable installers.
3. Extract the **entire ZIP**, for example to `D:\Apps\TradePet-1.0.0-rc.11`.
4. Run `TradePet.exe` and follow the four-step wizard. Do not run inside the ZIP or copy only the EXE.

Use `TradePet.exe --review` to open Review analysis directly. Exit any older running instance from its tray menu first.

.NET 8, Python and collector dependencies are bundled; no SDK or Python installation is needed. Keep the DLLs, `Runtime` and `Assets` beside the EXE.

Compare the ZIP's hash with the release's `SHA256SUMS.txt`:

```powershell
Get-FileHash .\TradePet-1.0.0-rc.11-win-x64.zip -Algorithm SHA256
```

This checks file consistency, not a digital signature. The package is currently unsigned. Verify source/hash when Windows prompts; do not disable system protection.

TradePet's bundled `LICENSE` is MIT: personal/commercial use, modification and distribution, including paid redistribution, are allowed if copyright/permission notices remain. Third-party components retain their own licenses. `rc.3` changed only licensing, docs and version compared with `rc.2`; connection steps are the same.

## Connect MT5

### 1. Prepare terminal and Python

- Install/start MetaTrader 5 and log in to the account to monitor.
- `rc.11` bundles **64-bit Python 3.13** and dependencies. `rc.5` and older require a separate 64-bit Python 3.13.
- Select MT5 and its `terminal64.exe` in the wizard; use Browse terminal if not detected.

Connect one terminal at a time. Complete reviews require an MT5 hedging account; netting/exchange accounts provide position and account-risk monitoring.

### 2. Prepare collection

Click Check Python. The portable package prioritizes `Runtime\python-runtime\python.exe`, needs no dependency downloads and avoids older user environments. Keep all of `Runtime`; if the check fails, exit and extract the complete ZIP into a fresh directory. Python licensing is in `Runtime\python-runtime\LICENSE.txt`; component licenses are under `Lib\site-packages`.

Only old releases/development environments need manual setup: detect Python, repair missing dependencies or select an installed Python 3.13 `python.exe`. This downloads pinned packages from `Runtime\python\requirements.txt` and requires a network.

Repair uses `%LOCALAPPDATA%\TradePet\python\venv`. Alternatively, run from the extracted directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Runtime\setup-python.ps1 -PythonPath 'C:\YourPath\Python313\python.exe'
```

Replace the example path with your actual Python installation.

### 3. Install the read-only bridge

1. Click Install / update read-only plugin.
2. In the selected MT5, press `Ctrl+N` for Navigator and right-click Expert Advisors → Refresh.
3. Expand `TradePet`, drag `TradePetBridge` onto a chart without another EA, keep default parameters and confirm. Keep terminal/chart open. DLL imports and trading permission are unnecessary.
4. Check connection status. Chart objects and the economic calendar require a connected bridge.

Files go to the selected terminal's data directory under `MQL5\Experts\TradePet`. If absent, use File → Open Data Folder and verify it is the same terminal.

Manual fallback: create that folder if needed, copy `Runtime\TradePetBridge.ex5`, then refresh and attach. “Bridge installed; awaiting chart attachment” means installation alone is insufficient.

### 4. Save and verify

Choose pet preferences and Save and finish setup. Exit/restart through the pet/tray menu after changing platform/terminal or repairing Python. Saving only confirms persisted configuration, not connection.

Initial history synchronization takes time; statistics may be incomplete until it finishes. Use connection, bridge and history status to assess progress.

## Connect MT4

1. Start/log in to MT4.
2. Select MT4 and its `terminal.exe`; Python is unnecessary.
3. Install / update read-only plugin.
4. Press `Ctrl+N`, refresh Expert Advisors, then attach `TradePet / TradePetBridge` to **one** chart without another EA. Confirm default parameters and keep the chart open.
5. Check status and save. Restart after changing platform/terminal.

The plugin lives in `MQL4\Experts\TradePet`; local snapshots under `MQL4\Files` carry data. No DLL or Allow live trading is needed. Multiple attached instances can write duplicate snapshots, so use one chart.

Manual fallback: File → Open Data Folder, create `MQL4\Experts\TradePet`, copy `Runtime\mt4\TradePetBridge.ex4`, refresh and attach.

MT4 supports accounts, positions, pending orders, floating losses, order reviews, M5/actual quote replay, chart plans, loss zones and reports. Reattach the updated bridge after upgrades and select All history. Imported orders persist. Validated broker ticket links group partial closes into complete positions only after full closure, retaining original tickets/fees. Unlinked orders stay separate; unread history depends on terminal scope.

Tick replay requires TradePet and the updated bridge running. Attached-symbol ticks and one-second samples for other chart/position symbols retain actual second-level quotes; missing periods are not invented. Terminal `MQL4\Files\TradePet\order-archive.db` and `tick-archive.db` must be copied when migrating the terminal; the assistant's review-backup ZIP excludes them. Historical UTC uses the current offset, so cross-DST holding times are limited. Forex Factory's public weekly calendar requires internet, provides advance reminders but no live actuals. No update for 10 seconds, disconnect or removed plugin marks data stale.

First launch needs a new quote to determine server offset. During closed markets, set `InpServerUtcOffsetMinutes` to the verified broker offset (UTC+3 = 180); default 10000 means automatic. Automatic calibration is retained locally for up to seven days.

## Daily use and quick reviews

- Pet/tray context menus open console, plans, reviews and settings.
- New entries/closes normally show pet-side entry/quick-review cards. Disable each independently. History sync does not replay prompts; Quick review also processes the queue manually.
- Cards do not force topmost or steal focus. Later requeues after ten minutes according to current automatic-prompt preferences.
- The queue is process-local. After exit, historical trades remain in the archive for review; saved reviews persist.
- With no queued trade, Quick review opens Review analysis. Generated reasons/improvements are editable.
- Reopen the wizard in Settings → Open setup wizard.
- Select **UI language** in Settings or the wizard's preferences, save, exit and restart to switch 简体中文 / English. Notes/existing reports retain their original language; newly generated reports use the preference.
- The console's first launch shows a skippable seven-step guide. Reopen through Usage guide; the review guide covers querying, reconstructing trades, analysis and improvement. Hover for field requirements.

## Quick records and update notifications

- Choose/write an entry reason beside the pet and Record reason, or skip. Stored by account/position and visible in the archive.
- Save a pet-side quick review to Review analysis → Trade archive → Saved reviews. Longer handwritten reviews are optional.
- Both prompt switches default on at the top of Settings. Save preferences; historical sync does not trigger cards. Manual access remains when disabled.
- GitHub Releases is checked on launch and every six hours; a new release is announced once. Check updates/Open release page work manually. Only Windows portable releases qualify. No automatic download/install or transmission of accounts, positions or reviews.
- Daily/full MD retains server/account data. Inspect before sharing or use public ZIP. Screenshots are not automatically redacted; terminal labels use local directory names.

## Upgrade from an older version

1. Save edits and choose Exit TradePet in pet/tray menus. Closing the console may only hide it.
2. After exit, back up all of `%LOCALAPPDATA%\TradePet`, including database, attachments, settings, logs and reports.
3. Extract the new ZIP into a **new directory** and run its EXE; existing local data is reused.
4. Older upgrades may show setup. Verify platform, terminal and preferences; update/reattach the bridge as needed.
5. Restart after connection changes or Python repair. Verify positions, connection and sync before removing the old application directory.
6. Update desktop/startup shortcuts to the new path. Toggle Start with Windows off/on in the new settings if needed.

Do not delete `%LOCALAPPDATA%\TradePet` to uninstall old binaries; this destroys local data. For rollback, exit the new version and restore its pre-upgrade full-data backup with the old application, avoiding mixed database versions.

## Optional historical database repair

`Runtime\tools\repair_history_database.py` repairs known duplicate/index problems in historical daily states and risk samples. It does not run automatically or replace the app database. Exit, keep a full backup and run against a copy; output must be a new, nonexistent file.

```powershell
.\Runtime\python-runtime\python.exe -X utf8 .\Runtime\tools\repair_history_database.py D:\Backup\tradepet-copy.db D:\Backup\tradepet-repaired.db
```

The original remains. A new database and `.conflicts.json` archive are produced, with integrity, foreign-key and original trade/review checks. Failed checks raise errors. This cannot repair every corruption; do not restore from a copy without reported success.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| No new window | Check tray for an existing instance; exit it before launching the new version. |
| Terminal not found | Start/log in, refresh or browse the actual EXE: MT4 `terminal.exe`, MT5 `terminal64.exe`. |
| Saved terminal missing | Select/save it again; the assistant does not switch silently. |
| MT5 Python check fails | Bundle: exit and fully reextract with `Runtime\python-runtime`. Old/dev: choose 64-bit Python 3.13, repair online and restart. |
| Plugin incomplete | Fully reextract; verify `Runtime\TradePetBridge.ex5` and `Runtime\mt4\TradePetBridge.ex4`. |
| Waiting bridge / stale MT4 | Attach in the selected logged-in/running terminal; use one MT4 chart. |
| MT4 history/report empty | Update/reattach, select All history, wait for quote calibration; verify offset parameter during closed markets. |
| Unwanted prompts | Disable each switch in Settings and save; manual access remains. |

[Report issues](https://github.com/cz1978/tradepet/issues) with app/Windows versions, platform, reproduction steps and redacted log excerpts. Do not upload full databases, accounts or unredacted trade screenshots.

## Build from source

Requires Git, .NET 8 SDK, MT4/MT5 MetaEditor and Python with pip (`-PythonPath` optional). The build downloads official embedded Python 3.13 x64 and dependencies; the resulting portable release needs no user Python.

```powershell
git clone https://github.com/cz1978/tradepet.git
cd tradepet
.\scripts\build-release.ps1 `
  -Mt5MetaEditorPath 'C:\YourMT5\MetaEditor64.exe' `
  -Mt4MetaEditorPath 'C:\YourMT4\metaeditor.exe'
```

Compiles both bridges, publishes .NET and packages/verifies Python. Default output: `artifacts\TradePet-win-x64`. Use `-OutputDirectory 'D:\Builds\TradePet-win-x64'` to avoid overwriting a running instance. `dotnet publish` alone does not create the complete Python package; releases require the script.

Without overrides, MT5 uses `C:\Program Files\WeTrade MetaTrader 5 Terminal\MetaEditor64.exe`; MT4 is discovered under Program Files. Both compilers are required for a complete release even if only one platform is used at runtime.

```powershell
dotnet test TradePet.sln --configuration Release
python -m unittest discover -s python -p "test_*.py" -v
```

Use installed Python 3.13 for collector tests.

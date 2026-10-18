# 本机更新

- 本机正式安装固定在 `artifacts/TradePet-win-x64`，桌面、开始菜单和任务栏快捷方式都指向其中的 `TradePet.exe`。
- 每次修改软件后，在宣告本机更新完成前执行 `scripts/update-local.ps1 -RepairShortcuts`，确认发布成功；每次都必须修复并逐一核对桌面、开始菜单和已有任务栏快捷方式的目标、工作目录和图标，确保指向本次更新后的固定安装目录，不能只在首次更新时执行，也不能只完成编译就宣告更新完成。
- 测试目录和 `src/TradePet.App/bin` 的编译结果只用于验证，不能代替本机安装更新。
- 更新前由代理通过后台命令退出固定安装目录的 TradePet；更新后从该目录后台启动，不带会打开控制台的参数。退出、更新和验证均禁止使用 Computer Use、界面自动化、模拟键鼠或切换焦点；不得操作用户正在交易的 MT4/MT5 窗口。

# GitHub 发布

- 本项目已形成的“推到 / 上传 GitHub”流程包含完整发版：递增版本，更新中英 README、INSTALL、CHANGELOG，验证改动，提交源码并创建对应 Tag，发布正式 Release，上传 Windows x64 完整便携 ZIP 与 `SHA256SUMS.txt`，核对远端与下载文件。
- 已有目标为 `cz1978/tradepet` 的 `origin/main`，默认使用 `vX.Y.Z` 正式版本，Release 的 prerelease 为 false。只有用户明确要求测试版时才使用候选版本并传入 `-Prerelease`；不能自行添加候选版标签，也不能仅推源码后宣告整个上传流程完成。
- 在新的构建目录打包，包含 .NET、Python、采集依赖、MT4/MT5 插件和双语文档；排除交易数据库、账户配置、日志及开发文件。版本、源码提交、Tag 与便携包必须对应。
- 使用 `scripts/build-release.ps1` 构建，准备中英 Release 说明和 ZIP 校验值，再用 `scripts/publish-github-release.ps1` 发布。Git 本地代理失效时仅对本次进程停用，不修改全局代理设置。

## English

The established GitHub upload workflow for this project is a complete release: increment the version, update bilingual README/INSTALL/CHANGELOG, validate changes, commit source, create the matching tag, publish a stable release, and upload the complete Windows x64 portable ZIP plus SHA256SUMS.txt. Verify the remote commit, tag and downloadable assets. The existing destination is cz1978/tradepet on origin/main. Default to vX.Y.Z tags with prerelease set to false. Use candidate versions and the -Prerelease switch only when the user explicitly requests a test release; do not add candidate labels on your own. A source-only push does not finish this workflow. Build in a fresh directory with bundled runtimes, read-only bridges and bilingual documentation; exclude user data and development files. Use build-release.ps1 and publish-github-release.ps1. The stable local installation and shortcut target remain artifacts/TradePet-win-x64. On every local software update, use background commands to exit only the TradePet process from the fixed installation, run scripts/update-local.ps1 -RepairShortcuts, and verify the target, working directory and icon of each desktop, Start Menu and existing taskbar shortcut before claiming completion. Shortcut repair is required every time, not only on the first update. Then launch the updated app in the background from the fixed installation directory without arguments that open the console. Never use Computer Use, UI automation, simulated input or focus changes to exit, update or validate TradePet; never interact with the user's MT4/MT5 trading windows.

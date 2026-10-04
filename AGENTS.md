# 本机更新

- 本机正式安装固定在 `artifacts/TradePet-win-x64`，桌面、开始菜单和任务栏快捷方式都指向其中的 `TradePet.exe`。
- 修改软件后，在宣告本机更新完成前执行 `scripts/update-local.ps1`，确认发布成功。首次修复已有快捷方式时加 `-RepairShortcuts`。
- 测试目录和 `src/TradePet.App/bin` 的编译结果只用于验证，不能代替本机安装更新。
- 更新前正常退出已运行的 TradePet；更新后从固定安装目录启动。

# GitHub 发布

- 本项目已形成的“推到 / 上传 GitHub”流程包含完整发版：递增候选版本，更新中英 README、INSTALL、CHANGELOG，验证改动，提交源码并创建对应 Tag，发布候选 Release，上传 Windows x64 完整便携 ZIP 与 `SHA256SUMS.txt`，核对远端与下载文件。
- 已有目标为 `cz1978/tradepet` 的 `origin/main`，Tag 使用 `v1.0.0-rc.N`，Release 保持 prerelease。不能仅推源码后宣告整个上传流程完成。
- 在新的构建目录打包，包含 .NET、Python、采集依赖、MT4/MT5 插件和双语文档；排除交易数据库、账户配置、日志及开发文件。版本、源码提交、Tag 与便携包必须对应。
- 使用 `scripts/build-release.ps1` 构建，准备中英 Release 说明和 ZIP 校验值，再用 `scripts/publish-github-release.ps1` 发布。Git 本地代理失效时仅对本次进程停用，不修改全局代理设置。

## English

The established GitHub upload workflow for this project is a complete candidate release: increment the version, update bilingual README/INSTALL/CHANGELOG, validate changes, commit source, create the matching tag, publish a prerelease, and upload the complete Windows x64 portable ZIP plus SHA256SUMS.txt. Verify the remote commit, tag and downloadable assets. The existing destination is cz1978/tradepet on origin/main; tags use v1.0.0-rc.N. A source-only push does not finish this workflow. Build in a fresh directory with bundled runtimes, read-only bridges and bilingual documentation; exclude user data and development files. Use build-release.ps1 and publish-github-release.ps1. The stable local installation and shortcut target remain artifacts/TradePet-win-x64; run update-local.ps1 before claiming the local installation was updated, and exit the running app normally first.

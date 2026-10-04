# 本机更新

- 本机正式安装固定在 `artifacts/TradePet-win-x64`，桌面、开始菜单和任务栏快捷方式都指向其中的 `TradePet.exe`。
- 修改软件后，在宣告本机更新完成前执行 `scripts/update-local.ps1`，确认发布成功。首次修复已有快捷方式时加 `-RepairShortcuts`。
- 测试目录和 `src/TradePet.App/bin` 的编译结果只用于验证，不能代替本机安装更新。
- 更新前正常退出已运行的 TradePet；更新后从固定安装目录启动。

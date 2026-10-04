# TradePet 1.0 基线证据

[English](README.md) | 简体中文

本目录保存经过脱敏、可复现的 WP01 证据。先通过 `scripts/create-data-baseline.py` 创建私有数据基线，再运行 `scripts/capture-baseline.ps1`。

- `baseline-manifest.json` 记录产品与契约版本、直接声明的依赖、现有便携产物目录树哈希、私有备份中不敏感的验证字段和外部发布条件。
- `source-files.sha256` 记录已审查源码集合的哈希及仓库相对路径。本目录的生成文件刻意排除在目录树摘要之外，避免自引用。

数据库、账户标识、服务器名称、日志、截图、证书、私钥、工作区绝对路径或备份正文均不应存放在本目录。历史 schema 1–8 样本仍是 WP08 的交付项；缺失情况记录在清单中，不用虚构样本代替。

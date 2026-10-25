---
name: setup-daily-workflow
description: Install or configure this daily-review and desktop-board workflow, select standalone modules, and register the user's local daily schedule when explicitly requested. Use for this package's onboarding or migration.
---

# 配置每日工作流

先读取本技能目录的 `installation.json`。其中 `configPath` 指向本机配置，`components` 表明已安装模块。没有元数据时，使用用户指定位置或当前项目的 `workflow.config.json`；尚未安装则从本包根目录的 `install.ps1` 开始。确认配置前不要读取别人的默认桌面路径。

## 首次安装

- 根据用户意图选择 `review`、`widget` 或 `all`。默认完整安装，保留已选模块、现有账本和配置。Windows执行根目录安装器；其他系统仅安装每日回顾技能和Python脚本，桌面程序要求Windows。
- 所有本机路径、时间和时区写入安装位置的配置，源码只保留示例。不要复制原作者的聊天标识、个人数据、自动化ID或登录凭据。
- 先执行一次已安装的每日回顾流程或用虚构数据刷新视图，确认 `tasks.json`、总览及详情来源一致。没有Python时，可发现本机已提供的Python运行环境；未经用户要求不额外配置外部模型API或购买服务。
- 日程板独立使用时接受 `tasks.json`，无须启动Codex或安装每日回顾模块。完整安装时从同一账本派生视图，保留一份主账本和一份任务总览。

## 注册本机定时任务

只有用户请求启用或迁移定时回顾时才注册。安装文件本身不等于定时任务已启用。

1. 读取 `dailyTime`、`timezone` 和目录配置；缺少偏好时使用配置默认值，并向用户说明。`local` 表示本机时区。周报提交日未知时保留空值。
2. 发现当前环境的任务调度能力，优先使用应用提供的自动化工具。先查同名或同用途任务，已有则更新，避免重复。当前聊天持续回顾使用该环境支持的同聊天任务类型；需要独立项目任务时尊重用户选择。
3. 任务提示词调用 `$daily-review`，带入实际配置路径，要求先读总账、检查可访问新增上下文、区分完成声明和验证证据、更新账本后刷新派生视图、积累科研周报。默认每日05:30，实际时区及计划应以创建工具的返回值核对。
4. 不手写应用内部 `automation.toml`，不移植别人的线程ID。调度工具不可用时，提供已展开的提示词和“在当前聊天每天指定时间执行”的可复制请求；标注“待注册”，不要声称安装器已注册。
5. 启用后用调度工具确认状态和执行位置，再把本机任务身份记到**本机**元数据；不回写公开仓库。不要更改其他自动化或全局配置。

## 完成标准

分别报告：模块文件是否安装、路径是否配置、示例/真实来源是否刷新、日程板是否启动、定时任务是否注册。真实聊天访问受当前工具和授权范围限制；不可访问来源须保留缺口。未经明确要求不发送周报、不发布数据，也不启动开机自启。

迁移和独立使用说明在本包 `docs/PORTABILITY.md`，目录边界在 `docs/DATA-CONTRACT.md`。安装后的副本用 `installation.json` 中的 `installRoot` 定位该 `docs` 目录。

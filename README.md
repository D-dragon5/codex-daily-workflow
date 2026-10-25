# Codex 每日工作流

让每天的任务、科研进展与课程安排持续可见，再把科研部分积累成周报。

这个仓库从实际使用流程整理而来，已经移除原使用者的任务、课表、聊天编号、机器路径和自动化身份。安装示例全部为虚构数据。**复制方法即可开始，复制个人记录则不需要。**

| 选择 | 作用 | 独立使用 |
|---|---|---|
| [每日回顾](modules/daily-review/) | 检查可访问的新交流，维护全部任务总账，安排今日重点，积累科研周报 | 复制一个技能文件夹即可 |
| [桌面日程板](modules/desktop-board/) | Windows 半透明任务面板，支持拖动、置顶、滚轮、尺寸和图标避让 | 读取自己的任务JSON，无须Codex |
| 整套工作流 | 总账 → 任务视图 → 桌面展示 → 定时回顾 | 下列安装器一次配置 |

## 最快开始

下载仓库ZIP并解压，双击 `install.cmd`。默认完整安装：

- 应用位于当前用户的本地应用目录 `CodexDailyWorkflow`。
- 数据位于“文档”中的 `CodexDailyWorkflow`，与应用代码分开。
- 技能位于 `~/.agents/skills/`，已有其他同名技能会停止安装，不强行覆盖。
- 不修改现有自动化、不启动开机自启、不自动打开日程板。成功后用桌面“Codex每日任务板”入口打开。

随后在 Codex 的**本地聊天**发送：

```text
使用 $setup-daily-workflow 完成当前安装的本机配置。
我希望每天本地时间05:30回顾昨天、安排今天，回到这个聊天。
先手动核查一次，再创建或更新同用途定时任务，避免重复。
```

**本地安装和定时任务注册分两步确认结果。** 定时任务只能由本机有相应能力的客户端创建；脚本不直接改写应用内部任务文件。定时任务需要读取本机文件时，电脑和应用须保持运行。[官方定时任务说明](https://learn.chatgpt.com/docs/automations?surface=app)

## 按需安装

Windows PowerShell：

```powershell
# 整套
.\install.ps1 -Component all
# 只装每日回顾
.\install.ps1 -Component review
# 只装桌面日程板
.\install.ps1 -Component widget
```

改安装位置或隔离试用：

```powershell
.\install.ps1 -Component all -InstallRoot .\sandbox\app -DataDir .\sandbox\data -SkillRoot .\sandbox\skills -NoShortcut
```

安装器保留现有配置与主账本。已有手工任务JSON但没有主账本时，会阻止用示例账本覆盖任务；请先导入账本或选择新的数据目录。更新运行中的日程板之前先退出该副本；安装器不强制结束其他实例。

其他系统可以单独复制 `modules/daily-review/skills/daily-review/` 并使用 Python 3.9+；桌面日程板需要 Windows 10/11 和系统 .NET Framework 4.8。安装器不下载或运行第三方安装程序。

## 给 Codex 一句话移植

```text
请阅读 https://github.com/D-dragon5/codex-daily-workflow ，
在一个新目录安装完整工作流，不覆盖我的现有技能、自动化或任务文件。
所有配置按我的本机路径和时区生成，只使用示例数据起步。
安装后先手动验证，再按我确认的时间注册当前聊天的每日回顾。
```

也可以把 `modules/daily-review` 或 `modules/desktop-board` 单独复制给自己的 Codex。两个模块各自带说明和技能，详细接口见 [数据约定](docs/DATA-CONTRACT.md)。

## 在插件目录中选择模块

仓库提供本地插件目录，包含整套、每日回顾、桌面日程板三个入口：

```text
codex plugin marketplace add D-dragon5/codex-daily-workflow
```

在支持的桌面客户端刷新/重新打开插件目录，选择 `Codex Daily Workflow` 来源并安装所需入口。首次运行其设置技能。此仓库发布不等于已上架官方公共插件商店，客户端版本与工作区策略也会影响可用入口。[官方插件目录说明](https://developers.openai.com/plugins/build/plugins)

## 这套流程怎样运行

1. 总账保存长期目标、任务、完成记录与来源，是事实主账。
2. 每日回顾只检查可访问的新上下文，区分意向、计划、完成声明和已验证成果。看不到的工作保留缺口。
3. 刷新脚本从标记任务表生成一份任务JSON、一份Excel总览和一份可定位的网页详情。
4. 日程板读取任务JSON；今日重点和课表配置独立保存，任务完成状态仍由主账派生。
5. 周报提取科研成果、认识、卡点与下一步，课程与个人事务仍在总账内跟踪。

桌面模式当前采用**图标避让**：重叠图标及文件名的区域透明，内容可穿透点击；恢复按钮也避让图标。它不修改系统壁纸，也不承诺所有Windows桌面组合环境中都有同样的背景层效果。

## 数据和边界

- 默认全部本地保存，无遥测、网络上传或独立模型API调用。Codex本身的数据处理遵循你使用的产品设置。
- 聊天读取范围取决于客户端工具、账号和授权。没有工具时，可用手动补记；不能承诺能读取每天的所有聊天。
- 开机启动和定时任务各自独立，均需使用者选择。仅打开日程板不会启动模型。
- 主总账与派生视图保持一套，不按日期不断复制新任务总览。文件占用/刷新失败时保留旧视图并报告原因。
- 发布包只含源码、协议、模板和合成示例；个人数据目录和运行偏好均排除在版本控制之外。

## 开发与检查

```powershell
python -m unittest discover -s modules/daily-review/tests -v
powershell -NoProfile -File modules/desktop-board/tests/test-models.ps1
python tools/audit_release.py .
```

构建及安装检查见 [迁移与验证](docs/PORTABILITY.md)。许可为MIT，桌面模块的开源基础、提交与原始许可完整保留在 [第三方声明](modules/desktop-board/THIRD_PARTY.md)。

# 每日回顾模块

单独使用本模块即可维护全部任务总账、安排今日重点和准备科研周报；桌面日程板为可选模块。

## 单独复制

把 `skills/daily-review/` 整个文件夹复制到自己的 `~/.agents/skills/`，或当前项目的 `.agents/skills/`。辅助脚本、提示词和总账模板都在技能文件夹内，直接复制即可保留依赖。若客户端未显示新技能，重新打开客户端。

在自己的工作目录创建 `workflow.config.json`，按 [数据接口](skills/daily-review/references/data-format.md) 设置路径；复制 [总账模板](skills/daily-review/assets/ledger-template.md) 到配置的 `ledgerPath`。所有示例任务均为虚构内容。

在 Codex 中调用：

```text
使用 $daily-review，读取这里的 workflow.config.json，帮我回顾昨天并安排今天。
```

## 刷新视图

```text
python scripts/refresh.py --config path/to/workflow.config.json
```

需要 Python 3.9 或更高版本，只使用标准库。脚本解析总账中的标记任务表，生成结构化任务、网页和标准电子表格。Excel 文件通过标准压缩包与 XML（可扩展标记语言）生成，这是应用自身的导出功能，不依赖作者机器上的私有工具、Office（办公软件）或第三方 Python 包。

总账是唯一事实来源；派生视图只维护配置指定的一份。成功刷新才覆盖派生数据，文件占用或数据格式错误时保留旧视图。脚本不会修改账本或读取聊天；聊天核查由当前客户端可用工具和用户授权决定。

## 定时回顾

使用 [自动化提示词](skills/daily-review/assets/automation-prompt.md)，在自己的 Codex 聊天中创建每日任务。推荐先手动运行并检查结果，再按自己的时间注册；整套工作流的安装引导可代为调用当前客户端提供的原生工具。不能复制作者的自动化编号或聊天编号来启用任务。

本地文件任务需要电脑及相应客户端保持运行。Web（网页端）环境可用资料及工具不同，不能假定它能直接访问本机文件或所有历史聊天。[官方定时任务说明](https://learn.chatgpt.com/docs/automations?surface=app)

## 开发检查

```text
python -m unittest discover -s tests -v
```

模块的边界和事实处理规则见 [回顾协议](skills/daily-review/references/review-protocol.md)。

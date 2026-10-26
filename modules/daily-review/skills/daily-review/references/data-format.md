# 数据接口

`workflow.config.json` 的路径相对该配置文件所在目录解析，也允许绝对路径：

```json
{
  "schemaVersion": 1,
  "timezone": "local",
  "ledgerPath": "data/每日任务与科研总账.md",
  "tasksPath": "data/tasks.json",
  "overviewPath": "data/任务总览.xlsx",
  "detailsPath": "data/任务详情.html",
  "weeklyReportsDir": "data/weekly-reports",
  "dailyTime": "05:30",
  "weeklySubmissionDay": null
}
```

`timezone` 支持 `local`、`UTC` 和 Python 的时区数据库已提供的 IANA（互联网时区标识）名称。无法解析时区时失败并保留旧视图；Windows 机器缺少数据库时可使用 `local` 或 `UTC`。

总账中仅解析两处标记之间的当前任务表：

```markdown
<!-- tasks:start -->
| ID | 类别 | 任务 | 优先级 | 截止日期 | 状态 | 下一步 | 详细说明 |
|---|---|---|---|---|---|---|---|
| R01 | 科研 | 复现实验基线 | 重要 | 待定 | 进行中 | 确认最小运行入口 | 这是虚构示例，可替换为自己的任务 |
<!-- tasks:end -->
```

ID使用英文字母开头，随后为字母、数字、短横线或下划线；最多64字符，不重复，任务改名时保持编号。日期使用完整`YYYY-MM-DD`，也接受完整明确的“截止：YYYY-MM-DD前”；未知日期填“待定”或留空。“建议2030-04-03”“2030-04-03（待核实）”、相对或多候选日期仅保留说明，机器日期为空，避免变成硬截止。原始截止说明保留在`deadlineNote`。只有明确完整完成状态才归一为“已完成”，原文保留在`progress`；局部完成继续待办。

表格单元格中的竖线写成 `\|`，换行可写 `<br>`。总账其余内容由用户和技能维护，刷新脚本不会更改。状态信息应写进任务表“状态”；历史记录保留原事件和证据。

派生任务接口：

```json
{
  "schemaVersion": 1,
  "updatedAt": "2030-04-01T08:00:00+00:00",
  "timezone": "local",
  "tasks": [
    {"id":"R01","title":"复现实验基线","priority":"重要","category":"科研","due":null,"status":"进行中","next":"确认最小运行入口","progress":"进行中","requirement":"虚构示例说明","deadlineNote":"待定"}
  ]
}
```

`tasks.json`、Excel（电子表格）和网页全部由同一次刷新生成。Excel 有“任务总览”和“任务详情”两个工作表；详情链接为工作簿原生链接，网页链接定位到 `#task-ID`。总览 I 列保留稳定 ID，日期是可计算的数值日期，完成事项不会被判为逾期；日期已过但未完成仅显示状态待核实。

命令：

```text
python scripts/refresh.py --config path/to/workflow.config.json
python scripts/refresh.py --config path/to/workflow.config.json --check
python scripts/refresh.py --config path/to/workflow.config.json --no-xlsx
```

`--check` 只验证配置和总账，不写文件。`--date` 可指定回顾基准日用于回放或测试，默认使用配置时区的当前日期。写出前先验证全部数据并暂存；刷新失败会尝试还原本轮已更新视图，账本始终不被脚本修改。文件被其他程序占用时关掉占用窗口后重试原文件。

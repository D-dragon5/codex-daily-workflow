# 独立桌面日程板

适用于 Windows 10/11 的本地半透明任务面板。无需联网、账号、额外包管理器或付费接口；使用系统的 .NET Framework 4.8 桌面环境。可单独复制此模块，也可由仓库总安装器接入每日回顾流程。其他平台可使用每日回顾技能和 JSON 数据，桌面程序仅支持 Windows。

桌面窗口部分基于 [TanxeStudio 与贡献者维护的 Desktop Tasks Widget](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget) 改造；前台事件、临时置顶及部分窗口状态处理属于直接代码改写。原始MIT版权许可已保留，使用范围和新增内容见 [第三方来源与改动说明](THIRD_PARTY.md)。感谢原作者与贡献者开放代码。

## 单独使用

在本模块目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\DesktopBoard.exe
```

默认构建会在 `dist/` 放入程序、配置及**虚构**任务示例。修改 `dist/data/tasks.json` 即可使用自己的任务。已有配置和示例同名文件不会被构建覆盖。

也可指定自己的配置：

```powershell
.\dist\DesktopBoard.exe --config C:\MyWorkflow\workflow.config.json
```

配置字段见 [examples/workflow.config.json](examples/workflow.config.json)，所有相对路径按**配置文件所在目录**解析。单独使用时，`tasksPath` 指向只读任务JSON；配合每日回顾时，`ledgerPath` 的规范标记任务表直接提供当前任务和完整详情，JSON尚未刷新也能读到新增内容。总账不存在或完全没有标记时保留JSON用法，标记不完整或任务表损坏则报告错误并保留旧显示。缺少Excel时，“完整任务”优先打开网页，再回退到任务JSON。

集成安装时执行 `build.ps1 -OutputFolder <应用目录> -SkipDemo`，随后在可执行文件旁写共享配置，避免创建第二份数据目录。

## 交互

- “允许拖动”开启后，标题和日期整块区域可拖动，包含空白；控制按钮独立。
- 支持置顶、全透明背景、收起、尺寸控制；“靠右”使用当前显示器并保留纵向位置。
- 首屏高度贴合今日重点及已录入的今日课程，其他任务在下方滚动查看。
- 全透明普通模式下，可滚轮范围是可见文字右边界到提示上缘的连续矩形；范围外空白穿透。
- 桌面模式让内容穿透点击，并用公开图标几何将文件图标及文件名冲突区域裁剪透明；“恢复”按钮也避让图标。无法读取图标位置时保持普通模式并说明原因。
- 每15秒检查任务JSON、总账和日程配置变化；不会在后台运行模型或自动完成任务。
- 点击任务直接展开下一步、进展、要求和日期依据。刷新按稳定编号保留展开状态与滚动位置，改名和重名不会串任务。
- 开机启动默认关闭，只有手动开启才写入当前用户启动项，关闭开关可移除本配置对应的启动项。此启动项使用独立名称，多个配置以最后开启的一份为准。

窗口设置默认保存于配置目录的 `显示设置.json`，课程和今日选择在 `日程配置.json`。可通过 `settingsPath`、`plannerPath` 更改位置。互斥锁按配置路径隔离，同一配置只启动一份。

`timezone` 默认 `local`；支持 `UTC`、`Asia/Hong_Kong`、`Asia/Shanghai` 和 Windows 时区 ID。无效值会报错，不静默回退。任务日期以此时区计算；任务 JSON 的 `timezone` 字段仅为来源元数据。

## 数据约定

任务文件采用 `{ "schemaVersion": 1, "tasks": [...] }`。每项必需 `id`、`title`；`due` 为 `yyyy-MM-dd` 或 `null`。其余字段：`priority`、`category`、`status`、`next`、`progress`、`requirement`、`deadlineNote`。样例在 [examples/data/tasks.json](examples/data/tasks.json)。

任务编号须唯一；改标题不会改变编号。空任务列表有效。重复编号、无效日期、缺少数组及无效配置会明确失败，刷新时保留上次成功显示。已完成识别 `已完成`、`completed`、`done`。没有当日手动选择时，面板建议一项科研任务及近期任务，明确标为“建议安排”。

## 验证与限制

无 GUI 的数据检查：

```powershell
powershell -NoProfile -File .\tests\test-models.ps1
```

核对自己配置下的任务数据，可运行`DesktopBoard.exe --check-data <任务JSON路径> --config <配置路径>`。结果以UTF-8结构化文本输出，不启动窗口、不改写设置。结果含自己的任务，保存和排查时放在公开目录之外。

可选 GUI 冒烟检查：

```powershell
Start-Process .\dist\DesktopBoard.exe -ArgumentList '--smoke-test' -Wait
Get-Content .\dist\smoke-result.json
```

冒烟检查只使用给定配置的数据及隔离测试偏好，不创建开机启动项；应先在复制出来的示例目录运行。它验证组件状态和渲染，**不等于真实鼠标、所有桌面主题或所有缩放组合已验收**。真实桌面图标读取和显示效果还需在自己的环境试用。

源码由 [Desktop Tasks Widget](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget) 的窗口逻辑改造，完整归属与许可见 [THIRD_PARTY.md](THIRD_PARTY.md) 和 [LICENSE-UPSTREAM.txt](LICENSE-UPSTREAM.txt)。当前桌面模式使用透明避让，不修改系统壁纸。样例与源码不包含原使用者的任务、课程、路径、聊天记录或账号凭据。

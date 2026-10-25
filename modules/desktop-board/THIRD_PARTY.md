# 第三方来源与改动说明

## 直接代码改写

本模块基于 [Desktop Tasks Widget](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget) 的桌面窗口逻辑开发。项目由 TanxeStudio（GitHub账号 `tanxestudio`）及贡献者维护，许可证为 MIT；使用版本为 [`32322d2e649a7271722a17a4eac44ac9189e1ca0`](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget/tree/32322d2e649a7271722a17a4eac44ac9189e1ca0)。

| 本仓库位置 | 上游来源 | 使用程度 |
|---|---|---|
| `src/NativeDesktop.cs` 的前台事件回调、事件钩子生命周期 | [`Windows/DesktopOverlay.xaml.cs`](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget/blob/32322d2e649a7271722a17a4eac44ac9189e1ca0/Windows/DesktopOverlay.xaml.cs) 中的 `WinEventProc`、加载与关闭处理 | 改写并保留检测桌面前台、临时置顶及还原层级的主要控制逻辑，增加线程调度和关闭状态保护 |
| `src/NativeDesktop.cs` 的最小化消息处理 | 同一上游文件中的 `WndProc` | 改写阻止最小化的条件，将上游锁定状态条件调整为本模块桌面模式条件 |

上游版权行保持为 **Copyright (c) 2025 Desktop Tasks Widget Contributors**。完整版权和许可条款原样保留于 [LICENSE-UPSTREAM.txt](LICENSE-UPSTREAM.txt)，构建时会复制到程序目录。每个独立分发包也包含许可文件。

## 交互与结构参考

穿透样式与窗口状态切换参考上游 `DesktopOverlay.xaml.cs` 的 `ApplyAllStates`，结合本模块的模式状态重新组织。拖动与锁定、独立置顶、托盘菜单、窗口位置保存和可选开机启动的交互设计参考了上游 [`DesktopOverlay.xaml`](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget/blob/32322d2e649a7271722a17a4eac44ac9189e1ca0/Windows/DesktopOverlay.xaml) 和 [`App.xaml.cs`](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget/blob/32322d2e649a7271722a17a4eac44ac9189e1ca0/App.xaml.cs)。当前界面和配置接入已重新实现；本仓库未打包上游的任务编辑器、任务管理窗口或图标绘制助手。

## 本工作流新增及重做部分

- 中文界面、标题与日期整块拖动区、独立拖动锁、按钮完整命中范围。
- 背景透明度、自动贴合今日内容的高度、尺寸控制、当前显示器靠右和缩放坐标处理。
- 当前可见文字宽度的连续矩形滚轮区，以及范围外的空白穿透。
- 桌面图标及文件名矩形的公开几何读取、冲突区域透明裁剪、恢复按钮位置避让和读取失败处理。
- 标准任务 JSON、稳定任务ID、配置相对路径、时区、今日重点和课表配置。
- 每日回顾技能、全部任务总账、科研周报协议、视图导出、安装器、插件目录、合成示例及测试。

这里按实际模块说明归属，不估算无法客观验证的代码百分比。本项目是独立维护的改造工作流，项目名称和来源链接不代表上游对本项目的背书。

## 技术文档参考与感谢

图标几何及多屏处理参考微软的 [无障碍坐标与缩放说明](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-and-screen-scaling)、[线程要求](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues) 和 [窗口坐标说明](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect)。Codex技能、定时任务和插件封装参考 [OpenAI官方技能文档](https://learn.chatgpt.com/docs/build-skills)、[定时任务文档](https://learn.chatgpt.com/docs/automations?surface=app) 与 [插件封装文档](https://developers.openai.com/plugins/build/plugins)。这些资料作为接口与使用方式的参考。

感谢 Desktop Tasks Widget 的原作者与贡献者提供开源基础，也感谢上述项目与文档维护者。本版本的桌面模式采用图标透明避让；开发中曾尝试的其他背景层方案未随当前代码发布，因此未列为本版本的代码依赖。

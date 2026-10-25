# 第三方来源

桌面窗口行为基于 [Desktop Tasks Widget](https://github.com/tanxestudio/Windows-desktop-Tasks-Widget) 改造。上游提交：`32322d2e649a7271722a17a4eac44ac9189e1ca0`，MIT 许可证。

复用、改写了上游 `Windows/DesktopOverlay.xaml.cs` 的前台窗口事件及临时桌面置顶逻辑，保留拖动、置顶、托盘和窗口位置保存的交互设计。上游完整版权和许可条款见 [LICENSE-UPSTREAM.txt](LICENSE-UPSTREAM.txt)。

本地改造包括中文界面、完整标题日期拖动区、拖动锁、独立背景透明度、窗口尺寸控制、当前屏靠右、连续矩形滚轮范围、桌面图标透明避让及恢复按钮。数据接口已改为通用 JSON，移除了原使用者的路径、任务名和任务编号规则。

桌面模式使用公开的 Windows 无障碍接口读取图标矩形，不读取图标名称或文件内容。不会读取其他进程的私有内存，也不会移动图标或更换系统壁纸。

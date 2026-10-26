using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace DailySchedule {
    internal sealed class Board : Window {
        readonly string folder,settingsPath,planPath;
        readonly WorkflowConfig config;
        readonly TimeZoneInfo zone;
        readonly string configFilePath;
        readonly bool testing;
        readonly bool interactiveTesting;
        Preferences prefs;
        PlannerData plan;
        List<TaskInfo> tasks=new List<TaskInfo>();
        Border panel;
        Border headerDragArea;
        StackPanel body,settings;
        TextBlock dateText,sourceText,errorText;
        CheckBox dragToggle,topToggle,startToggle;
        Button collapseButton;
        Button desktopButton,recoveryButton;
        Window recoveryWindow;
        IconGeometry.Result iconSnapshot;
        bool iconReadBusy,desktopModeRequested;
        DateTime iconReadStarted;
        DispatcherTimer iconTimer;
        int iconFailures;
        Slider transparency;
        ScrollViewer scroll;
        Border wheelHitRectangle;
        bool wheelRectangleQueued;
        StackPanel footerContent;
        Border todayBoundary;
        Thumb sizeGrip;
        TextBlock scrollHint;
        readonly List<Expander> taskCards=new List<Expander>();
        readonly HashSet<string> expandedTaskIds=new HashSet<string>(StringComparer.Ordinal);
        Expander otherTasks;
        int renderRevision;
        readonly List<Button> surfaceButtons=new List<Button>();
        Forms.NotifyIcon tray;
        NativeDesktop native;
        DispatcherTimer refresh;
        bool ready,collapsed,applyingSize,closing,syncQueued;
        string loadedSignature="";
        static readonly Brush Text=Brush("#F0F4FA"),Muted=Brush("#B8C5D6"),Blue=Brush("#97C4FD");
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName="CodexDailyWorkflowBoard";
        public DateTime Today {get{return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,zone).Date;}}
        public Board(string root,bool test,bool interactive=false,string configPath=null) {
            folder=root;testing=test||interactive;interactiveTesting=interactive;
            configFilePath=Path.GetFullPath(configPath??Path.Combine(root,"workflow.config.json"));config=WorkflowConfig.Load(configFilePath);zone=config.Zone();
            settingsPath=interactive?Path.Combine(root,"qa-preferences.json"):test?Path.Combine(root,"test-preferences.json"):config.Resolve(config.settingsPath);
            planPath=test?Path.Combine(root,"test-plan.json"):config.Resolve(config.plannerPath);
            prefs=testing?new Preferences():Store.Read(settingsPath,new Preferences());plan=testing?new PlannerData():Store.Read(planPath,new PlannerData());
            Title="Codex桌面日程板";FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
            Width=Math.Max(340,Math.Min(640,prefs.Width));Height=Math.Max(220,Math.Min(SystemParameters.WorkArea.Height-28,prefs.Height));
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            ResizeMode=ResizeMode.NoResize;ShowInTaskbar=interactive;ShowActivated=!testing;
            Topmost=prefs.AlwaysOnTop;
            Build();
            Left=prefs.Left<-90000?SystemParameters.WorkArea.Right-Width-16:prefs.Left;
            Top=prefs.Top<-90000?SystemParameters.WorkArea.Top+16:prefs.Top;
            ClampPosition();
            Loaded+=delegate {
                if(!testing){native=new NativeDesktop(this);SetupTray();}ready=true;
                dragToggle.IsChecked=prefs.AllowDragging;topToggle.IsChecked=prefs.AlwaysOnTop;
                transparency.Value=100-prefs.BackgroundOpacity*100;
                SetCollapsed(prefs.Collapsed);RefreshData(true);
                UpdateResizeGrip();QueueFitToday();SetDesktopMode(prefs.DesktopMode);
                refresh=new DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};
                refresh.Tick+=delegate{RefreshData(false);};refresh.Start();
                if(!testing){iconTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
                iconTimer.Tick+=delegate{if(desktopModeRequested||prefs.DesktopMode){if(iconReadBusy&&iconSnapshot==null&&(DateTime.UtcNow-iconReadStarted).TotalSeconds>4){desktopModeRequested=false;desktopButton.Content="桌面";errorText.Text="桌面图标位置读取超时，保持普通模式。";errorText.Visibility=Visibility.Visible;}else RequestIcons();}};iconTimer.Start();}
                if(test)Dispatcher.BeginInvoke(new Action(SelfTest),DispatcherPriority.ApplicationIdle);
            };
            Closing+=delegate{closing=true;if(refresh!=null)refresh.Stop();if(iconTimer!=null)iconTimer.Stop();SavePreferences();if(recoveryWindow!=null)recoveryWindow.Close();if(native!=null)native.Dispose();if(tray!=null)tray.Dispose();};
            SizeChanged+=delegate{if(ready&&!applyingSize&&prefs.AutoFitToday)QueueFitToday();QueueDesktopControl();QueueWheelRectangle();};
            LocationChanged+=delegate{QueueDesktopControl();};
            IsVisibleChanged+=delegate{QueueDesktopControl();};
            StateChanged+=delegate{if(prefs.DesktopMode&&WindowState==WindowState.Minimized)WindowState=WindowState.Normal;};
        }
        static SolidColorBrush Brush(string hex){return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);}
        static TextBlock Label(string text,double size,Brush color) {
            return new TextBlock{Text=text,FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6)};
        }
        Button Btn(string text,RoutedEventHandler action) {
            var b=new Button{Content=text,Foreground=Text,Background=Brush("#29394D"),BorderThickness=new Thickness(0),Padding=new Thickness(8,6,8,6),MinWidth=36,MinHeight=34,Margin=new Thickness(0,0,4,0),Cursor=Cursors.Hand};
            b.Click+=action;surfaceButtons.Add(b);return b;
        }
        void Build() {
            using(var theme=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Load(theme);
            panel=new Border{CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),BorderBrush=Brush("#50768FAE"),Padding=new Thickness(20,17,20,16)};
            Content=panel;ApplyBackground();
            var grid=new Grid();panel.Child=grid;
            grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new Grid();grid.Children.Add(header);
            header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var dragContents=new StackPanel();headerDragArea=new Border{Child=dragContents,Margin=new Thickness(0,0,8,0),Background=Brushes.Transparent,ToolTip="开启允许拖动后，标题和日期整块都可拖动"};header.Children.Add(headerDragArea);
            headerDragArea.MouseLeftButtonDown+=HeaderDrag;
            var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top};Grid.SetColumn(controls,1);header.Children.Add(controls);
            desktopButton=Btn("桌面",delegate{SetDesktopMode(!(prefs.DesktopMode||desktopModeRequested));});desktopButton.ToolTip="桌面模式：避让图标、锁定并穿透点击";controls.Children.Add(desktopButton);
            controls.Children.Add(Btn("设置",delegate{if(collapsed)SetCollapsed(false);settings.Visibility=settings.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;QueueFitToday();}));
            controls.Children.Add(Btn("大小",delegate{EditSize();}));
            collapseButton=Btn("收起",delegate{SetCollapsed(!collapsed);SavePreferences();});controls.Children.Add(collapseButton);
            controls.Children.Add(Btn("×",delegate{Close();}));
            var title=Label("今日安排",22,Text);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;dragContents.Children.Add(title);
            dateText=Label("",12,Muted);dateText.TextWrapping=TextWrapping.NoWrap;dateText.TextTrimming=TextTrimming.CharacterEllipsis;dragContents.Children.Add(dateText);
            settings=new StackPanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,10)};Grid.SetRow(settings,1);grid.Children.Add(settings);
            var switches=new WrapPanel();settings.Children.Add(switches);
            dragToggle=Toggle("允许拖动",delegate{prefs.AllowDragging=dragToggle.IsChecked==true;UpdateDragArea();SavePreferences();});switches.Children.Add(dragToggle);
            topToggle=Toggle("始终置顶",delegate{prefs.AlwaysOnTop=topToggle.IsChecked==true;Topmost=prefs.AlwaysOnTop&&!prefs.DesktopMode;if(native!=null)native.Reset();SavePreferences();});switches.Children.Add(topToggle);
            startToggle=Toggle("开机启动",delegate{if(ready&&!testing)SetStartup(startToggle.IsChecked==true);});startToggle.IsChecked=!testing&&StartupEnabled();switches.Children.Add(startToggle);
            var opacityRow=new DockPanel{Margin=new Thickness(0,8,0,8)};settings.Children.Add(opacityRow);
            opacityRow.Children.Add(Label("背景透明度",12,Muted));
            transparency=new Slider{Minimum=0,Maximum=100,Width=180,Margin=new Thickness(14,0,0,0),Value=100-prefs.BackgroundOpacity*100};
            transparency.ValueChanged+=delegate{prefs.BackgroundOpacity=1-transparency.Value/100;ApplyBackground();QueueFitToday();SavePreferences();};opacityRow.Children.Add(transparency);
            var row=new WrapPanel();settings.Children.Add(row);
            row.Children.Add(Btn("课表",delegate{EditCourses();}));
            row.Children.Add(Btn("今日选项",delegate{EditFocus();}));
            row.Children.Add(Btn("刷新",delegate{RefreshData(true);}));
            row.Children.Add(Btn("靠右",delegate{SnapRight();}));
            body=new StackPanel();scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,6,0,8)};
            wheelHitRectangle=new Border{Background=new SolidColorBrush(Color.FromArgb(1,19,30,46)),HorizontalAlignment=HorizontalAlignment.Left,Width=0,Margin=new Thickness(0,6,0,0),Visibility=Visibility.Collapsed};
            wheelHitRectangle.AddHandler(Mouse.PreviewMouseWheelEvent,new MouseWheelEventHandler(delegate(object sender,MouseWheelEventArgs e){if(!prefs.DesktopMode&&!collapsed){ScrollWheel(e.Delta);e.Handled=true;}}),true);
            Grid.SetRow(wheelHitRectangle,2);grid.Children.Add(wheelHitRectangle);
            scroll.AddHandler(Mouse.PreviewMouseWheelEvent,new MouseWheelEventHandler(delegate(object sender,MouseWheelEventArgs e){if(!prefs.DesktopMode&&!collapsed){ScrollWheel(e.Delta);e.Handled=true;}}),true);
            Grid.SetRow(scroll,2);grid.Children.Add(scroll);
            var footer=new StackPanel();Grid.SetRow(footer,3);grid.Children.Add(footer);
            errorText=Label("",11,Brush("#FFD395"));errorText.Visibility=Visibility.Collapsed;footer.Children.Add(errorText);
            scrollHint=Label("向下滚动，查看课程和其他任务",11,Muted);footer.Children.Add(scrollHint);
            footerContent=new StackPanel{Margin=new Thickness(0,12,0,6)};
            sourceText=Label("",11,Muted);footerContent.Children.Add(sourceText);
            var links=new WrapPanel();footerContent.Children.Add(links);
            links.Children.Add(Btn("完整任务",delegate{OpenOverview();}));
            links.Children.Add(Btn("总账",delegate{Open(config.Resolve(config.ledgerPath));}));
            scroll.ScrollChanged+=delegate{scrollHint.Text=scroll.VerticalOffset<1?"向下滚动，查看课程和其他任务":"向上滚动，返回今日任务";QueueWheelRectangle();if(interactiveTesting)Store.Write(Path.Combine(folder,"鼠标验证状态.json"),new{offset=scroll.VerticalOffset,viewport=scroll.ViewportHeight,extent=scroll.ExtentHeight,desktopMode=prefs.DesktopMode});};
            sizeGrip=new Thumb{Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Cursor=Cursors.SizeNWSE,ToolTip="拖动调整窗口大小",Background=Brushes.Transparent};
            sizeGrip.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><TextBlock Text='◢' Foreground='#97B2D4' FontSize='14' HorizontalAlignment='Right' VerticalAlignment='Bottom'/></ControlTemplate>");
            Grid.SetRowSpan(sizeGrip,4);grid.Children.Add(sizeGrip);
            sizeGrip.DragDelta+=delegate(object sender,DragDeltaEventArgs e){if(!prefs.AllowResizing||collapsed)return;prefs.AutoFitToday=false;Width=Math.Max(340,Math.Min(640,Width+e.HorizontalChange));Height=Math.Max(220,Math.Min(SystemParameters.WorkArea.Height-28,Height+e.VerticalChange));};
            sizeGrip.DragCompleted+=delegate{SavePreferences();};
            ApplyBackground();
            UpdateDragArea();
        }
        void UpdateDragArea(){if(headerDragArea==null)return;bool enabled=prefs.AllowDragging&&!prefs.DesktopMode;headerDragArea.Background=enabled?new SolidColorBrush(Color.FromArgb(2,19,30,46)):Brushes.Transparent;headerDragArea.Cursor=enabled?Cursors.SizeAll:Cursors.Arrow;}
        void HeaderDrag(object sender,MouseButtonEventArgs e){if(!prefs.AllowDragging||prefs.DesktopMode||e.LeftButton!=MouseButtonState.Pressed)return;e.Handled=true;try{DragMove();SavePreferences();}catch(InvalidOperationException){}}
        void SnapRight(){try{NativeDesktop.AlignRight(this,16);Dispatcher.BeginInvoke(new Action(delegate{if(!closing){SavePreferences();QueueDesktopControl();QueueWheelRectangle();}}),DispatcherPriority.Loaded);}catch(Exception ex){errorText.Text="靠右未完成："+ex.Message;errorText.Visibility=Visibility.Visible;}}
        CheckBox Toggle(string text,RoutedEventHandler action) {
            var c=new CheckBox{Content=text,Foreground=Text,Margin=new Thickness(0,0,13,6),FontSize=12};
            c.Checked+=action;c.Unchecked+=action;return c;
        }
        void ApplyBackground(){
            if(panel==null)return;
            double alpha=Math.Max(0,Math.Min(1,prefs.BackgroundOpacity));bool clear=alpha<.001;
            panel.Background=new SolidColorBrush(Color.FromArgb((byte)(alpha*255),19,30,46));
            panel.BorderThickness=new Thickness(clear?0:1);panel.BorderBrush=clear?Brushes.Transparent:Brush("#50768FAE");
            foreach(var card in taskCards){card.Background=clear?Brushes.Transparent:Brush("#102E4764");var detail=card.Content as StackPanel;if(detail!=null)detail.Background=Brushes.Transparent;}
            foreach(var button in surfaceButtons)button.Background=clear?new SolidColorBrush(Color.FromArgb(2,19,30,46)):Brush("#29394D");
            QueueWheelRectangle();
        }
        void QueueWheelRectangle(){
            if(!ready||closing||wheelHitRectangle==null||wheelRectangleQueued)return;wheelRectangleQueued=true;
            Dispatcher.BeginInvoke(new Action(delegate{wheelRectangleQueued=false;if(!closing)UpdateWheelRectangle();}),DispatcherPriority.Loaded);
        }
        IEnumerable<TextBlock> TextBlocks(DependencyObject root){
            int count=VisualTreeHelper.GetChildrenCount(root);
            for(int i=0;i<count;i++){var child=VisualTreeHelper.GetChild(root,i);var text=child as TextBlock;if(text!=null)yield return text;foreach(var nested in TextBlocks(child))yield return nested;}
        }
        void UpdateWheelRectangle(){
            if(prefs.DesktopMode||collapsed||prefs.BackgroundOpacity>=.001){wheelHitRectangle.Visibility=Visibility.Collapsed;return;}
            UpdateLayout();double right=0;
            var viewport=new Rect(0,0,Math.Max(0,scroll.ActualWidth),Math.Max(0,scroll.ActualHeight));
            foreach(var text in TextBlocks(body)){
                if(!text.IsVisible||text.ActualWidth<=0||text.ActualHeight<=0||string.IsNullOrEmpty(text.Text))continue;
                var rect=text.TransformToAncestor(scroll).TransformBounds(new Rect(0,0,text.ActualWidth,text.ActualHeight));
                if(!rect.IntersectsWith(viewport))continue;
                var measure=new FormattedText(text.Text,CultureInfo.GetCultureInfo("zh-CN"),text.FlowDirection,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,text.Foreground??Text,PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11);
                measure.MaxTextWidth=Math.Max(1,text.ActualWidth);
                double drawn=Math.Min(text.ActualWidth,measure.WidthIncludingTrailingWhitespace);
                double inset=text.TextAlignment==TextAlignment.Right?text.ActualWidth-drawn:text.TextAlignment==TextAlignment.Center?(text.ActualWidth-drawn)/2:0;
                right=Math.Max(right,rect.Left+inset+drawn);
            }
            wheelHitRectangle.Width=Math.Max(0,Math.Min(scroll.ActualWidth,Math.Ceiling(right+4)));
            wheelHitRectangle.Visibility=right>0?Visibility.Visible:Visibility.Collapsed;
        }
        void SetCollapsed(bool value) {
            collapsed=value;prefs.Collapsed=value;collapseButton.Content=value?"展开":"收起";
            scroll.Visibility=value?Visibility.Collapsed:Visibility.Visible;
            scrollHint.Visibility=value?Visibility.Collapsed:Visibility.Visible;
            if(value){if(!prefs.AutoFitToday)prefs.Height=Height;Height=108;settings.Visibility=Visibility.Collapsed;}
            else if(prefs.AutoFitToday)QueueFitToday();
            else Height=Math.Max(220,Math.Min(SystemParameters.WorkArea.Height-28,prefs.Height));
            UpdateResizeGrip();
            QueueWheelRectangle();
        }
        void UpdateResizeGrip(){if(sizeGrip!=null)sizeGrip.Visibility=prefs.AllowResizing&&!collapsed&&!prefs.DesktopMode?Visibility.Visible:Visibility.Collapsed;}
        void ScrollWheel(int delta){if(ready&&!prefs.DesktopMode&&!collapsed){scroll.ScrollToVerticalOffset(scroll.VerticalOffset-delta*.5);}}
        void SetDesktopMode(bool enabled){
            desktopModeRequested=enabled;
            if(enabled&&(iconSnapshot==null||!iconSnapshot.Valid)){
                prefs.DesktopMode=false;Opacity=1;panel.Clip=null;panel.IsHitTestVisible=true;
                if(native!=null)native.SetDesktopMode(false);
                desktopButton.Content="稍候";RequestIcons();return;
            }
            prefs.DesktopMode=enabled;desktopButton.Content=enabled?"恢复":"桌面";
            UpdateDragArea();
            desktopButton.ToolTip=enabled?"恢复普通模式，允许点击和滚动":"桌面模式：置底、锁定并穿透点击";
            Topmost=enabled?false:prefs.AlwaysOnTop;
            if(enabled)settings.Visibility=Visibility.Collapsed;
            panel.IsHitTestVisible=!enabled;UpdateResizeGrip();
            if(native!=null)native.SetDesktopMode(enabled);
            Opacity=1;
            if(!enabled)panel.Clip=null;
            QueueDesktopControl();QueueFitToday();SavePreferences();
            QueueWheelRectangle();
            if(!enabled&&ready&&!testing)Activate();
        }
        void QueueDesktopControl(){
            if(!ready||closing||syncQueued)return;syncQueued=true;
            Dispatcher.BeginInvoke(new Action(delegate{syncQueued=false;SyncDesktopControl();}),DispatcherPriority.Loaded);
        }
        void SyncDesktopControl(){
            if(closing)return;
            if(!prefs.DesktopMode||!IsVisible){if(recoveryWindow!=null)recoveryWindow.Hide();return;}
            ApplyIconMask();
            if(recoveryWindow==null){
                recoveryButton=new Button{Content="恢复",Foreground=Text,Background=Brush("#314965"),BorderThickness=new Thickness(0),Padding=new Thickness(8,6,8,6),MinHeight=34,MinWidth=42,Cursor=Cursors.Hand,ToolTip="点击恢复普通模式"};
                recoveryButton.Click+=delegate{SetDesktopMode(false);};
                recoveryWindow=new Window{Title="日程板模式控制",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,Focusable=false,FontFamily=FontFamily,FontSize=FontSize,Resources=Resources,Content=recoveryButton};
                recoveryWindow.StateChanged+=delegate{if(prefs.DesktopMode&&recoveryWindow.WindowState==WindowState.Minimized)recoveryWindow.WindowState=WindowState.Normal;};
            }
            UpdateLayout();var point=desktopButton.PointToScreen(new Point(0,0));var matrix=PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            Rect slot;
            if(!FindRecoverySlot(new Rect(point.X,point.Y,Math.Max(42,desktopButton.ActualWidth)*matrix.M11,Math.Max(34,desktopButton.ActualHeight)*matrix.M22),out slot)){recoveryWindow.Hide();return;}
            var dip=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice.Transform(new Point(slot.X,slot.Y));
            recoveryWindow.Left=dip.X;recoveryWindow.Top=dip.Y;
            recoveryWindow.Width=slot.Width/matrix.M11;recoveryWindow.Height=slot.Height/matrix.M22;
            if(!recoveryWindow.IsVisible)recoveryWindow.Show();
            NativeDesktop.MakeNoActivate(recoveryWindow);native.RecoveryWindow=recoveryWindow;native.PlaceAtDesktop();
        }
        void RequestIcons(){
            if(iconReadBusy||closing)return;iconReadBusy=true;iconReadStarted=DateTime.UtcNow;
            Task.Factory.StartNew<IconGeometry.Result>(IconGeometry.Read,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default).ContinueWith(task=>{
                if(closing||Dispatcher.HasShutdownStarted)return;
                Dispatcher.BeginInvoke(new Action(delegate{
                    iconReadBusy=false;if(closing)return;
                    var value=task.IsFaulted?new IconGeometry.Result{Valid=false,Error=task.Exception.GetBaseException().Message}:task.Result;
                    if(value.Valid){iconSnapshot=value;iconFailures=0;if(desktopModeRequested&&!prefs.DesktopMode)SetDesktopMode(true);else if(prefs.DesktopMode){errorText.Visibility=Visibility.Collapsed;QueueDesktopControl();}}
                    else{iconFailures++;errorText.Text="图标位置暂不可读："+value.Error;errorText.Visibility=Visibility.Visible;if(iconSnapshot==null){desktopModeRequested=false;desktopButton.Content="桌面";}else if(iconFailures>=3&&prefs.DesktopMode)SetDesktopMode(false);}
                }));
            });
        }
        void ApplyIconMask(){
            if(!prefs.DesktopMode||iconSnapshot==null||!iconSnapshot.Valid){panel.Clip=null;return;}
            UpdateLayout();var holes=new GeometryGroup{FillRule=FillRule.Nonzero};var bounds=new Rect(0,0,panel.ActualWidth,panel.ActualHeight);
            foreach(var raw in iconSnapshot.Rects){var screen=raw;screen.Inflate(5,5);var a=PointFromScreen(screen.TopLeft);var b=PointFromScreen(screen.BottomRight);var local=new Rect(a,b);if(local.IntersectsWith(bounds))holes.Children.Add(new RectangleGeometry(local));}
            panel.Clip=Geometry.Combine(new RectangleGeometry(bounds),holes,GeometryCombineMode.Exclude,null);
        }
        bool FindRecoverySlot(Rect desired,out Rect slot){
            var work=Forms.Screen.FromPoint(new System.Drawing.Point((int)desired.X,(int)desired.Y)).WorkingArea;
            var area=new Rect(work.X,work.Y,work.Width,work.Height);
            Func<Rect,bool> free=r=>area.Contains(r)&&!iconSnapshot.Rects.Any(icon=>{icon.Inflate(6,6);return icon.IntersectsWith(r);});
            if(free(desired)){slot=desired;return true;}
            for(int radius=16;radius<=512;radius+=16)for(int dx=-radius;dx<=radius;dx+=16)foreach(int dy in new[]{-radius,radius}){var r=new Rect(desired.X+dx,desired.Y+dy,desired.Width,desired.Height);if(free(r)){slot=r;return true;}}
            for(double y=area.Top+8;y+desired.Height<area.Bottom;y+=desired.Height+8)for(double x=area.Right-desired.Width-8;x>=area.Left;x-=desired.Width+8){var r=new Rect(x,y,desired.Width,desired.Height);if(free(r)){slot=r;return true;}}
            slot=Rect.Empty;return false;
        }
        void QueueFitToday(){if(ready)Dispatcher.BeginInvoke(new Action(FitToday),DispatcherPriority.Loaded);}
        void FitToday(){
            if(!ready||collapsed||!prefs.AutoFitToday||todayBoundary==null||applyingSize)return;
            UpdateLayout();
            double todayHeight=todayBoundary.TranslatePoint(new Point(0,0),body).Y+todayBoundary.ActualHeight;
            double chrome=ActualHeight-scroll.ActualHeight;
            double wanted=Math.Ceiling(chrome+todayHeight);
            if(double.IsNaN(wanted)||wanted<100)return;
            applyingSize=true;Height=Math.Max(220,Math.Min(SystemParameters.WorkArea.Height-28,wanted));UpdateLayout();applyingSize=false;
        }
        void EditSize(){
            if(collapsed)SetCollapsed(false);
            var stack=new StackPanel{Margin=new Thickness(20)};var w=Dialog("窗口大小",stack);w.Height=390;
            var auto=new CheckBox{Content="高度自动贴合今天的任务",IsChecked=prefs.AutoFitToday,Margin=new Thickness(0,0,0,14)};stack.Children.Add(auto);
            var allow=new CheckBox{Content="允许拖动右下角调整大小",IsChecked=prefs.AllowResizing,Margin=new Thickness(0,0,0,14)};stack.Children.Add(allow);
            var widthLabel=new TextBlock();stack.Children.Add(widthLabel);
            var width=new Slider{Minimum=340,Maximum=640,Value=Width,Margin=new Thickness(0,8,0,16)};stack.Children.Add(width);
            var heightLabel=new TextBlock();stack.Children.Add(heightLabel);
            var height=new Slider{Minimum=220,Maximum=SystemParameters.WorkArea.Height-28,Value=Math.Max(220,Height),Margin=new Thickness(0,8,0,16),IsEnabled=!prefs.AutoFitToday};stack.Children.Add(height);
            Action labels=delegate{widthLabel.Text="宽度："+Math.Round(Width);heightLabel.Text="高度："+Math.Round(Height)+(prefs.AutoFitToday?"（自动）":"");};labels();
            auto.Checked+=delegate{prefs.AutoFitToday=true;height.IsEnabled=false;FitToday();labels();SavePreferences();};
            auto.Unchecked+=delegate{prefs.AutoFitToday=false;height.Value=Height;height.IsEnabled=true;labels();SavePreferences();};
            allow.Checked+=delegate{prefs.AllowResizing=true;UpdateResizeGrip();SavePreferences();};allow.Unchecked+=delegate{prefs.AllowResizing=false;UpdateResizeGrip();SavePreferences();};
            width.ValueChanged+=delegate{Width=width.Value;if(prefs.AutoFitToday)FitToday();labels();SavePreferences();};
            height.ValueChanged+=delegate{if(!prefs.AutoFitToday){Height=height.Value;labels();SavePreferences();}};
            var reset=new Button{Content="恢复今日任务大小",Padding=new Thickness(10),Margin=new Thickness(0,8,0,0)};stack.Children.Add(reset);
            reset.Click+=delegate{auto.IsChecked=true;allow.IsChecked=false;width.Value=390;FitToday();labels();SavePreferences();};
            w.Closed+=delegate{FitToday();SavePreferences();};w.ShowDialog();
        }
        void ClampPosition() {
            // Recover safely after disconnecting a monitor without changing screen settings.
            var region=new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);
            if(!region.IntersectsWith(new Rect(Left,Top,Math.Max(Width-40,1),80))) {
                Left=SystemParameters.WorkArea.Right-Width-16;Top=SystemParameters.WorkArea.Top+16;
            }
        }
        void SavePreferences() {
            if(!ready)return;
            prefs.Left=Left;prefs.Top=Top;prefs.Width=Width;if(!collapsed)prefs.Height=Height;
            try{Store.Write(settingsPath,prefs);}catch(Exception ex){errorText.Text="显示设置未保存："+ex.Message;}
        }
        void SetupTray() {
            tray=new Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="今日桌面日程",Visible=true};
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏",null,delegate{if(IsVisible)Hide();else{Show();Activate();}});
            menu.Items.Add("允许拖动",null,delegate{dragToggle.IsChecked=!(dragToggle.IsChecked==true);});
            menu.Items.Add("切换置顶",null,delegate{topToggle.IsChecked=!(topToggle.IsChecked==true);});
            menu.Items.Add("切换桌面 / 普通模式",null,delegate{SetDesktopMode(!prefs.DesktopMode);});
            menu.Items.Add("刷新",null,delegate{RefreshData(true);});
            menu.Items.Add("退出",null,delegate{Close();});tray.ContextMenuStrip=menu;
            tray.DoubleClick+=delegate{Show();Activate();};
        }
        void RefreshData(bool force) {
            var day=Today;dateText.Text=day.ToString("M月d日  dddd",CultureInfo.GetCultureInfo("zh-CN"));
            string taskFile=config.Resolve(config.tasksPath);
            try {
                string ledgerFile=config.Resolve(config.ledgerPath);
                string sig=Store.FileSignature(taskFile)+"|"+Store.FileSignature(ledgerFile)+"|"+Store.FileSignature(planPath)+"|"+day.Ticks;
                if(!force&&sig==loadedSignature)return;
                var newTasks=Store.LoadTasks(taskFile,ledgerFile);var newPlan=Store.Read(planPath,new PlannerData());
                if(newPlan==null||newPlan.Courses==null||newPlan.Courses.Any(course=>course==null))throw new InvalidDataException("日程配置必须为对象，且 Courses 为有效数组。");

                tasks=newTasks;plan=newPlan;loadedSignature=sig;errorText.Text="";RenderTasks();
                errorText.Visibility=Visibility.Collapsed;
                DateTime updated=File.GetLastWriteTimeUtc(taskFile);if(File.Exists(ledgerFile)&&File.GetLastWriteTimeUtc(ledgerFile)>updated)updated=File.GetLastWriteTimeUtc(ledgerFile);
                sourceText.Text="数据更新 "+TimeZoneInfo.ConvertTimeFromUtc(updated,zone).ToString("MM-dd HH:mm")+" · 每15秒检查变化";
            }catch(Exception ex){errorText.Text="刷新未成功，保留上次内容："+ex.Message;errorText.Visibility=Visibility.Visible;QueueFitToday();}
        }
        void Section(string text){var t=Label(text,13,Blue);t.FontWeight=FontWeights.SemiBold;t.Margin=new Thickness(0,15,0,8);body.Children.Add(t);}
        void RenderTasks() {
            double offset=scroll.VerticalOffset;bool othersExpanded=otherTasks!=null&&otherTasks.IsExpanded;int revision=++renderRevision;
            expandedTaskIds.Clear();foreach(var card in taskCards)if(card.IsExpanded&&card.Tag is string)expandedTaskIds.Add((string)card.Tag);
            var today=Today;body.Children.Clear();taskCards.Clear();
            Section(plan.PlanDate==today.ToString("yyyy-MM-dd")?"今日重点 · 已选择":"今日重点 · 建议安排");
            var focus=Store.FocusTasks(tasks,plan,today);foreach(var task in focus)body.Children.Add(Card(task));
            if(focus.Count==0)body.Children.Add(Label("今天尚未选择重点，可在设置中选择。",13,Muted));
            var courses=plan.Courses.Where(c=>c.OccursOn(today)).OrderBy(c=>c.Start).ToList();
            if(courses.Count>0)Section("今日课程与日程");
            foreach(var c in courses){var box=new StackPanel();box.Children.Add(Label(c.Start+"—"+c.End+"  "+c.Title,14,Text));if(c.Location!="")box.Children.Add(Label(c.Location,12,Muted));body.Children.Add(box);}
            todayBoundary=new Border{Height=1};body.Children.Add(todayBoundary);
            if(courses.Count==0)Section("课程与固定日程");
            if(plan.Courses.Count==0)body.Children.Add(Label("课表待补充 · 设置 → 课表",13,Muted));
            else if(courses.Count==0)body.Children.Add(Label("已录入日程中，今天没有安排。",13,Muted));
            Section("其他近期截止");
            var due=tasks.Where(t=>!t.Completed&&!focus.Contains(t)&&t.Due.HasValue&&t.Due.Value.Date>=today&&t.Due.Value.Date<=today.AddDays(14)).OrderBy(t=>t.Due).Take(5).ToList();
            foreach(var task in due)body.Children.Add(Card(task));
            if(due.Count==0)body.Children.Add(Label("未来两周暂无已记录截止事项。",13,Muted));
            var old=tasks.Where(t=>!t.Completed&&t.Due.HasValue&&t.Due.Value.Date<today).ToList();
            if(old.Count>0){Section("日期已过 · 状态待核实");foreach(var t in old)body.Children.Add(Card(t));}
            var all=new Expander{Header="其他待办（"+tasks.Count(t=>!t.Completed&&!focus.Contains(t)&&!due.Contains(t)&&!old.Contains(t))+"）",Foreground=Muted,Margin=new Thickness(0,12,0,4),IsExpanded=othersExpanded};otherTasks=all;
            var others=new StackPanel();foreach(var t in tasks.Where(t=>!t.Completed&&!focus.Contains(t)&&!due.Contains(t)&&!old.Contains(t)))others.Children.Add(Card(t));all.Content=others;body.Children.Add(all);
            body.Children.Add(Label("已完成 "+tasks.Count(t=>t.Completed)+" 项 · 已从待办中收起",11,Muted));
            body.Children.Add(footerContent);ApplyBackground();QueueFitToday();
            Dispatcher.BeginInvoke(new Action(delegate{if(!closing&&revision==renderRevision){UpdateLayout();scroll.ScrollToVerticalOffset(offset);QueueWheelRectangle();}}),DispatcherPriority.ContextIdle);
        }
        Expander Card(TaskInfo task) {
            var head=new StackPanel();head.Children.Add(Label(task.Title,14,Text));
            string cue=task.Due.HasValue?task.Due.Value.ToString("M/d")+" · "+task.Cue(Today):task.Category+" · "+task.Status;
            head.Children.Add(Label(cue,11,task.Due.HasValue&&(task.Due.Value.Date-Today).Days<=3?Brush("#FFC98B"):Muted));
            var detail=new StackPanel{Margin=new Thickness(16,6,0,8)};
            detail.Children.Add(Label("下一步",11,Blue));detail.Children.Add(Label(task.Next,13,Text));
            detail.Children.Add(Label("当前进展",11,Blue));detail.Children.Add(Label(task.Progress,12,Muted));
            detail.Children.Add(Label("要求与日期依据",11,Blue));detail.Children.Add(Label(task.Requirement+"\n"+task.DeadlineNote,12,Muted));
            var card=new Expander{Header=head,Content=detail,Tag=task.Id,IsExpanded=expandedTaskIds.Contains(task.Id),Foreground=Muted,Background=Brush("#102E4764"),Padding=new Thickness(2,5,5,2),Margin=new Thickness(0,0,0,5),HorizontalContentAlignment=HorizontalAlignment.Stretch,Cursor=Cursors.Hand};
            card.Expanded+=delegate{QueueFitToday();QueueWheelRectangle();};card.Collapsed+=delegate{QueueFitToday();QueueWheelRectangle();};taskCards.Add(card);return card;
        }
        void OpenOverview(){string preferred=config.Resolve(config.overviewPath);if(!File.Exists(preferred))preferred=File.Exists(config.Resolve(config.detailsPath))?config.Resolve(config.detailsPath):config.Resolve(config.tasksPath);Open(preferred);}
        void Open(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){errorText.Text="打开失败："+ex.Message;}}
        bool StartupEnabled(){try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){string command=key==null?null:key.GetValue(RunName) as string;return command!=null&&command.IndexOf(configFilePath,StringComparison.OrdinalIgnoreCase)>=0;}}catch(System.Security.SecurityException){return false;}}
        void SetStartup(bool enable){try{using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(enable)key.SetValue(RunName,"\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --config \""+configFilePath+"\"");else if(StartupEnabled())key.DeleteValue(RunName,false);}}catch(Exception ex){errorText.Text="开机启动设置未生效："+ex.Message;startToggle.IsChecked=StartupEnabled();}}
        Window Dialog(string title,StackPanel contents) {
            var w=new Window{Title=title,Owner=this,Width=440,Height=550,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#F2F5F8"),FontFamily=FontFamily,FontSize=14,Content=new ScrollViewer{Content=contents,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
            return w;
        }
        void EditFocus() {
            var stack=new StackPanel{Margin=new Thickness(20)};
            stack.Children.Add(new TextBlock{Text="选择今天真正要推进的任务；建议不超过3项。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
            var checks=new Dictionary<CheckBox,string>();
            var selected=new HashSet<string>(Store.SelectedTaskIds(tasks,plan),StringComparer.Ordinal);
            foreach(var t in tasks.Where(t=>!t.Completed)){
                var c=new CheckBox{Content=t.Title,IsChecked=plan.PlanDate==Today.ToString("yyyy-MM-dd")&&selected.Contains(t.Id),Margin=new Thickness(0,0,0,12)};stack.Children.Add(c);checks[c]=t.Id;
            }
            var w=Dialog("选择今日重点",stack);
            var save=new Button{Content="保存今天的安排",Padding=new Thickness(12),Margin=new Thickness(0,10,0,0)};
            save.Click+=delegate{try{plan.PlanDate=Today.ToString("yyyy-MM-dd");plan.TodayTaskIds=checks.Where(x=>x.Key.IsChecked==true).Select(x=>x.Value).ToArray();Store.Write(planPath,plan);RefreshData(true);w.Close();}catch(Exception ex){MessageBox.Show(w,ex.Message,"未保存");}};stack.Children.Add(save);w.ShowDialog();
        }
        void EditCourses() {
            var stack=new StackPanel{Margin=new Thickness(20)};var w=Dialog("课表与固定日程",stack);
            stack.Children.Add(new TextBlock{Text="每周课程可设置星期；单次安排填写日期。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            var title=new TextBox{Margin=new Thickness(0,3,0,10)};stack.Children.Add(new TextBlock{Text="课程 / 事项名称"});stack.Children.Add(title);
            var weekly=new CheckBox{Content="每周重复",IsChecked=true,Margin=new Thickness(0,0,0,10)};stack.Children.Add(weekly);
            var days=new ComboBox{ItemsSource=new[]{"星期日","星期一","星期二","星期三","星期四","星期五","星期六"},SelectedIndex=1,Margin=new Thickness(0,0,0,10)};stack.Children.Add(days);
            var date=new TextBox{Text=Today.ToString("yyyy-MM-dd"),Margin=new Thickness(0,0,0,10)};stack.Children.Add(new TextBlock{Text="单次日期（年-月-日）"});stack.Children.Add(date);
            TextBox start=new TextBox{Text="09:00"},end=new TextBox{Text="10:00"},location=new TextBox();
            foreach(var pair in new[]{new KeyValuePair<string,TextBox>("开始时间（24小时）",start),new KeyValuePair<string,TextBox>("结束时间",end),new KeyValuePair<string,TextBox>("教室 / 地点",location)}){stack.Children.Add(new TextBlock{Text=pair.Key});pair.Value.Margin=new Thickness(0,3,0,10);stack.Children.Add(pair.Value);}
            var msg=new TextBlock{Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};stack.Children.Add(msg);
            var add=new Button{Content="添加并保存",Padding=new Thickness(10)};stack.Children.Add(add);
            add.Click+=delegate{
                DateTime parsed;TimeSpan a,b;
                if(string.IsNullOrWhiteSpace(title.Text)||!TimeSpan.TryParseExact(start.Text,@"hh\:mm",CultureInfo.InvariantCulture,out a)||!TimeSpan.TryParseExact(end.Text,@"hh\:mm",CultureInfo.InvariantCulture,out b)||b<=a||a.TotalHours>=24||b.TotalHours>=24){msg.Text="请填写名称及有效起止时间，例如09:00—10:00。";return;}
                if(weekly.IsChecked!=true&&!DateTime.TryParseExact(date.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed)){msg.Text="单次日期请使用 yyyy-MM-dd 格式。";return;}
                try{plan.Courses.Add(new Course{Title=title.Text.Trim(),Weekly=weekly.IsChecked==true,Weekday=days.SelectedIndex,Date=date.Text,Start=start.Text,End=end.Text,Location=location.Text.Trim()});Store.Write(planPath,plan);RefreshData(true);w.Close();}catch(Exception ex){msg.Text=ex.Message;}
            };
            stack.Children.Add(new TextBlock{Text="已录入（点击删除后可重新添加）",Margin=new Thickness(0,18,0,10)});
            foreach(var c in plan.Courses.ToList()) {
                var course=c;var row=new StackPanel{Margin=new Thickness(0,0,0,10)};
                row.Children.Add(new TextBlock{Text=c.Title+"  "+(c.Weekly?days.Items[c.Weekday]:c.Date)+" "+c.Start+"—"+c.End,TextWrapping=TextWrapping.Wrap});
                var remove=new Button{Content="删除这条",HorizontalAlignment=HorizontalAlignment.Left};remove.Click+=delegate{try{plan.Courses.Remove(course);Store.Write(planPath,plan);RefreshData(true);row.Visibility=Visibility.Collapsed;}catch(Exception ex){msg.Text=ex.Message;}};row.Children.Add(remove);stack.Children.Add(row);
            }
            w.ShowDialog();
        }
        void SelfTest() {
            try {
                Action<bool,string> check=(ok,msg)=>{if(!ok)throw new Exception(msg);};
                check(loadedSignature!="","任务未成功读取："+errorText.Text);
                dragToggle.IsChecked=true;check(prefs.AllowDragging,"拖动开关无效");dragToggle.IsChecked=false;
                topToggle.IsChecked=true;check(Topmost,"置顶开关无效");topToggle.IsChecked=false;
                transparency.Value=100;SettleLayout();check(panel.BorderThickness.Left==0,"全透明仍有外框");
                SetCollapsed(true);check(scroll.Visibility==Visibility.Collapsed,"收起无效");SetCollapsed(false);
                prefs.AutoFitToday=true;settings.Visibility=Visibility.Collapsed;FitToday();SettleLayout();
                check(Height>=220&&Height<=SystemParameters.WorkArea.Height,"窗口自动高度异常");
                SavePreferences();var saved=Store.Read(settingsPath,new Preferences());check(!saved.AlwaysOnTop,"显示设置未保存");
                string fixtures=Path.Combine(folder,"smoke-fixtures");Directory.CreateDirectory(fixtures);
                config.tasksPath=Path.Combine(fixtures,"tasks.json");config.ledgerPath=Path.Combine(fixtures,"ledger.md");
                Store.Write(config.tasksPath,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>{new TaskRow{id="alpha",title="JSON旧标题",status="待开始"},new TaskRow{id="jsonOnly",title="JSON独有项目",status="待开始"}}});
                plan=new PlannerData{PlanDate=Today.ToString("yyyy-MM-dd"),TodayTaskIds=new[]{"alpha"}};Store.Write(planPath,plan);
                string[] rows={"| alpha | 工作 | 同名任务 | 重要 | 待核实 | 进行中 | 核对全量下一步<br>第二行 | 完整要求\\|分隔符<br>source: generic |","| Beta_7 | 科研 | 同名任务 | 常规 | 建议2030-04-02 | 已完成初稿，未提交 | 继续处理 | 局部完成仍待办 |","| longWork | 工作 | 长期事项 | 常规 | 待核实 | 待开始 | 推进安排 | 详情说明 |","| verified | 工作 | 确认完成事项 | 常规 | 2030-04-02 | 已完成（用户确认） | 无 | 完成依据 |"};
                string header="<!-- tasks:start -->\n| ID | 类别 | 任务 | 优先级 | 截止日期 | 状态 | 下一步 | 详细说明 |\n| --- | --- | --- | --- | --- | --- | --- | --- |\n",tail="\n<!-- tasks:end -->\n";
                File.WriteAllText(config.ledgerPath,header+string.Join("\n",rows)+tail,new UTF8Encoding(false));
                RefreshData(false);SettleLayout();
                check(tasks.Select(task=>task.Id).SequenceEqual(new[]{"alpha","Beta_7","longWork","verified"}),"主表任务集合或顺序未同步");
                check(tasks.All(task=>task.Id!="jsonOnly")&&tasks.Single(task=>task.Id=="verified").Completed,"旧JSON任务或完成项处理错误");
                var alpha=taskCards.Single(card=>(string)card.Tag=="alpha");alpha.ApplyTemplate();
                var toggle=alpha.Template.FindName("Toggle",alpha) as ToggleButton;check(toggle!=null&&panel.IsHitTestVisible&&toggle.IsEnabled,"普通模式任务不可交互");
                typeof(ToggleButton).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(toggle,null);SettleLayout();
                check(alpha.IsExpanded,"任务模板展开交互失败");
                string detail=string.Join("\n",((StackPanel)alpha.Content).Children.OfType<TextBlock>().Select(text=>text.Text));
                check(detail.Contains("核对全量下一步\n第二行")&&detail.Contains("完整要求|分隔符\nsource: generic"),"自动关联的完整详情未显示");
                otherTasks.IsExpanded=true;prefs.AutoFitToday=false;Height=340;SettleLayout();scroll.ScrollToVerticalOffset(80);SettleLayout();double offset=scroll.VerticalOffset;
                check(offset>0,"合成内容未产生可验证的滚动范围");
                rows[0]="| alpha | 工作 | 改名后的任务 | 紧急 | 截止日期：2030-04-02前 | 进展已更新 | 更新后的下一步 | 更新后的完整要求 |";
                rows[2]="| added-task | 工作 | 新增且JSON尚未刷新 | 常规 | 待核实 | 待开始 | 自动出现 | 新增详情 |";
                string revised=header+string.Join("\n",rows.Reverse())+tail;
                File.WriteAllText(config.ledgerPath,revised,new UTF8Encoding(false));RefreshData(false);SettleLayout();
                check(tasks.Select(task=>task.Id).SequenceEqual(new[]{"verified","added-task","Beta_7","alpha"}),"新增、删项或排序未随主表更新");
                check(Store.FocusTasks(tasks,plan,Today).Single().Title=="改名后的任务","改名导致今日稳定编号失效");
                check(taskCards.Single(card=>(string)card.Tag=="alpha").IsExpanded&&otherTasks.IsExpanded,"刷新后任务或其他待办展开状态丢失");
                check(Math.Abs(scroll.VerticalOffset-offset)<1,"刷新后滚动位置丢失");
                check(taskCards.Any(card=>(string)card.Tag=="Beta_7")&&!taskCards.Any(card=>(string)card.Tag=="verified"),"局部完成被收起或完成任务仍显示");
                string signature=loadedSignature;var previous=tasks;var previousCard=taskCards.Single(card=>(string)card.Tag=="alpha");
                File.WriteAllText(config.ledgerPath,"<!-- tasks:start -->\n正在编辑",new UTF8Encoding(false));RefreshData(false);SettleLayout();
                check(object.ReferenceEquals(previous,tasks)&&loadedSignature==signature&&taskCards.Contains(previousCard)&&errorText.Visibility==Visibility.Visible,"错误总账刷新未保留旧内容");
                File.Delete(config.ledgerPath);RefreshData(false);SettleLayout();check(tasks.Select(task=>task.Id).SequenceEqual(new[]{"alpha","jsonOnly"}),"主表删除未触发JSON回退");
                File.WriteAllText(config.ledgerPath,revised,new UTF8Encoding(false));RefreshData(false);SettleLayout();check(tasks.Any(task=>task.Id=="added-task"),"新建主表文件未触发自动读取");
                otherTasks.IsExpanded=true;taskCards.Single(card=>(string)card.Tag=="alpha").IsExpanded=true;SettleLayout();scroll.ScrollToTop();SettleLayout();
                SavePreview("smoke-preview.png");
                Store.Write(Path.Combine(folder,"smoke-result.json"),new{passed=true,tasks=tasks.Count,completed=tasks.Count(t=>t.Completed),configuredTimezone=config.timezone,tests=new[]{"配置和任务读取","拖动开关","置顶开关","全透明外框","收起展开","自动高度","隔离显示设置持久化","规范总账主源","通用新编号和重名","完整详情和转义","模板展开交互","改名与排序","完成与局部完成","新增与删项","按ID保留展开和滚动","错误保留旧显示","主表存在性变化"},startupChanged=false,desktopIconsRead=false,existingPreferencesRead=false});
            }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"smoke-error.txt"),ex.ToString(),Encoding.UTF8);Environment.ExitCode=1;}
            Close();
        }
        void SettleLayout(){var frame=new DispatcherFrame();Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);UpdateLayout();}
        void SavePreview(string name){var image=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(folder,name)))encoder.Save(output);}
    }
    internal static class Program {
        [STAThread] static void Main(string[] args) {
            bool test=args.Contains("--self-test")||args.Contains("--smoke-test"),interactive=args.Contains("--interactive-test");
            bool checkData=args.Contains("--check-data");
            string folder=AppDomain.CurrentDomain.BaseDirectory;
            try {
                if(checkData){Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true});Console.SetError(new StreamWriter(Console.OpenStandardError(),new UTF8Encoding(false)){AutoFlush=true});}
                int ci=Array.IndexOf(args,"--config");
                if(ci>=0&&ci+1>=args.Length)throw new ArgumentException("--config 后需要配置文件路径。");
                string configPath=ci>=0?Path.GetFullPath(args[ci+1]):Path.Combine(folder,"workflow.config.json");
                if(checkData) {
                    int di=Array.IndexOf(args,"--check-data");
                    if(di+1>=args.Length||args[di+1].StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("--check-data 后需要任务 JSON 路径。");
                    var config=WorkflowConfig.Load(configPath);var zone=config.Zone();
                    var day=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,zone).Date;
                    var tasks=Store.LoadTasks(Path.GetFullPath(args[di+1]),config.Resolve(config.ledgerPath));
                    Console.WriteLine(Store.Json.Serialize(new{passed=true,today=day.ToString("yyyy-MM-dd"),configuredTimezone=config.timezone,tasks=tasks.Select(task=>new{id=task.Id,title=task.Title,category=task.Category,priority=task.Priority,status=task.Status,due=task.Due.HasValue?task.Due.Value.ToString("yyyy-MM-dd"):null,next=task.Next,progress=task.Progress,requirement=task.Requirement,deadlineNote=task.DeadlineNote}).ToArray()}));
                    return;
                }
                string identity;
                using(var sha=System.Security.Cryptography.SHA256.Create())identity=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(configPath.ToUpperInvariant()))).Replace("-","").Substring(0,16);
                bool created;
                using(var mutex=new Mutex(true,@"Local\CodexDesktopBoard-"+identity+(test?"-Test":interactive?"-Qa":""),out created)) {
                    if(!created){if(!test)Forms.MessageBox.Show("此配置的日程板已经运行，可从右下角托盘图标打开。","Codex桌面日程板");return;}
                    var app=new Application();app.Run(new Board(folder,test,interactive,configPath));
                }
            } catch(Exception ex) {
                if(checkData){Console.Error.WriteLine("数据检查失败："+ex.Message);Environment.ExitCode=1;return;}
                File.WriteAllText(Path.Combine(folder,test?"smoke-error.txt":"运行错误.txt"),ex.ToString(),Encoding.UTF8);
                if(!test)Forms.MessageBox.Show("日程板未能启动："+ex.Message,"Codex桌面日程板");Environment.ExitCode=1;
            }
        }
    }
}

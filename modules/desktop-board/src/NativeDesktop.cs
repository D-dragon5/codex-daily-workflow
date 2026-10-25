// Portions adapted from Desktop Tasks Widget (MIT).
// Copyright (c) 2025 Desktop Tasks Widget Contributors
// https://github.com/tanxestudio/Windows-desktop-Tasks-Widget
// Source: Windows/DesktopOverlay.xaml.cs at 32322d2e649a7271722a17a4eac44ac9189e1ca0
// Adapted portions: foreground event hook, temporary topmost state and
// minimization handling; window style switching was reorganized using upstream's
// interaction approach. See ../THIRD_PARTY.md
// and ../LICENSE-UPSTREAM.txt. Monitor alignment and icon avoidance integration
// are additions maintained in Codex Daily Workflow.
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace DailySchedule {
    internal sealed class NativeDesktop : IDisposable {
        const uint EVENT_SYSTEM_FOREGROUND = 3;
        const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 16;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        delegate void WinEventDelegate(IntPtr hook, uint type, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
        delegate bool EnumWindowDelegate(IntPtr hwnd,IntPtr param);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowDelegate callback,IntPtr param);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y;}
        [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public NativeRect Bounds,Work;public uint Flags;}
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        delegate bool MonitorEnumProc(IntPtr monitor,IntPtr dc,ref NativeRect rect,IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorEnumProc callback,IntPtr data);
        sealed class PixelCoordinates : IDisposable {
            readonly IntPtr old;
            public PixelCoordinates(){try{old=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){old=IntPtr.Zero;}}
            public void Dispose(){if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}
        }
        internal sealed class ScreenState {public IntPtr Monitor;public Rect Window,Work;}
        static Rect Rectangle(NativeRect r){return new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);}
        static ScreenState ReadScreen(Window window){
            var hwnd=Handle(window);var monitor=MonitorFromWindow(hwnd,2);var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};NativeRect rect;
            if(!GetMonitorInfo(monitor,ref info)||!GetWindowRect(hwnd,out rect))throw new InvalidOperationException("无法读取当前显示器工作区");
            return new ScreenState{Monitor=monitor,Work=Rectangle(info.Work),Window=Rectangle(rect)};
        }
        public static ScreenState CurrentScreen(Window window){using(new PixelCoordinates())return ReadScreen(window);}
        public static void AlignRight(Window window,double marginDip){
            using(new PixelCoordinates()){
                var state=ReadScreen(window);int margin=(int)Math.Round(marginDip*state.Window.Width/Math.Max(1,window.ActualWidth));
                int x=(int)Math.Round(Math.Max(state.Work.Left+margin,state.Work.Right-state.Window.Width-margin));
                double topMin=state.Work.Top+margin,topMax=Math.Max(topMin,state.Work.Bottom-state.Window.Height-margin);
                int y=(int)Math.Round(Math.Max(topMin,Math.Min(topMax,state.Window.Top)));
                if(!SetWindowPos(Handle(window),IntPtr.Zero,x,y,0,0,SWP_NOSIZE|SWP_NOACTIVATE|0x0004))throw new InvalidOperationException("窗口靠右未完成");
            }
        }
        public static List<Rect> MonitorWorkAreas(){using(new PixelCoordinates()){
            var list=new List<Rect>();EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr m,IntPtr dc,ref NativeRect r,IntPtr p){var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};if(GetMonitorInfo(m,ref info))list.Add(Rectangle(info.Work));return true;},IntPtr.Zero);return list;
        }}
        public static void MoveInPixels(Window window,int x,int y){using(new PixelCoordinates())SetWindowPos(Handle(window),IntPtr.Zero,x,y,0,0,SWP_NOSIZE|SWP_NOACTIVATE|0x0004);}
        readonly Window window;
        readonly WinEventDelegate callback;
        IntPtr hook;
        bool temporary,disposed;
        HwndSource source;

        public bool DesktopMode {get;private set;}
        public Window RecoveryWindow {get;set;}


        public NativeDesktop(Window w) {
            window=w; callback=OnForeground;
            source=HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);if(source!=null)source.AddHook(WndProc);
            hook=SetWinEventHook(EVENT_SYSTEM_FOREGROUND,EVENT_SYSTEM_FOREGROUND,IntPtr.Zero,callback,0,0,0);
        }
        IntPtr WndProc(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled){
            // Allow WPF's routed wheel input to reach the content ScrollViewer.
            if(msg==0x0112&&DesktopMode&&((w.ToInt64()&0xfff0)==0xf020))handled=true;
            return IntPtr.Zero;
        }
        static IntPtr Handle(Window w){return new WindowInteropHelper(w).EnsureHandle();}
        public void SetDesktopMode(bool enabled){
            DesktopMode=enabled;temporary=false;
            int style=GetWindowLong(Handle(window),-20);
            const int transparent=0x20,noActivate=0x08000000;
            style=enabled?style|transparent|noActivate:style&~transparent&~noActivate;
            SetWindowLong(Handle(window),-20,style);
            if(enabled)PlaceAtDesktop();else Reset();
        }
        public static void MakeNoActivate(Window w){SetWindowLong(Handle(w),-20,GetWindowLong(Handle(w),-20)|0x08000000);}
        public bool InputTransparent {get{return (GetWindowLong(Handle(window),-20)&0x20)!=0;}}
        public bool RecoveryInteractive {get{return RecoveryWindow!=null&&(GetWindowLong(Handle(RecoveryWindow),-20)&0x20)==0;}}
        public bool AtDesktopLayer {get{var anchor=DesktopAnchor();return anchor!=IntPtr.Zero&&GetWindow(Handle(window),2)==anchor;}}





        IntPtr DesktopAnchor(){
            IntPtr anchor=IntPtr.Zero;
            EnumWindows(delegate(IntPtr hwnd,IntPtr p){if(FindWindowEx(hwnd,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero){anchor=hwnd;return false;}return true;},IntPtr.Zero);
            return anchor==IntPtr.Zero?FindWindow("Progman",null):anchor;
        }
        public void PlaceAtDesktop(){
            if(!DesktopMode)return;
            IntPtr hwnd=Handle(window),anchor=DesktopAnchor();
            // Lowest application layer, immediately above Explorer's desktop.
            // HWND_BOTTOM alone can hide the widget underneath the wallpaper.
            IntPtr after=anchor==IntPtr.Zero?new IntPtr(1):GetWindow(anchor,3);
            if(after!=hwnd){
                if(RecoveryWindow!=null&&after==Handle(RecoveryWindow))after=GetWindow(after,3);
                if(after!=hwnd)SetWindowPos(hwnd,after,0,0,0,0,SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE);
            }
            if(RecoveryWindow!=null&&RecoveryWindow.IsVisible){
                IntPtr recovery=Handle(RecoveryWindow),above=GetWindow(hwnd,3);
                if(above!=recovery)SetWindowPos(recovery,above,0,0,0,0,SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE);
            }

        }
        public static IntPtr HitAt(Point screen){return WindowFromPoint(new NativePoint{X=(int)screen.X,Y=(int)screen.Y});}
        public static IntPtr WindowHandle(Window w){return Handle(w);}
        public static void BringForTest(Window w){SetWindowPos(Handle(w),HWND_TOPMOST,0,0,0,0,SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE);}
        public void TestWheelMessage(int delta){SendMessage(Handle(window),0x020A,new IntPtr(delta<<16),IntPtr.Zero);}
        void OnForeground(IntPtr h,uint t,IntPtr hwnd,int o,int c,uint th,uint time) {
            if(disposed)return;
            // Keep WPF state changes on its dispatcher; never permanently pin while disabled.
            window.Dispatcher.BeginInvoke(new Action(delegate {
                if(disposed)return;
                if(DesktopMode){PlaceAtDesktop();return;}
                if(window.Topmost) { temporary=false; return; }
                var name=new StringBuilder(256); GetClassName(hwnd,name,256);
                bool desktop=name.ToString()=="WorkerW" || name.ToString()=="Progman";
                if(desktop && !temporary) {
                    temporary=true;
                    SetWindowPos(new WindowInteropHelper(window).Handle,HWND_TOPMOST,0,0,0,0,SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE);
                } else if(!desktop && temporary) {
                    temporary=false;
                    SetWindowPos(new WindowInteropHelper(window).Handle,HWND_NOTOPMOST,0,0,0,0,SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE);
                }
            }));
        }
        public void Reset() {
            if(DesktopMode){PlaceAtDesktop();return;}
            temporary=false;
            SetWindowPos(new WindowInteropHelper(window).Handle,window.Topmost?HWND_TOPMOST:HWND_NOTOPMOST,0,0,0,0,SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE);
        }
        public void Dispose() { disposed=true;if(hook!=IntPtr.Zero)UnhookWinEvent(hook); hook=IntPtr.Zero;if(source!=null)source.RemoveHook(WndProc); }
    }
}

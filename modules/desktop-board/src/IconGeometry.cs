using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace DailySchedule {
    // Public, read-only desktop accessibility geometry. No names, remote memory,
    // input injection, or screenshots. Invoke on the caller's background thread.
    internal static class IconGeometry {
        public sealed class Result {
            public bool Valid;
            public string Error="";
            public List<Rect> Rects=new List<Rect>();
            public int NativeCount=-1;
        }
        private delegate bool EnumProc(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int capacity);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string windowName);
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);

        private static bool Finite(double value) {return !double.IsNaN(value)&&!double.IsInfinity(value);}
        private static Result Fail(Result result,string message) {
            result.Valid=false;result.Error=message;result.Rects.Clear();return result;
        }
        public static Result Read() {
            var result=new Result();
            try {
                IntPtr view=IntPtr.Zero;
                EnumWindows(delegate(IntPtr window,IntPtr parameter) {
                    var name=new StringBuilder(64);GetClassName(window,name,name.Capacity);
                    string cls=name.ToString();if(cls!="Progman"&&cls!="WorkerW")return true;
                    var candidate=FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null);
                    if(candidate==IntPtr.Zero)return true;
                    view=candidate;return false;
                },IntPtr.Zero);
                if(view==IntPtr.Zero)return Fail(result,"未找到桌面图标视图");
                var list=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);
                if(list==IntPtr.Zero)return Fail(result,"未找到桌面图标列表");

                // Hidden desktop icons intentionally need no masking. Occlusion
                // by an ordinary application does not make IsWindowVisible false.
                if(!IsWindowVisible(view)||!IsWindowVisible(list)) {
                    result.Valid=true;result.NativeCount=0;return result;
                }
                IntPtr count;
                // LVM_GETITEMCOUNT is a scalar read-only message: no pointers.
                if(SendMessageTimeout(list,0x1004,IntPtr.Zero,IntPtr.Zero,2,400,out count)==IntPtr.Zero)
                    return Fail(result,"桌面图标数量读取超时或被拒绝");
                long native=count.ToInt64();
                if(native<0||native>10000)return Fail(result,"桌面图标数量无效");
                result.NativeCount=(int)native;

                var root=AutomationElement.FromHandle(list);
                if(root==null||root.Current.ControlType!=ControlType.List)
                    return Fail(result,"桌面无障碍提供程序未返回图标列表");
                AutomationElementCollection items;
                var cache=new CacheRequest {TreeScope=TreeScope.Element};
                cache.Add(AutomationElement.BoundingRectangleProperty);
                using(cache.Activate()) {
                    items=root.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem));
                }
                if(items.Count!=result.NativeCount)
                    return Fail(result,"桌面图标数量与公开几何不一致，保留上次成功结果");
                for(int i=0;i<items.Count;i++) {
                    Rect rect=items[i].Cached.BoundingRectangle;
                    if(rect.IsEmpty||!Finite(rect.X)||!Finite(rect.Y)||!Finite(rect.Width)||!Finite(rect.Height)||rect.Width<=0||rect.Height<=0)
                        return Fail(result,"部分桌面图标几何不可用，保留上次成功结果");
                    // UI Automation rectangles are physical screen pixels and
                    // include the icon and its displayed filename label.
                    result.Rects.Add(rect);
                }
                result.Valid=true;return result;
            } catch(Exception ex) {
                return Fail(result,"桌面图标几何读取失败："+ex.GetType().Name);
            }
        }
    }
}

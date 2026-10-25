using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DailySchedule {
    public sealed class Preferences {
        public double Left=-99999, Top=-99999, Width=390, Height=790, BackgroundOpacity=0.82;
        public bool AllowDragging=false, AlwaysOnTop=false, Collapsed=false;
        public bool AutoFitToday=true, AllowResizing=false, DesktopMode=false;
    }
    public sealed class Course {
        public string Id=Guid.NewGuid().ToString("N"), Title="", Start="09:00", End="10:00", Location="", Date="";
        public int Weekday=1;
        public bool Weekly=true;
        public bool OccursOn(DateTime day) { return Weekly?(int)day.DayOfWeek==Weekday:Date==day.ToString("yyyy-MM-dd"); }
    }
    public sealed class PlannerData {
        public string PlanDate="";
        public string[] TodayTaskIds=new string[0];
        public List<Course> Courses=new List<Course>();
    }
    public sealed class TaskInfo {
        public string Id="",Title="",Priority="",Category="",Status="",Next="",Progress="",Requirement="",DeadlineNote="";
        public DateTime? Due;
        public bool Completed {get{return Status=="已完成"||Status.Equals("completed",StringComparison.OrdinalIgnoreCase)||Status.Equals("done",StringComparison.OrdinalIgnoreCase);}}
        public string Cue(DateTime today) {
            if(Completed)return "已完成";
            if(!Due.HasValue)return "日期待定";
            int n=(Due.Value.Date-today.Date).Days;
            return n<0?"日期已过 · 状态待核实":n==0?"今天截止":n==1?"明天截止":n+"天后截止";
        }
    }
    public sealed class WorkflowConfig {
        public int schemaVersion=1;
        public string timezone="local",ledgerPath="data/每日任务与科研总账.md",tasksPath="data/tasks.json",overviewPath="data/任务总览.xlsx",detailsPath="data/任务详情.html",weeklyReportsDir="data/weekly-reports",dailyTime="05:30";
        public string plannerPath="日程配置.json",settingsPath="显示设置.json";
        public string weeklySubmissionDay=null;
        [ScriptIgnore] public string DirectoryPath;
        public string Resolve(string value) {
            if(string.IsNullOrWhiteSpace(value))throw new InvalidDataException("配置中的文件路径不能为空。");
            string expanded=Environment.ExpandEnvironmentVariables(value);
            return Path.GetFullPath(Path.IsPathRooted(expanded)?expanded:Path.Combine(DirectoryPath,expanded));
        }
        public TimeZoneInfo Zone() {
            string value=(timezone??"").Trim();
            if(value.Equals("local",StringComparison.OrdinalIgnoreCase))return TimeZoneInfo.Local;
            if(value.Equals("UTC",StringComparison.OrdinalIgnoreCase)||value=="Etc/UTC")return TimeZoneInfo.Utc;
            if(value=="Asia/Hong_Kong"||value=="Asia/Shanghai")value="China Standard Time";
            try{return TimeZoneInfo.FindSystemTimeZoneById(value);}
            catch(Exception ex){if(ex is TimeZoneNotFoundException||ex is InvalidTimeZoneException)throw new InvalidDataException("不支持的时区："+timezone+"。请使用 local、UTC、Asia/Hong_Kong、Asia/Shanghai 或有效的 Windows 时区 ID。",ex);throw;}
        }
        public static WorkflowConfig Load(string path) {
            if(!File.Exists(path))throw new FileNotFoundException("找不到 workflow.config.json；先复制示例配置或运行安装器。",path);
            var config=Store.Read<WorkflowConfig>(path,null);
            if(config==null||config.schemaVersion!=1)throw new InvalidDataException("workflow.config.json 的 schemaVersion 必须为 1。");
            config.DirectoryPath=Path.GetDirectoryName(Path.GetFullPath(path));
            config.Resolve(config.tasksPath);config.Resolve(config.ledgerPath);config.Resolve(config.overviewPath);config.Resolve(config.plannerPath);config.Resolve(config.settingsPath);config.Zone();
            return config;
        }
    }
    public sealed class TasksDocument {
        public int schemaVersion;
        public string updatedAt="",timezone="local";
        public List<TaskRow> tasks;
    }
    public sealed class TaskRow {
        public string id,title,priority,category,due,status,next,progress,requirement,deadlineNote;
    }
    internal static class Store {
        public static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
        public static T Read<T>(string path,T fallback) {
            if(!File.Exists(path))return fallback;
            return Json.Deserialize<T>(ReadSharedText(path));
        }
        public static string ReadSharedText(string path) {
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(file,Encoding.UTF8))return reader.ReadToEnd();
        }
        public static void Write(string path,object value) {
            string parent=Path.GetDirectoryName(Path.GetFullPath(path));Directory.CreateDirectory(parent);
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,Json.Serialize(value),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public static List<TaskInfo> LoadTasks(string path) {
            if(!File.Exists(path))throw new FileNotFoundException("找不到任务 JSON；请检查 tasksPath 或先刷新派生视图。",path);
            var doc=Read<TasksDocument>(path,null);
            if(doc==null||doc.schemaVersion!=1||doc.tasks==null)throw new InvalidDataException("任务 JSON 必须含 schemaVersion:1 和 tasks 数组。");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            var result=new List<TaskInfo>();
            foreach(var row in doc.tasks) {
                if(row==null||string.IsNullOrWhiteSpace(row.id)||string.IsNullOrWhiteSpace(row.title))throw new InvalidDataException("每项任务必须有非空 id 和 title。");
                if(!seen.Add(row.id))throw new InvalidDataException("任务 id 重复："+row.id);
                DateTime parsed;DateTime? due=null;
                if(!string.IsNullOrWhiteSpace(row.due)) {
                    if(!DateTime.TryParseExact(row.due,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))throw new InvalidDataException("任务 "+row.id+" 的 due 应为 yyyy-MM-dd 或 null。");
                    due=parsed.Date;
                }
                result.Add(new TaskInfo{Id=row.id,Title=row.title,Priority=row.priority??"常规",Category=row.category??"其他",Due=due,Status=row.status??"待开始",Next=row.next??"",Progress=row.progress??"",Requirement=row.requirement??"",DeadlineNote=row.deadlineNote??""});
            }
            return result;
        }
        public static List<TaskInfo> FocusTasks(List<TaskInfo> all,PlannerData plan,DateTime day) {
            var active=all.Where(t=>!t.Completed).ToList();
            if(plan.PlanDate==day.ToString("yyyy-MM-dd"))return (plan.TodayTaskIds??new string[0]).Distinct().Select(id=>active.FirstOrDefault(t=>t.Id==id)).Where(t=>t!=null).ToList();
            var result=active.Where(t=>t.Category.Contains("科研")||t.Category.Equals("research",StringComparison.OrdinalIgnoreCase)).OrderBy(t=>t.Due??DateTime.MaxValue).Take(1).ToList();
            result.AddRange(active.Where(t=>!result.Contains(t)&&t.Due.HasValue&&t.Due.Value.Date>=day.Date).OrderBy(t=>t.Due).Take(3-result.Count));
            if(result.Count<3)result.AddRange(active.Where(t=>!result.Contains(t)).Take(3-result.Count));
            return result;
        }
    }
}

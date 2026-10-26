using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        public bool Completed {get{return Store.IsComplete(Status);}}
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
        const string TaskStart="<!-- tasks:start -->",TaskEnd="<!-- tasks:end -->";
        static readonly string[] Headers={"ID","类别","任务","优先级","截止日期","状态","下一步","详细说明"};
        static readonly HashSet<string> Complete=new HashSet<string>(new[]{"已完成","完成","已验证完成","已完成（用户确认）","已完成（已验证）","已完成(用户确认)","已完成(已验证)"},StringComparer.Ordinal);
        static readonly Regex TaskId=new Regex(@"\A[A-Za-z][A-Za-z0-9_-]{0,63}\z");
        static readonly Regex ExplicitDate=new Regex(@"\A(?:(?:截止(?:日期|时间)?)[：:]?\s*)?(\d{4}-\d{2}-\d{2})(?:\s*(?:截止|前))?\z");
        public static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
        public static bool IsComplete(string status){return status!=null&&(Complete.Contains(status)||status.Equals("done",StringComparison.OrdinalIgnoreCase)||status.Equals("completed",StringComparison.OrdinalIgnoreCase));}
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
                if(row==null||row.id==null||!TaskId.IsMatch(row.id)||string.IsNullOrWhiteSpace(row.title))throw new InvalidDataException("每项任务必须有有效 id 和非空 title；id 须以英文字母开头，仅含字母、数字、下划线或连字符，最多64字符。");
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
        public static List<TaskInfo> LoadTasks(string path,string ledgerPath) {
            if(File.Exists(ledgerPath)) {
                string content=ReadSharedText(ledgerPath);
                // A legacy descriptive ledger keeps the standalone JSON workflow usable.
                if(content.Contains(TaskStart)||content.Contains(TaskEnd))return ParseLedger(content);
            }
            return LoadTasks(path);
        }
        static int Occurrences(string text,string value){int count=0,position=0;while((position=text.IndexOf(value,position,StringComparison.Ordinal))>=0){count++;position+=value.Length;}return count;}
        static string[] SplitRow(string line) {
            string value=line.Trim();
            if(!value.StartsWith("|",StringComparison.Ordinal)||!value.EndsWith("|",StringComparison.Ordinal))throw new InvalidDataException("任务表每行须以竖线开始和结束。");
            var fields=new List<string>();var current=new StringBuilder();bool escaped=false;
            for(int i=1;i<value.Length-1;i++) {
                char ch=value[i];
                if(escaped){if(ch!='|'&&ch!='\\')current.Append('\\');current.Append(ch);escaped=false;}
                else if(ch=='\\')escaped=true;
                else if(ch=='|'){fields.Add(current.ToString().Trim());current.Length=0;}
                else current.Append(ch);
            }
            if(escaped)current.Append('\\');fields.Add(current.ToString().Trim());
            return fields.Select(item=>Regex.Replace(item,@"<br\s*/?>","\n",RegexOptions.IgnoreCase)).ToArray();
        }
        public static List<TaskInfo> ParseLedger(string content) {
            if(Occurrences(content,TaskStart)!=1||Occurrences(content,TaskEnd)!=1)throw new InvalidDataException("总账必须恰好包含一组 tasks:start 和 tasks:end 标记。");
            int begin=content.IndexOf(TaskStart,StringComparison.Ordinal),end=content.IndexOf(TaskEnd,StringComparison.Ordinal);
            if(begin>=end)throw new InvalidDataException("任务表标记顺序错误。");
            var lines=content.Substring(begin+TaskStart.Length,end-begin-TaskStart.Length).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Where(line=>!string.IsNullOrWhiteSpace(line)).ToArray();
            if(lines.Length<2||!SplitRow(lines[0]).SequenceEqual(Headers))throw new InvalidDataException("任务表表头应为："+string.Join(" / ",Headers));
            var separator=SplitRow(lines[1]);
            if(separator.Length!=Headers.Length||separator.Any(field=>!Regex.IsMatch(field.Replace(" ",""),@"\A:?-{3,}:?\z")))throw new InvalidDataException("任务表分隔行无效。");
            var result=new List<TaskInfo>();var seen=new HashSet<string>(StringComparer.Ordinal);
            for(int i=2;i<lines.Length;i++) {
                var row=SplitRow(lines[i]);
                if(row.Length!=Headers.Length)throw new InvalidDataException("任务表第 "+(i+1)+" 行列数错误；单元格竖线须转义为 \\|。");
                string id=row[0],status=row[5];
                if(!TaskId.IsMatch(id))throw new InvalidDataException("任务 ID 无效："+id);
                if(!seen.Add(id))throw new InvalidDataException("任务 ID 重复："+id);
                if(string.IsNullOrWhiteSpace(row[2]))throw new InvalidDataException("任务标题不能为空："+id);
                DateTime? due=null;var match=ExplicitDate.Match(row[4].Trim());
                if(match.Success){DateTime parsed;if(!DateTime.TryParseExact(match.Groups[1].Value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed))throw new InvalidDataException("截止日期无效（"+id+"）："+match.Groups[1].Value);due=parsed.Date;}
                result.Add(new TaskInfo{Id=id,Category=row[1]==""?"未分类":row[1],Title=row[2],Priority=row[3]==""?"常规":row[3],Due=due,Status=IsComplete(status)?"已完成":status==""?"待核查":status,Next=row[6],Progress=status==""?"待核查":status,Requirement=row[7],DeadlineNote=row[4]==""?"日期待定":row[4]});
            }
            return result;
        }
        public static string FileSignature(string path) {
            string full=Path.GetFullPath(path);if(!File.Exists(full))return full+":missing";
            var file=new FileInfo(full);return full+":exists:"+file.LastWriteTimeUtc.Ticks+":"+file.Length;
        }
        public static IEnumerable<string> SelectedTaskIds(List<TaskInfo> all,PlannerData plan) {
            var byId=all.ToDictionary(task=>task.Id,StringComparer.Ordinal);
            foreach(string value in (plan.TodayTaskIds??new string[0]).Distinct()) {
                if(value==null)continue;
                TaskInfo task;
                if(byId.TryGetValue(value,out task)){yield return task.Id;continue;}
                var matches=all.Where(item=>item.Title==value).Take(2).ToList();
                if(matches.Count==1)yield return matches[0].Id;
            }
        }
        public static List<TaskInfo> FocusTasks(List<TaskInfo> all,PlannerData plan,DateTime day) {
            var active=all.Where(t=>!t.Completed).ToList();
            if(plan.PlanDate==day.ToString("yyyy-MM-dd"))return SelectedTaskIds(all,plan).Distinct().Select(id=>active.FirstOrDefault(t=>t.Id==id)).Where(t=>t!=null).ToList();
            var result=active.Where(t=>t.Category.Contains("科研")||t.Category.Equals("research",StringComparison.OrdinalIgnoreCase)).OrderBy(t=>t.Due??DateTime.MaxValue).Take(1).ToList();
            result.AddRange(active.Where(t=>!result.Contains(t)&&t.Due.HasValue&&t.Due.Value.Date>=day.Date).OrderBy(t=>t.Due).Take(3-result.Count));
            if(result.Count<3)result.AddRange(active.Where(t=>!result.Contains(t)).Take(3-result.Count));
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DailySchedule {
    internal static class ModelsTests {
        static int checks;
        static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
        static void Invalid(Action action,string message){bool rejected=false;try{action();}catch(Exception ex){rejected=ex is InvalidDataException||ex is ArgumentException;}Check(rejected,message);}
        static TaskRow Row(string id,string title,string due=null,string status="待开始"){return new TaskRow{id=id,title=title,category="工作",priority="常规",due=due,status=status,next="示例下一步"};}
        static void Main(string[] args){
            try{
                string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string tasks=Path.Combine(root,"tasks.json");
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>{Row("alpha","通用任务","2030-02-01"),Row("done-item","已完成任务",null,"已完成")}});
                var loaded=Store.LoadTasks(tasks);Check(loaded.Count==2&&loaded[0].Id=="alpha","稳定ID读取失败");Check(loaded[0].Due==new DateTime(2030,2,1),"日期解析失败");Check(loaded[1].Completed,"完成状态未识别");
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>()});Check(Store.LoadTasks(tasks).Count==0,"空任务数组未被接受");
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>{Row("same","A"),Row("same","B")}});Invalid(()=>Store.LoadTasks(tasks),"重复ID未拒绝");
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>{Row("bad-date","日期错误","2030-02-30")}});Invalid(()=>Store.LoadTasks(tasks),"无效日期未拒绝");
                Store.Write(tasks,new TasksDocument{schemaVersion=2,tasks=new List<TaskRow>()});Invalid(()=>Store.LoadTasks(tasks),"无效任务schema未拒绝");
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=null});Invalid(()=>Store.LoadTasks(tasks),"缺少tasks数组未拒绝");
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>{Row("","无ID")}});Invalid(()=>Store.LoadTasks(tasks),"空ID未拒绝");
                var dir=Path.Combine(root,"configuration");Directory.CreateDirectory(dir);var configPath=Path.Combine(dir,"workflow.config.json");Store.Write(configPath,new WorkflowConfig());
                string previous=Environment.CurrentDirectory;Environment.CurrentDirectory=root;
                try{var config=WorkflowConfig.Load(configPath);Check(config.Resolve(config.tasksPath)==Path.Combine(dir,"data","tasks.json"),"相对路径未按配置目录解析");Check(config.Zone().Id==TimeZoneInfo.Local.Id,"local时区错误");}finally{Environment.CurrentDirectory=previous;}
                var tz=new WorkflowConfig{timezone="UTC"};Check(tz.Zone().GetUtcOffset(DateTime.UtcNow)==TimeSpan.Zero,"UTC时区错误");
                tz.timezone="Asia/Hong_Kong";Check(tz.Zone().GetUtcOffset(DateTime.UtcNow)==TimeSpan.FromHours(8),"IANA映射错误");
                tz.timezone="__invalid_zone__";Invalid(()=>tz.Zone(),"未知时区被静默替换");
                var badConfig=new WorkflowConfig{tasksPath=""};Store.Write(configPath,badConfig);Invalid(()=>WorkflowConfig.Load(configPath),"空任务路径未拒绝");
                var day=new DateTime(2030,1,1);
                var all=new List<TaskInfo>{new TaskInfo{Id="research-any-id",Title="研究示例",Category="科研",Status="待开始"},new TaskInfo{Id="soon",Title="近期任务",Category="工作",Status="进行中",Due=day.AddDays(1)},new TaskInfo{Id="completed",Status="已完成"}};
                var focus=Store.FocusTasks(all,new PlannerData(),day);Check(focus.Count==2&&focus[0].Id=="research-any-id"&&focus.All(t=>!t.Completed),"通用建议不应依赖私人任务编号");
                var chosen=new PlannerData{PlanDate=day.ToString("yyyy-MM-dd"),TodayTaskIds=new[]{"soon","soon","missing","completed"}};
                Check(Store.FocusTasks(all,chosen,day).Select(t=>t.Id).SequenceEqual(new[]{"soon"}),"今日选择未去重或未过滤完成任务");
                chosen.TodayTaskIds=new string[0];Check(Store.FocusTasks(all,chosen,day).Count==0,"明确选择空安排被改成建议");
                Check(new TaskInfo{Status="已完成",Due=day.AddDays(-1)}.Cue(day)=="已完成","已完成被误报日期已过");
                Console.WriteLine("PASS: "+checks+" data/configuration checks; no desktop data read and no GUI launched.");
            }catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
        }
    }
}

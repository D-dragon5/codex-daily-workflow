using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DailySchedule {
    internal static class ModelsTests {
        static int checks;
        static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
        static void Invalid(Action action,string message){bool rejected=false;try{action();}catch(Exception ex){rejected=ex is InvalidDataException||ex is ArgumentException;}Check(rejected,message);}
        static TaskRow Row(string id,string title,string due=null,string status="待开始"){return new TaskRow{id=id,title=title,category="工作",priority="常规",due=due,status=status,next="示例下一步"};}
        static string Ledger(params string[] rows){return "# Synthetic task ledger\n<!-- tasks:start -->\n| ID | 类别 | 任务 | 优先级 | 截止日期 | 状态 | 下一步 | 详细说明 |\n| --- | --- | --- | --- | --- | --- | --- | --- |\n"+string.Join("\n",rows)+"\n<!-- tasks:end -->\n";}
        static void WriteText(string path,string text){File.WriteAllText(path,text,new UTF8Encoding(false));}
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
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>{Row("bad id","非法ID")}});Invalid(()=>Store.LoadTasks(tasks),"非法JSON编号未拒绝");
                Store.Write(tasks,new TasksDocument{schemaVersion=1,tasks=new List<TaskRow>{Row("alpha","JSON旧标题","2030-01-01"),Row("jsonOnly","JSON独有任务")}});
                string ledger=Path.Combine(root,"ledger-fixture.md");if(File.Exists(ledger))File.Delete(ledger);
                Check(Store.LoadTasks(tasks,ledger).Select(t=>t.Id).SequenceEqual(new[]{"alpha","jsonOnly"}),"缺少总账时JSON独立模式失效");
                string missingStamp=Store.FileSignature(ledger);WriteText(ledger,"# 旧说明账本\n本文件没有规范表标记。");
                Check(Store.LoadTasks(tasks,ledger).Count==2,"无标记旧说明总账未回退JSON");Check(missingStamp!=Store.FileSignature(ledger),"文件从缺失到存在未改变签名");
                string content=Ledger("| Beta_7 | 科研 | 重复标题 | 重要 | 截止日期：2030-04-02前 | 已完成初稿，未提交 | 下一步\\|保留分隔符<br />第二行 | 完整说明<br>source: generic\\|保留 |","| alpha | 工作 | 改名后的标题 | 紧急 | 建议2030-04-03 | 进行中 | 更新下一步 | 更新说明 |","| duplicate-title | 工作 | 重复标题 | 常规 | 待核实 | 已完成（用户确认） | 无 | 完成依据 |");
                WriteText(ledger,content);var master=Store.LoadTasks(tasks,ledger);
                Check(master.Select(t=>t.Id).SequenceEqual(new[]{"Beta_7","alpha","duplicate-title"}),"规范主表未决定任务集合或顺序");
                Check(master.All(t=>t.Id!="jsonOnly")&&master[1].Title=="改名后的标题","主表删项或改名未覆盖旧JSON");
                Check(master[0].Due==new DateTime(2030,4,2)&&!master[1].Due.HasValue,"明确日期或建议日期处理错误");
                Check(master[0].Next=="下一步|保留分隔符\n第二行"&&master[0].Requirement=="完整说明\nsource: generic|保留","转义竖线、换行或通用source文字被损坏");
                Check(master[0].Progress=="已完成初稿，未提交"&&!master[0].Completed,"局部完成被隐藏");
                Check(master[2].Status=="已完成"&&master[2].Progress=="已完成（用户确认）"&&master[2].Completed,"完成规范化或原始进展丢失");
                Check(master[1].DeadlineNote=="建议2030-04-03"&&master[1].Next=="更新下一步","完整日期依据和下一步未关联");
                Check(Store.LoadTasks(Path.Combine(root,"missing-tasks.json"),ledger).Count==master.Count,"有效规范主表依赖JSON存在");
                Store.Write(Path.Combine(root,"ledger-parsed.json"),master);
                var complete=new[]{"已完成","完成","已验证完成","已完成（用户确认）","已完成（已验证）","已完成(用户确认)","已完成(已验证)"};
                foreach(string status in complete)Check(new TaskInfo{Status=status}.Completed,"精确完成声明未识别："+status);
                foreach(string status in new[]{"done","completed","DONE","Completed"}){Check(new TaskInfo{Status=status}.Completed,"原有JSON英文完成声明失效："+status);Check(Store.ParseLedger(Ledger("| english | 工作 | 完成兼容 | 常规 | 待定 | "+status+" | 无 | 完成依据 |"))[0].Status=="已完成","总账英文完成声明未归一："+status);}
                foreach(string status in new[]{"已完成初稿","完成了一部分","部分完成","done draft","partially completed",""})Check(!new TaskInfo{Status=status}.Completed,"非精确完成声明被隐藏："+status);
                foreach(string field in new[]{"2030-04-02"," 截止：2030-04-02 ","截止时间: 2030-04-02","2030-04-02截止","2030-04-02 前"})Check(Store.ParseLedger(Ledger("| valid | 工作 | 日期验证 | 常规 | "+field+" | 进行中 | 推进 | 说明 |"))[0].Due==new DateTime(2030,4,2),"完整明确日期未识别："+field);
                foreach(string field in new[]{"建议2030-04-02","预计2030-04-02","明天","4月2日","2030-04-02或2030-04-03","2030-04-02 09:00","2030-04-02之前待定","参考2030-04-02","2030-04-02\u0661"})Check(!Store.ParseLedger(Ledger("| uncertain | 工作 | 日期待定 | 常规 | "+field+" | 进行中 | 推进 | 说明 |"))[0].Due.HasValue,"非明确日期被升级为截止："+field);
                Invalid(()=>Store.ParseLedger(Ledger("| invalid | 工作 | 日期非法 | 常规 | 2030-02-30 | 进行中 | 推进 | 说明 |")),"主表非法明确日期未拒绝");
                Invalid(()=>Store.ParseLedger(content+"<!-- tasks:start -->"),"重复标记未拒绝");
                Invalid(()=>Store.ParseLedger("<!-- tasks:end -->\n<!-- tasks:start -->"),"反向标记未拒绝");
                WriteText(ledger,"<!-- tasks:start -->\n未写完的编辑");Invalid(()=>Store.LoadTasks(tasks,ledger),"不完整标记错误被JSON回退掩盖");
                Invalid(()=>Store.ParseLedger(Ledger("| first | 工作 | 缺列 | 常规 | 2030-04-02 | 进行中 | 推进 |")),"主表缺列未拒绝");
                Invalid(()=>Store.ParseLedger(Ledger("| first | 工作 | A | 常规 | 待定 | 进行中 | 推进 | 说明 |","| first | 工作 | B | 常规 | 待定 | 进行中 | 推进 | 说明 |")),"主表重复编号未拒绝");
                Invalid(()=>Store.ParseLedger(Ledger("| 1bad | 工作 | A | 常规 | 待定 | 进行中 | 推进 | 说明 |")),"主表非法编号未拒绝");
                Invalid(()=>Store.ParseLedger(Ledger("| title | 工作 |  | 常规 | 待定 | 进行中 | 推进 | 说明 |")),"主表空标题未拒绝");
                Invalid(()=>Store.ParseLedger(Ledger().Replace("| 详细说明 |","| 错误列 |")),"主表错误列名未拒绝");
                Invalid(()=>Store.ParseLedger(Ledger().Replace("| --- | --- |","| -- | --- |")),"主表非法分隔行未拒绝");
                Check(Store.ParseLedger(Ledger()).Count==0,"空规范表未支持");
                File.Delete(ledger);Check(Store.FileSignature(ledger)==missingStamp,"文件删除未改变存在状态签名");WriteText(ledger,content);
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
                chosen.TodayTaskIds=new[]{"研究示例","重复标题"};Check(Store.FocusTasks(all.Concat(master).ToList(),chosen,day).Select(t=>t.Id).SequenceEqual(new[]{"research-any-id"}),"唯一标题兼容或重名排除错误");
                chosen.TodayTaskIds=new[]{"alpha"};Check(Store.FocusTasks(master,chosen,day).Single().Title=="改名后的标题","改名后稳定编号选择失效");
                var collision=new List<TaskInfo>{new TaskInfo{Id="alpha",Title="编号优先",Status="待开始"},new TaskInfo{Id="beta",Title="alpha",Status="待开始"}};Check(Store.FocusTasks(collision,chosen,day).Single().Id=="alpha","稳定编号与标题冲突时未优先编号");
                Check(new TaskInfo{Status="已完成",Due=day.AddDays(-1)}.Cue(day)=="已完成","已完成被误报日期已过");
                Console.WriteLine("PASS: "+checks+" data/configuration checks; no desktop data read and no GUI launched.");
            }catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
        }
    }
}

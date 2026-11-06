using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

[DataContract]
internal sealed class PetCalendarPlan
{
    [DataMember(Order=0)] public string Text = "";
    [DataMember(Order=1)] public bool Done;
    [DataMember(Order=2,EmitDefaultValue=false)] public string Start;
    [DataMember(Order=3,EmitDefaultValue=false)] public string End;
}

[DataContract]
internal sealed class PetCalendarDay
{
    [DataMember(Order=0)] public int Version = 1;
    [DataMember(Order=1)] public string Date;
    [DataMember(Order=2)] public List<PetCalendarPlan> Plans = new List<PetCalendarPlan>();
    [DataMember(Order=3)] public string Journal = "";
    [DataMember(Order=4)] public string UpdatedAt;
    internal bool HasContent { get { return Plans.Count > 0 || !String.IsNullOrWhiteSpace(Journal); } }
}

internal sealed class PetCalendarStore
{
    internal readonly string DirectoryPath;
    internal readonly bool ReadOnly;
    internal PetCalendarStore(string root, bool readOnly=false)
    { DirectoryPath=Path.Combine(Path.GetFullPath(root),"data","calendar");ReadOnly=readOnly; }
    internal static string DateKey(DateTime date) { return date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture); }
    internal string FilePath(DateTime date) { return Path.Combine(DirectoryPath,DateKey(date)+".json"); }
    internal PetCalendarDay Load(DateTime date)
    {
        string path=FilePath(date);
        if(!File.Exists(path))return new PetCalendarDay { Date=DateKey(date) };
        try
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var day=(PetCalendarDay)new DataContractJsonSerializer(typeof(PetCalendarDay)).ReadObject(stream);
                Validate(day,date);return day;
            }
        }
        catch(SerializationException error) { throw new InvalidDataException("日历文件格式不正确："+Path.GetFileName(path),error); }
    }
    internal static void Validate(PetCalendarDay day,DateTime date)
    {
        if(day==null||day.Version!=1||day.Date!=DateKey(date)||day.Plans==null||day.Journal==null||
            day.Plans.Any(plan=>plan==null||String.IsNullOrWhiteSpace(plan.Text)))
            throw new InvalidDataException("日历数据无效或版本不支持："+DateKey(date)+".json");
        foreach(var plan in day.Plans)
        {
            bool start=String.IsNullOrEmpty(plan.Start),end=String.IsNullOrEmpty(plan.End);
            if(start!=end||!start&&(Minutes(plan.Start)<0||Minutes(plan.End)<=Minutes(plan.Start)))
                throw new InvalidDataException("计划时间请填写 HH:mm，结束时间需晚于开始时间；全天计划的时间留空。");
        }
    }
    internal static int Minutes(string text)
    {
        if(text=="24:00")return 1440;
        DateTime time;
        return DateTime.TryParseExact(text,new[] {"H:mm","HH:mm"},CultureInfo.InvariantCulture,DateTimeStyles.None,out time) ? time.Hour*60+time.Minute : -1;
    }
    internal static string Clock(int minutes) { return (minutes/60).ToString("D2")+":"+(minutes%60).ToString("D2"); }
    internal static PetCalendarDay Clone(PetCalendarDay day)
    { using(var stream=new MemoryStream(Bytes(day))){return (PetCalendarDay)new DataContractJsonSerializer(typeof(PetCalendarDay)).ReadObject(stream);} }
    internal static byte[] Bytes(PetCalendarDay day)
    { using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(PetCalendarDay)).WriteObject(stream,day);return stream.ToArray();} }
    internal List<CalendarEntry> Query(string keyword,int state,bool includePlans,bool includeJournal,out List<string> errors)
    {
        errors=new List<string>();var entries=new List<CalendarEntry>();
        if(!Directory.Exists(DirectoryPath))return entries;
        foreach(string file in Directory.GetFiles(DirectoryPath,"*.json",SearchOption.TopDirectoryOnly).OrderBy(path=>path))
        {
            DateTime date;
            if(!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))continue;
            PetCalendarDay day;
            try { day=Load(date); }
            catch(Exception error){if(!IsDataError(error))throw;errors.Add(Path.GetFileName(file)+"："+error.Message);continue;}
            bool dateMatch=!String.IsNullOrWhiteSpace(keyword)&&DateKey(date).IndexOf(keyword.Trim(),StringComparison.OrdinalIgnoreCase)>=0;
            if(includePlans)
            for(int i=0;i<day.Plans.Count;i++)
            {
                var plan=day.Plans[i];
                if(state==1&&plan.Done||state==2&&!plan.Done||!dateMatch&&!Matches(plan.Text,keyword))continue;
                entries.Add(new CalendarEntry {Date=date,Index=i,Text=plan.Text,Done=plan.Done,Start=plan.Start,End=plan.End,Stamp=day.UpdatedAt});
            }
            if(includeJournal&&state==0&&!String.IsNullOrWhiteSpace(day.Journal)&&(dateMatch||Matches(day.Journal,keyword)))
                entries.Add(new CalendarEntry {Date=date,Index=-1,Text=day.Journal,Stamp=day.UpdatedAt});
        }
        return entries;
    }
    static bool Matches(string text,string keyword)
    { return String.IsNullOrWhiteSpace(keyword)||text.IndexOf(keyword.Trim(),StringComparison.CurrentCultureIgnoreCase)>=0; }
    Dictionary<DateTime,PetCalendarDay> undoBefore,undoAfter;
    internal bool CanUndo { get { return undoBefore!=null; } }
    internal int ApplyBatch(IEnumerable<CalendarEntry> selection,CalendarBatchAction action,DateTime target,int? slotMinutes=null)
    {
        if(ReadOnly)throw new InvalidOperationException("预览模式不修改日历数据。");
        var items=selection.GroupBy(item=>DateKey(item.Date)+":"+item.Index).Select(group=>group.First())
            .Where(item=>action==CalendarBatchAction.Delete||item.Index>=0).ToList();
        if(items.Count==0)return 0;
        var before=new Dictionary<DateTime,PetCalendarDay>();
        foreach(var item in items)
        {
            if(!before.ContainsKey(item.Date))before.Add(item.Date,Load(item.Date));
            var day=before[item.Date];
            bool matches=day.UpdatedAt==item.Stamp&&(item.Index<0 ? day.Journal==item.Text : item.Index<day.Plans.Count&&
                day.Plans[item.Index].Text==item.Text&&day.Plans[item.Index].Done==item.Done&&day.Plans[item.Index].Start==item.Start&&day.Plans[item.Index].End==item.End);
            if(!matches)throw new InvalidDataException("记录已经变化，请刷新列表后重新选择。");
        }
        target=target.Date;
        if(action==CalendarBatchAction.Move&&!before.ContainsKey(target))before.Add(target,Load(target));
        var after=before.ToDictionary(pair=>pair.Key,pair=>Clone(pair.Value));
        foreach(var group in items.GroupBy(item=>item.Date))
        foreach(var item in group.OrderByDescending(item=>item.Index))
        {
            var day=after[item.Date];
            if(action==CalendarBatchAction.Complete)day.Plans[item.Index].Done=true;
            else if(action==CalendarBatchAction.Reopen)day.Plans[item.Index].Done=false;
            else if(action==CalendarBatchAction.Delete){if(item.Index<0)day.Journal="";else day.Plans.RemoveAt(item.Index);}
            else if(item.Date!=target)day.Plans.RemoveAt(item.Index);
        }
        // Append moved plans in their original chronological order, including
        // duplicate titles. Daily journals stay attached to their own dates.
        if(action==CalendarBatchAction.Move)
            foreach(var item in items.OrderBy(item=>item.Date).ThenBy(item=>item.Index))
            {
                var original=before[item.Date].Plans[item.Index];
                var moved=item.Date==target ? after[target].Plans[item.Index] : new PetCalendarPlan {Text=original.Text,Done=original.Done,Start=original.Start,End=original.End};
                if(slotMinutes.HasValue)
                {
                    if(slotMinutes.Value<0)moved.Start=moved.End=null;
                    else
                    {
                        int duration=String.IsNullOrEmpty(original.Start) ? 30 : Minutes(original.End)-Minutes(original.Start);
                        int start=Math.Min(slotMinutes.Value,1440-duration);moved.Start=Clock(start);moved.End=Clock(start+duration);
                    }
                }
                if(item.Date!=target)after[target].Plans.Add(moved);
            }
        CommitBatch(before,after);
        undoBefore=before;undoAfter=after;
        return items.Count;
    }
    void CommitBatch(Dictionary<DateTime,PetCalendarDay> before,Dictionary<DateTime,PetCalendarDay> after)
    {
        // Persist a full recovery snapshot before the first write. Per-file
        // replacement is atomic; on a multi-day failure restore all written
        // days and retain the snapshot even if a second I/O error blocks repair.
        string history=Path.Combine(DirectoryPath,"history",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(history);
        foreach(var pair in before)File.WriteAllBytes(Path.Combine(history,DateKey(pair.Key)+".json"),Bytes(pair.Value));
        var written=new List<DateTime>();
        try { foreach(var pair in after.OrderBy(pair=>pair.Key)){Save(pair.Key,pair.Value);written.Add(pair.Key);} }
        catch(Exception error)
        {
            if(!IsDataError(error))throw;
            var failed=new List<string>();
            foreach(var date in written)
                try{Save(date,Clone(before[date]));}catch(Exception rollback){if(!IsDataError(rollback))throw;failed.Add(DateKey(date));}
            throw new IOException("批量操作未完成"+(failed.Count==0 ? "，已还原已写入的日期。" : "，以下日期需要从恢复快照还原："+String.Join("、",failed)+"。")+
                "恢复快照："+history+"。 "+error.Message,error);
        }
    }
    internal bool UndoBatch()
    {
        if(ReadOnly)throw new InvalidOperationException("预览模式不修改日历数据。");
        if(undoBefore==null)return false;
        foreach(var pair in undoAfter)
            if(!Bytes(Load(pair.Key)).SequenceEqual(Bytes(pair.Value)))throw new InvalidDataException("批量操作后这些日期已有新修改，不能覆盖；请从备份恢复需要的内容。");
        CommitBatch(undoAfter,undoBefore.ToDictionary(pair=>pair.Key,pair=>Clone(pair.Value)));
        undoBefore=undoAfter=null;return true;
    }
    internal void Save(DateTime date,PetCalendarDay day)
    {
        if(ReadOnly)throw new InvalidOperationException("预览模式不保存日历数据。");
        Validate(day,date);
        // Refuse to replace a damaged/unknown file with an empty edit buffer.
        // The original and its last successful backup remain available.
        Load(date);
        Directory.CreateDirectory(DirectoryPath);
        string path=FilePath(date),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            day.UpdatedAt=DateTimeOffset.Now.ToString("o",CultureInfo.InvariantCulture);
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                new DataContractJsonSerializer(typeof(PetCalendarDay)).WriteObject(stream,day);
                stream.Flush(true);
            }
            if(File.Exists(path))File.Replace(temporary,path,path+".bak",true);
            else File.Move(temporary,path);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    internal static bool IsDataError(Exception error)
    { return error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is System.Security.SecurityException; }
}

internal enum CalendarBatchAction { Complete, Reopen, Move, Delete }
internal enum CalendarView { Day, WorkWeek, Week, Month, List }
[Serializable]
internal sealed class CalendarEntry
{
    internal DateTime Date;
    internal int Index;
    internal string Text,Start,End,Stamp;
    internal bool Done;
}

using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.NivelesNYv1;

namespace NinjaTrader.NinjaScript.Strategies.RechazoNY
{
    public enum FranjaNY { Asia, Europa, America, Ninguna }
    public sealed class SenalNY
    {
        public string Id;
        public int Direction, BreakBar;
        public DateTime BreakTime, ConfirmTime;
        public double BreakK;
        public NivelNY Level;
        public List<NivelNY> Secondary=new List<NivelNY>();
    }
    public sealed class DetectorRechazoNY
    {
        private readonly Dictionary<string,SenalNY> pending=new Dictionary<string,SenalNY>();
        private long serial;
        public Action<string,SenalNY> Event;
        public void Reset(string reason)
        { foreach(var s in pending.Values) Emit(reason,s); pending.Clear(); }
        private void Emit(string reason,SenalNY s) { if(Event!=null) Event(reason,s); }
        // Levels must already exist at the START of this signal bar.
        public List<SenalNY> Bar(int index,DateTime at,double previous,double close,double k,
            IEnumerable<NivelNY> levels,int window,double overbought,double oversold,ISet<string> excluded)
        {
            var usable=levels.Where(x=>x.Status==NivelEstado.FIXED && x.SourceComplete &&
                x.AvailableFromNY<at && x.Family!=FamiliaNivel.CURRENT_RTH && !double.IsNaN(x.Price) && !excluded.Contains(x.LevelId))
                .ToDictionary(x=>x.LevelId);
            var confirmed=new List<SenalNY>();
            foreach(var key in pending.Keys.ToList())
            {
                var s=pending[key];
                if(!usable.ContainsKey(key)) { Emit("LEVEL_EXPIRED",s); pending.Remove(key); continue; }
                int age=index-s.BreakBar;
                if(age>window) { Emit("EXPIRED",s); pending.Remove(key); continue; }
                if(age>0 && (s.Direction<0?close<s.Level.Price:close>s.Level.Price))
                { s.ConfirmTime=at; confirmed.Add(s); Emit("REJECTION",s); pending.Remove(key); }
            }
            // Do not reinterpret the confirmation candle as another break on that same ID.
            var consumed=new HashSet<string>(confirmed.Select(x=>x.Level.LevelId));
            foreach(var n in usable.Values)
            {
                if(pending.ContainsKey(n.LevelId)||consumed.Contains(n.LevelId)) continue;
                int direction=previous<=n.Price && close>n.Price && k>=overbought?-1:
                    previous>=n.Price && close<n.Price && k<=oversold?1:0;
                if(direction==0) continue;
                var s=new SenalNY {Id="SIG"+(++serial),Direction=direction,BreakBar=index,BreakTime=at,BreakK=k,Level=n};
                pending.Add(n.LevelId,s); Emit("BREAK",s);
            }
            return confirmed;
        }
        // Anchored grouping, never chained A~B~C. Only actually confirmed breaks become signals.
        public static List<SenalNY> Group(List<SenalNY> signals,IEnumerable<NivelNY> known,double distance)
        {
            var remaining=signals.OrderBy(x=>x.Level.SourceStartNY).ThenBy(x=>x.Level.LevelId,StringComparer.Ordinal).ToList();
            var result=new List<SenalNY>();
            while(remaining.Count>0)
            {
                var principal=remaining[0]; remaining.RemoveAt(0);
                var nearby=remaining.Where(x=>x.Direction==principal.Direction && Math.Abs(x.Level.Price-principal.Level.Price)<=distance).ToList();
                foreach(var s in nearby) remaining.Remove(s);
                principal.Secondary=known.Where(x=>x.LevelId!=principal.Level.LevelId &&
                    Math.Abs(x.Price-principal.Level.Price)<=distance).OrderBy(x=>x.SourceStartNY).ThenBy(x=>x.LevelId,StringComparer.Ordinal).ToList();
                result.Add(principal);
            }
            return result;
        }
    }
    public sealed class MasFranjasNY
    {
        private sealed class Sample { public DateTime Start,End,Next; public int Count; public bool Gap; public double High=double.MinValue,Low=double.MaxValue; }
        private readonly Dictionary<string,Sample> active=new Dictionary<string,Sample>();
        private readonly Dictionary<FranjaNY,List<double>> ranges=new Dictionary<FranjaNY,List<double>>();
        private readonly Dictionary<FranjaNY,List<DateTime>> dates=new Dictionary<FranjaNY,List<DateTime>>();
        public Action<string,string> Event;
        public static FranjaNY Type(DateTime at)
        {
            var t=at.TimeOfDay;
            if(t>=TimeSpan.FromHours(18)||t<TimeSpan.FromHours(3)) return FranjaNY.Asia;
            if(t<TimeSpan.FromHours(9.5)) return FranjaNY.Europa;
            if(t<TimeSpan.FromHours(16)) return FranjaNY.America;
            return FranjaNY.Ninguna;
        }
        public void Minute(DateTime closed,double high,double low)
        {
            DateTime open=closed.AddMinutes(-1); var kind=Type(open); if(kind==FranjaNY.Ninguna) return;
            DateTime day=open.TimeOfDay>=TimeSpan.FromHours(18)?open.Date.AddDays(1):open.Date;
            DateTime official; bool regular; string why;
            if(!CalendarioSesionesNY.Resolve(day,out official,out regular,out why)||official==DateTime.MinValue) return;
            DateTime start=kind==FranjaNY.Asia?day.AddDays(-1).AddHours(18):kind==FranjaNY.Europa?day.AddHours(3):day.AddHours(9.5);
            DateTime end=kind==FranjaNY.Asia?day.AddHours(3):kind==FranjaNY.Europa?day.AddHours(9.5):day.AddHours(16);
            // Short official windows are not full samples of the same type.
            if(official<end) { if(Event!=null && open==start) Event("MAS_OFFICIAL_SHORT",kind+":"+day.ToString("s")); return; }
            string key=kind.ToString(); Sample s;
            if(!active.TryGetValue(key,out s)||s.Start!=start)
            {
                if(s!=null && s.Count>0 && Event!=null) Event("MAS_INCOMPLETE",key+":"+s.Start.ToString("s"));
                s=new Sample {Start=start,End=end,Next=start}; active[key]=s;
            }
            if(open!=s.Next) s.Gap=true;
            s.Next=closed; s.Count++; s.High=Math.Max(s.High,high); s.Low=Math.Min(s.Low,low);
            if(closed==end)
            {
                if(!s.Gap && s.Count==(int)(end-start).TotalMinutes)
                {
                    List<double> values; if(!ranges.TryGetValue(kind,out values)) {values=new List<double>();ranges.Add(kind,values);}
                    values.Add(s.High-s.Low); if(values.Count>50) values.RemoveAt(0);
                    List<DateTime> history;if(!dates.TryGetValue(kind,out history)){history=new List<DateTime>();dates.Add(kind,history);}
                    history.Add(day);if(history.Count>50)history.RemoveAt(0);
                    if(Event!=null) Event("MAS_COMPLETE",key+":"+day.ToString("s")+":"+(s.High-s.Low));
                }
                else if(Event!=null) Event("MAS_INCOMPLETE",key+":"+day.ToString("s"));
                active.Remove(key);
            }
        }
        public bool TryGet(FranjaNY kind,int count,out double value)
        {
            List<double> values; value=double.NaN;
            if(!ranges.TryGetValue(kind,out values)||values.Count<count) return false;
            value=values.Skip(values.Count-count).Average(); return value>0;
        }
        public string Sources(FranjaNY kind,int count)
        {
            List<DateTime> history;if(!dates.TryGetValue(kind,out history))return "";
            return string.Join("|",history.Skip(Math.Max(0,history.Count-count)).Select(d=>d.ToString("yyyy-MM-dd")));
        }
    }
    public sealed class SalidasNY { public double Stop,Target,ExpectedLoss,ExpectedGain; }
    public static class RiesgoRechazoNY
    {
        public static SalidasNY Levels(int direction,int quantity,double average,double realised,int totalBought,
            double point,double tick,double fee,double budget,double target)
        {
            if((direction!=1&&direction!=-1)||quantity<=0||point<=0||tick<=0||fee<0||budget<=0||target<=0||
                double.IsNaN(average)||double.IsInfinity(average)) throw new ArgumentException("Invalid joint risk inputs");
            decimal q=quantity,v=(decimal)point,t=(decimal)tick,a=(decimal)average,c=(decimal)fee*totalBought;
            decimal b=(decimal)budget,r=(decimal)realised,goal=(decimal)target;
            if(b+r<=c) throw new ArgumentException("Budget does not cover costs and realised losses");
            decimal st=a-direction*(b+r-c)/(v*q),tp=a+direction*(goal-r+c)/(v*q);
            st=(direction>0?decimal.Ceiling(st/t):decimal.Floor(st/t))*t;
            tp=(direction>0?decimal.Ceiling(tp/t):decimal.Floor(tp/t))*t;
            return new SalidasNY {Stop=(double)st,Target=(double)tp,
                ExpectedLoss=(double)(-(r+direction*(st-a)*q*v-c)),ExpectedGain=(double)(r+direction*(tp-a)*q*v-c)};
        }
        public static int Addition(int first,int current,int maximum) { return Math.Max(0,Math.Min(first,maximum-current)); }
        public static bool Blocked(DateTime at,TimeSpan from,TimeSpan until,int marginMinutes,out string reason)
        {
            reason=""; TimeSpan t=at.TimeOfDay;
            bool daily=from<=until?t>=from&&t<until:t>=from||t<until;
            DateTime day=t>=TimeSpan.FromHours(18)?at.Date.AddDays(1):at.Date;
            DateTime end; bool regular; string why;
            if(!CalendarioSesionesNY.Resolve(day,out end,out regular,out why)||end==DateTime.MinValue)
            {reason="CALENDAR:"+why;return true;}
            if(daily) {reason="DAILY_BLOCK";return true;}
            if(end.TimeOfDay<TimeSpan.FromHours(16) && at>=end.AddMinutes(-marginMinutes))
            {reason="EARLY_CLOSE:"+why;return true;}
            return false;
        }
        public static bool Favorable(int direction,double price,double origin)
        {return direction>0?price<origin:price>origin;}
        public static bool CrossedCutoff(DateTime previous,DateTime current,TimeSpan configured,int margin)
        {
            if(previous==DateTime.MinValue||current<=previous)return false;
            for(DateTime day=previous.Date;day<=current.Date;day=day.AddDays(1))
            {
                DateTime end;bool regular;string why;
                if(!CalendarioSesionesNY.Resolve(day,out end,out regular,out why)||end==DateTime.MinValue)continue;
                DateTime cutoff=day.Add(configured);
                if(end.TimeOfDay<TimeSpan.FromHours(16)&&end.AddMinutes(-margin)<cutoff)cutoff=end.AddMinutes(-margin);
                if(previous<cutoff&&current>=cutoff)return true;
            }
            return false;
        }
    }
    public sealed class PausaRechazoNY
    {
        private DateTime exit=DateTime.MinValue,last=DateTime.MinValue; public int CompleteBars {get;private set;}
        public void Exit(DateTime at) {exit=at;last=DateTime.MinValue;CompleteBars=0;}
        public void Bar(DateTime start,DateTime end)
        {if(end!=last && exit!=DateTime.MinValue && start>exit) CompleteBars++; last=end;}
        public bool Ready(int count) {return exit==DateTime.MinValue || CompleteBars>=count;}
    }
}

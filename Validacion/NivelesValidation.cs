using System;
using System.Linq;
using NinjaTrader.NinjaScript.NivelesNYv1;

// Synthetic public data only. No NinjaTrader runtime or market-data dependency.
class NivelesValidation
{
    static int checks;
    static DateTime D(string s) { return DateTime.SpecifyKind(DateTime.Parse(s,System.Globalization.CultureInfo.InvariantCulture),DateTimeKind.Unspecified); }
    static void Check(bool ok,string name) { checks++; if(!ok) throw new Exception("FAIL: "+name); }
    static void Throws(Action a,string name) { bool threw=false; try { a(); } catch(ArgumentException) { threw=true; } Check(threw,name); }
    static MotorNivelesNY Engine(int rth=5,int other=1,double tick=.25,string instrument="MNQ 12-26")
    { return new MotorNivelesNY(instrument,tick,rth,other,18,"synthetic ETH NY"); }
    static void Bar(MotorNivelesNY m,DateTime close,double p=100,DateTime end=default(DateTime))
    { m.ProcessMinute(close,p,p+1,p-1,p+.5,end); }
    static void Feed(MotorNivelesNY m,DateTime start,DateTime end,double p=100,DateTime template=default(DateTime),DateTime skip=default(DateTime))
    { for(DateTime close=start.AddMinutes(1);close<=end;close=close.AddMinutes(1)) if(close!=skip) Bar(m,close,p,template); }
    static NivelNY N(MotorNivelesNY m,string code,DateTime at,DateTime day=default(DateTime))
    { return m.GetLevelsAt(at).SingleOrDefault(x=>x.FamilyCode==code && (day==default(DateTime)||x.TradingDate==day.Date)); }
    static void Main()
    {
        try
        {
            BoundariesAndNoLookahead(); MissingAndPartial(); HolidaysAndTemplate(); RetentionAndIdentity(); InputAndDst();
            Console.WriteLine("PASS: "+checks+" comprobaciones sintéticas de niveles. Sin F5 ni ejecución NinjaTrader.");
        }
        catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
    }
    static void BoundariesAndNoLookahead()
    {
        MotorNivelesNY m=Engine(); DateTime day=D("2026-11-16");
        Feed(m,day.AddDays(-1).AddHours(18),day.AddHours(9.5));
        NivelNY night=N(m,"OVERNIGHT_HIGH",day.AddHours(9.5));
        Check(night.Status==NivelEstado.FIXED && night.SourceComplete && night.ObservedMinutes==930,"full Sunday overnight / Monday trading date");
        Check(night.TradingDate==day && night.Price==101,"overnight price and date");
        Check(double.IsNaN(N(m,"RTH_OPEN",day.AddHours(9.5)).Price),"09:30 closed label is not RTH open");
        Check(N(m,"PREV_RTH_HIGH",day.AddHours(9.5))==null,"no previous RTH before completion");
        Feed(m,day.AddHours(9.5),day.AddHours(9.75));
        NivelNY first=N(m,"FIRST_HOUR_HIGH",day.AddHours(9.75));
        Check(first.Status==NivelEstado.FORMING && first.Price==101 && !first.SourceComplete,"first hour forming");
        Check(N(m,"RTH_OPEN",day.AddHours(9.5).AddMinutes(1)).Price==100,"exact first-minute opening");
        Check(N(m,"RTH_OPEN",day.AddHours(9.5).AddMinutes(1)).AvailableFromNY==day.AddHours(9.5).AddMinutes(1),"open availability is 09:31");
        Feed(m,day.AddHours(9.75),day.AddHours(10.5),200);
        NivelNY fixedHour=N(m,"FIRST_HOUR_HIGH",day.AddHours(10.5));
        Check(fixedHour.Price==201 && fixedHour.Status==NivelEstado.FIXED && fixedHour.ObservedMinutes==60,"first hour final only at10:30");
        Check(N(m,"FIRST_HOUR_HIGH",day.AddHours(10.5).AddMinutes(-1)).Status==NivelEstado.FORMING,"10:29 not fixed");
        Check(m.GetLevelAt(fixedHour.LevelId,day.AddHours(9.75)).Price==101,"no retroactive final extreme");
        Check(first.Price==101 && first.Status==NivelEstado.FORMING,"immutable prior snapshot");
        Check(fixedHour.LevelId==first.LevelId && fixedHour.PreviousPrice==201,"stable ID through revisions");
        Feed(m,day.AddHours(10.5),day.AddHours(16),300);
        Check(N(m,"PREV_RTH_HIGH",day.AddHours(16).AddMinutes(-1))==null,"prior RTH not published early");
        NivelNY prior=N(m,"PREV_RTH_HIGH",day.AddHours(16));
        Check(prior.Price==301 && prior.SourceComplete && prior.ObservedMinutes==390 && prior.ExpectedMinutes==390,"full RTH high");
        Check(N(m,"PREV_RTH_LOW",day.AddHours(16)).Price==99,"full RTH low");
        Check(N(m,"PREV_RTH_CLOSE",day.AddHours(16)).Price==300.5,"close is closing minute, not settlement");
        Check(prior.AgeSessions==1 && N(m,"CURRENT_RTH_HIGH",day.AddHours(16)).AgeSessions==0,"age convention");
        Check(m.GetLevelsAt(day.AddHours(16)).Select(x=>x.Family).Distinct().Count()==6,"six families exposed");
        Check(m.GetLevelsAt(day.AddHours(16)).All(x=>x.AvailableFromNY<=day.AddHours(16) && x.CalendarVersion.Length>0 && x.SeriesIdentity.Length>0 && x.Priority==0),"metadata complete without priority");
        Check(m.GetLevelsAt(day.AddHours(16)).All(x=>x.LevelId.Contains("MNQ 12-26|")),"instrument included in IDs");
        Bar(m,day.AddHours(16).AddMinutes(1),500);
        Check(N(m,"PREV_RTH_HIGH",m.ObservedAtNY).Price==301 && N(m,"FIRST_HOUR_HIGH",m.ObservedAtNY).Price==201,"later data cannot rewrite fixed ranges");
    }
    static void MissingAndPartial()
    {
        DateTime day=D("2026-11-16"); MotorNivelesNY m=Engine();
        Feed(m,day.AddHours(9.5).AddMinutes(1),day.AddHours(16),100,default(DateTime),day.AddHours(11));
        Check(N(m,"RTH_OPEN",m.ObservedAtNY).Status==NivelEstado.UNAVAILABLE,"late load never substitutes opening");
        NivelNY high=N(m,"PREV_RTH_HIGH",m.ObservedAtNY);
        Check(high.Status==NivelEstado.INCOMPLETE && !high.SourceComplete && high.Reason.StartsWith("GAP_EXPECTED_"),"missing head/interior flagged");
        Check(N(m,"PREV_RTH_CLOSE",m.ObservedAtNY).Price==100.5 && !N(m,"PREV_RTH_CLOSE",m.ObservedAtNY).SourceComplete,"observed closing price with incomplete source");
        Check(N(m,"OVERNIGHT_HIGH",m.ObservedAtNY).Status==NivelEstado.UNAVAILABLE,"no invented overnight");
        MotorNivelesNY tail=Engine(); Feed(tail,day.AddHours(9.5),day.AddHours(16).AddMinutes(-1));
        Check(N(tail,"CURRENT_RTH_HIGH",tail.ObservedAtNY).Status==NivelEstado.FORMING,"no clock-based completion beyond last bar");
        Bar(tail,day.AddHours(16).AddMinutes(1));
        Check(N(tail,"PREV_RTH_HIGH",tail.ObservedAtNY).Status==NivelEstado.INCOMPLETE,"tail absence classified at next observation");
        Check(N(tail,"PREV_RTH_CLOSE",tail.ObservedAtNY).Status==NivelEstado.UNAVAILABLE,"missing closing minute never replaced");
        MotorNivelesNY gap=Engine(); Feed(gap,day.AddHours(9.5),day.AddHours(10.5),100,default(DateTime),day.AddHours(10));
        Check(N(gap,"FIRST_HOUR_HIGH",gap.ObservedAtNY).Status==NivelEstado.INCOMPLETE,"interior minute missing");
        Check(N(gap,"RTH_OPEN",gap.ObservedAtNY).SourceComplete,"opening independent of later gaps");
    }
    static void HolidaysAndTemplate()
    {
        foreach(string ds in new[]{"2026-06-19","2026-07-03","2026-09-07"})
        {
            DateTime day=D(ds); MotorNivelesNY m=Engine(); Feed(m,day.AddHours(9.5),day.AddHours(13));
            NivelNY n=N(m,"PREV_RTH_HIGH",m.ObservedAtNY);
            Check(n!=null && n.SourceComplete && n.ExpectedMinutes==210 && n.SourceEndNY==day.AddHours(13),"short RTH included "+ds);
            Check(N(m,"FIRST_HOUR_HIGH",m.ObservedAtNY).Status==NivelEstado.FIXED,"short day first hour "+ds);
        }
        foreach(string ds in new[]{"2025-01-09","2026-04-03"})
        {
            DateTime day=D(ds); MotorNivelesNY m=Engine(); DateTime end=day.AddHours(ds.StartsWith("2025")?9.5:9.25);
            Feed(m,day.AddDays(-1).AddHours(18),end);
            Check(m.GetLevelsAt(end).All(x=>x.Family!=FamiliaNivel.PREV_RTH),"no fabricated RTH "+ds);
            Check(N(m,"OVERNIGHT_HIGH",end).Status==NivelEstado.FIXED,"overnight until official early end "+ds);
            Check(N(m,"RTH_OPEN",end).Reason.StartsWith("NO_RTH:"),"no-RTH reason "+ds);
        }
        MotorNivelesNY custom=Engine(); DateTime d=D("2026-11-16");
        Feed(custom,d.AddHours(9.5),d.AddHours(9.75),100,d.AddHours(9.75));
        Check(N(custom,"PREV_RTH_HIGH",custom.ObservedAtNY).SourceComplete,"effective template close respected");
        Check(N(custom,"FIRST_HOUR_HIGH",custom.ObservedAtNY).Status==NivelEstado.INCOMPLETE,"15-min RTH is not complete first hour");
        Check(N(custom,"FIRST_HOUR_HIGH",custom.ObservedAtNY).Reason.Contains("SHORTENED"),"short first-hour reason");
        MotorNivelesNY holiday=Engine(); Bar(holiday,D("2026-12-25 10:00"));
        Check(holiday.RetainedRthPeriods==0 && holiday.GetLevelsAt(holiday.ObservedAtNY).Count==0,"closed holiday does not consume retention");
    }
    static void RetentionAndIdentity()
    {
        MotorNivelesNY m=Engine(); DateTime monday=D("2026-11-09");
        for(int i=0;i<5;i++) Feed(m,monday.AddDays(i).AddHours(9.5),monday.AddDays(i).AddHours(16),100+i);
        Check(m.GetLevelsAt(m.ObservedAtNY).Count(x=>x.Family==FamiliaNivel.PREV_RTH)==10,"default five RTH pairs");
        Bar(m,D("2026-11-15 18:01"));
        Check(m.GetLevelsAt(m.ObservedAtNY).Count(x=>x.Family==FamiliaNivel.PREV_RTH)==10,"weekend doesn't consume RTH retention");
        Check(N(m,"PREV_RTH_HIGH",m.ObservedAtNY,monday).AgeSessions==5,"age counts RTH not calendar days");
        Check(N(m,"FIRST_HOUR_HIGH",m.ObservedAtNY,monday)==null,"other-family retention independent");
        Feed(m,D("2026-11-16 09:30"),D("2026-11-16 16:00"));
        Check(N(m,"PREV_RTH_HIGH",m.ObservedAtNY,monday)==null,"sixth RTH evicts oldest");
        Check(m.GetLevelsAt(m.ObservedAtNY).Count(x=>x.Family==FamiliaNivel.PREV_RTH)==10,"bounded five after eviction");
        MotorNivelesNY missing=Engine(2); Feed(missing,monday.AddHours(9.5),monday.AddHours(16));
        Feed(missing,monday.AddDays(2).AddHours(9.5),monday.AddDays(2).AddHours(16));
        Check(N(missing,"PREV_RTH_HIGH",missing.ObservedAtNY,monday)==null,"missing expected RTH consumes slot");
        Check(N(missing,"PREV_RTH_HIGH",missing.ObservedAtNY,monday.AddDays(1)).Status==NivelEstado.UNAVAILABLE,"absent session placeholder");
        Check(N(missing,"PREV_RTH_HIGH",missing.ObservedAtNY,monday.AddDays(1)).Reason=="DATA_ABSENT","absent reason");
        MotorNivelesNY a=Engine(),b=Engine(),c=Engine(5,1,.25,"NQ 12-26");
        Feed(a,monday.AddHours(9.5),monday.AddHours(10.5)); Feed(b,monday.AddHours(9.5),monday.AddHours(10.5)); Feed(c,monday.AddHours(9.5),monday.AddHours(10.5));
        Check(N(a,"FIRST_HOUR_HIGH",a.ObservedAtNY).LevelId==N(b,"FIRST_HOUR_HIGH",b.ObservedAtNY).LevelId,"deterministic replay ID");
        Check(N(a,"FIRST_HOUR_HIGH",a.ObservedAtNY).LevelId!=N(c,"FIRST_HOUR_HIGH",c.ObservedAtNY).LevelId,"separate contract IDs");
        Check(a.GetLevelsAt(a.ObservedAtNY).Select(x=>x.LevelId+":"+x.Price+":"+x.Status+":"+x.AvailableFromNY.Ticks).SequenceEqual(b.GetLevelsAt(b.ObservedAtNY).Select(x=>x.LevelId+":"+x.Price+":"+x.Status+":"+x.AvailableFromNY.Ticks)),"deterministic observations");
    }
    static void InputAndDst()
    {
        foreach(string ds in new[]{"2026-03-06","2026-03-09","2026-10-30","2026-11-02"})
        {
            MotorNivelesNY m=Engine(); DateTime d=D(ds); Feed(m,d.AddHours(9.5),d.AddHours(16));
            Check(N(m,"PREV_RTH_HIGH",m.ObservedAtNY).SourceEndNY==d.AddHours(16),"NY RTH unaffected by DST offset "+ds);
            Check(N(m,"RTH_OPEN",m.ObservedAtNY).SourceStartNY==d.AddHours(9.5),"NY opening "+ds);
        }
        MotorNivelesNY rounded=Engine(5,1,.5); Bar(rounded,D("2026-11-16 09:31"),100.25);
        Check(N(rounded,"RTH_OPEN",rounded.ObservedAtNY).Price==100.5,"instrument tick rounding midpoint");
        Check(!rounded.ProcessMinute(rounded.ObservedAtNY,1,2,0,1,DateTime.MinValue),"duplicate ignored");
        Throws(()=>Bar(rounded,D("2026-11-16 09:30")),"out-of-order rejected");
        Throws(()=>Bar(rounded,D("2026-11-16 09:32:01")),"unaligned rejected");
        Throws(()=>rounded.ProcessMinute(D("2026-11-16 09:32"),100,90,80,100,DateTime.MinValue),"invalid OHLC rejected");
        Throws(()=>Bar(rounded,D("2026-11-16 09:32"),double.NaN),"NaN rejected");
        Throws(()=>Bar(rounded,DateTime.SpecifyKind(D("2026-11-16 09:32"),DateTimeKind.Utc)),"UTC rejected to avoid double conversion");
        Throws(()=>Engine(0),"invalid retention"); Throws(()=>Engine(5,1,0),"invalid tick");
        MotorNivelesNY unknown=Engine(); Bar(unknown,D("2027-01-04 09:31"));
        Check(unknown.CoverageStatus=="CALENDAR_OUT_OF_COVERAGE" && unknown.GetLevelsAt(unknown.ObservedAtNY).Count==0,"unknown calendar fails closed");
    }
}

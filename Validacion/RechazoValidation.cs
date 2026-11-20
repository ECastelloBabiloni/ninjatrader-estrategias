using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.NivelesNYv1;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Strategies.RechazoNY;
class RechazoValidation
{
    static int checks;
    static readonly DateTime Day=new DateTime(2026,11,16);
    static void C(bool ok,string name) {checks++;if(!ok)throw new Exception(name);}
    static NivelNY Level(string id,double p,DateTime origin,NivelEstado status=NivelEstado.FIXED)
    {return new NivelNY(id,FamiliaNivel.PREV_RTH,"PREV_RTH_HIGH","HIGH","MNQ","synthetic",p,double.NaN,origin,origin.AddHours(1),origin.Date,origin.AddHours(1),status,status==NivelEstado.FIXED,true,"test",60,60,1);}
    static List<SenalNY> Bar(DetectorRechazoNY d,int i,double prev,double close,double k,int window=10,IEnumerable<NivelNY> levels=null)
    {return d.Bar(i,Day.AddHours(10).AddMinutes(i),prev,close,k,levels??new[]{Level("L",100,Day.AddDays(-1))},window,80,20,new HashSet<string>());}
    static void Feed(MasFranjasNY m,DateTime start,DateTime end,double range,DateTime skip=default(DateTime))
    {for(DateTime at=start.AddMinutes(1);at<=end;at=at.AddMinutes(1))if(at!=skip)m.Minute(at,100+range,100);}
    static void Main()
    {
        try {Signals();Grouping();Bands();Risk();Clock();Console.WriteLine("PASS: "+checks+" comprobaciones RechazoNY. Motor puro; no ejecución de órdenes NinjaTrader.");}
        catch(Exception e) {Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
    static void Signals()
    {
        var d=new DetectorRechazoNY();var log=new List<string>();d.Event=(kind,s)=>log.Add(kind);
        C(Bar(d,0,99,101,80).Count==0,"break doesn't confirm itself");
        C(Bar(d,1,101,100,50).Count==0,"equal is not rejection");
        C(Bar(d,2,100,99,50).Single().Direction==-1,"short rejection uses break K only");
        C(log.SequenceEqual(new[]{"BREAK","REJECTION"}),"single break and rejection");
        d=new DetectorRechazoNY();Bar(d,0,101,99,20);
        C(Bar(d,1,99,101,50).Single().Direction==1,"long symmetry");
        d=new DetectorRechazoNY();Bar(d,0,101,102,90);
        C(Bar(d,1,102,99,50).Count==0,"repeated outside close is not crossing");
        d=new DetectorRechazoNY();Bar(d,0,99,101,79.99);
        C(Bar(d,1,101,99,50).Count==0,"K strict threshold filter");
        d=new DetectorRechazoNY();Bar(d,0,99,101,80);
        for(int i=1;i<10;i++) C(Bar(d,i,101,102,85).Count==0,"outside does not restart");
        C(Bar(d,10,102,99,40).Count==1,"tenth subsequent candle eligible");
        d=new DetectorRechazoNY();Bar(d,0,99,101,80);
        for(int i=1;i<=10;i++)Bar(d,i,101,102,85);
        C(Bar(d,11,102,99,40).Count==0,"eleventh expired");
        d=new DetectorRechazoNY();Bar(d,0,99,101,80);d.Reset("block");
        C(Bar(d,1,101,99,40).Count==0,"reset discards stale break");
        d=new DetectorRechazoNY();var forming=new[]{Level("L",100,Day.AddDays(-1),NivelEstado.FORMING)};
        Bar(d,0,99,101,90,10,forming);C(Bar(d,1,101,99,40).Count==0,"forming excluded");
        d=new DetectorRechazoNY();Bar(d,0,99,101,80);Bar(d,1,101,99,40);
        Bar(d,2,99,101,80);C(Bar(d,3,101,99,50).Count==1,"new crossing rearms");
    }
    static void Grouping()
    {
        var a=Level("A",100,Day.AddDays(-3));var b=Level("B",104,Day.AddDays(-2));var c=Level("C",108,Day.AddDays(-1));
        var signals=new List<SenalNY>{new SenalNY{Id="a",Level=a,Direction=-1},new SenalNY{Id="b",Level=b,Direction=-1},new SenalNY{Id="c",Level=c,Direction=-1}};
        var groups=DetectorRechazoNY.Group(signals,new[]{a,b,c},5);
        C(groups.Count==2,"no transitive grouping");C(groups[0].Level.LevelId=="A","oldest crossed level principal");
        C(groups[0].Secondary.Single().LevelId=="B","secondary exact IDs preserved");
        var onlyB=DetectorRechazoNY.Group(new List<SenalNY>{new SenalNY{Level=b,Direction=-1}},new[]{a,b},5);
        C(onlyB[0].Level.LevelId=="B"&&onlyB[0].Secondary[0].LevelId=="A","proximity does not attribute crossing to unbroken oldest");
    }
    static void Bands()
    {
        C(MasFranjasNY.Type(Day.AddHours(18))==FranjaNY.Asia,"18 boundary");
        C(MasFranjasNY.Type(Day.AddHours(3))==FranjaNY.Europa,"03 boundary");
        C(MasFranjasNY.Type(Day.AddHours(9.5))==FranjaNY.America,"09:30 boundary");
        C(MasFranjasNY.Type(Day.AddHours(16))==FranjaNY.Ninguna,"16 gap");
        var m=new MasFranjasNY();double v;
        for(int i=0;i<5;i++)
        {
            DateTime day=Day.AddDays(i);
            Feed(m,day.AddDays(-1).AddHours(18),day.AddHours(3),10+i);
            Feed(m,day.AddHours(3),day.AddHours(9.5),20+i);
            Feed(m,day.AddHours(9.5),day.AddHours(16),30+i);
        }
        C(m.TryGet(FranjaNY.Asia,5,out v)&&v==12,"same-kind Asia SMA");
        C(m.TryGet(FranjaNY.Europa,5,out v)&&v==22,"same-kind Europe SMA");
        C(m.TryGet(FranjaNY.America,5,out v)&&v==32,"America retains16 despite15:45block");
        Feed(m,Day.AddDays(7).AddHours(9.5),Day.AddDays(7).AddHours(10),1000);
        C(m.TryGet(FranjaNY.America,5,out v)&&v==32,"current band cannot leak future extreme");
        var missing=new MasFranjasNY();Feed(missing,Day.AddHours(3),Day.AddHours(9.5),100,Day.AddHours(7));
        C(!missing.TryGet(FranjaNY.Europa,1,out v),"gap is not full band");
        var shortened=new MasFranjasNY();var holiday=new DateTime(2026,9,7);
        Feed(shortened,holiday.AddHours(9.5),holiday.AddHours(13),50);
        C(!shortened.TryGet(FranjaNY.America,1,out v),"official short isn't390min sample");
    }
    static void Risk()
    {
        var plan=ExperimentoAMath.Size(250,40,.25m,2,1,40,2000,1500,1.82m);
        C(plan.Quantity==10&&plan.StopPoints==99&&plan.TargetPoints==76,"reuse original sizing example");
        foreach(int direction in new[]{1,-1})foreach(int q in new[]{1,7,10,25,40})foreach(double avg in new[]{20000.0,20000.125,20000.2})
        foreach(double pnl in new[]{0.0,-50.0,80.0})
        {
            var p=RiesgoRechazoNY.Levels(direction,q,avg,pnl,q,2,.25,1.82,2000,300);
            C(p.ExpectedLoss<=2000.000001,"joint loss bounded including fees/realised");
            C(p.ExpectedGain>=299.999999,"target net rounded conservatively");
            C(Math.Abs(p.Stop/.25-Math.Round(p.Stop/.25))<1e-6,"SL tick aligned");
            C(direction*(avg-p.Stop)>0,"SL correct side");
        }
        C(RiesgoRechazoNY.Addition(25,25,40)==15,"25+15 capacity");
        C(RiesgoRechazoNY.Addition(40,40,40)==0,"zero capacity");
        C(RiesgoRechazoNY.Addition(7,7,40)==7,"double actual first quantity");
        var first=RiesgoRechazoNY.Levels(1,10,20000,0,10,2,.25,1.82,2000,300);
        var add=RiesgoRechazoNY.Levels(1,20,19980,0,20,2,.25,1.82,2000,50);
        C(add.Stop!=first.Stop&&add.ExpectedLoss<=2000&&add.ExpectedGain>=50,"recovery recalculates single budget");
        C(RiesgoRechazoNY.Favorable(1,19990,20000)&&!RiesgoRechazoNY.Favorable(1,20000,20000),"strict favorable long");
        C(RiesgoRechazoNY.Favorable(-1,20010,20000),"strict favorable short");
        bool threw=false;try {RiesgoRechazoNY.Levels(1,40,20000,0,40,2,.25,1.82,30,50);}catch(ArgumentException){threw=true;}
        C(threw,"costs exceed budget => no bracket");
    }
    static void Clock()
    {
        string why;var from=new TimeSpan(15,45,0);var until=new TimeSpan(18,15,0);
        foreach(DateTime day in new[]{new DateTime(2026,3,6),new DateTime(2026,3,9),new DateTime(2026,10,30),new DateTime(2026,11,2)})
        {
            C(!RiesgoRechazoNY.Blocked(day.AddHours(15).AddMinutes(44),from,until,2,out why),"before NY block DST");
            C(RiesgoRechazoNY.Blocked(day.AddHours(15).AddMinutes(45),from,until,2,out why),"NY block15:45 DST");
        }
        C(RiesgoRechazoNY.Blocked(Day.AddHours(18).AddMinutes(14),from,until,2,out why),"18:14 blocked");
        C(!RiesgoRechazoNY.Blocked(Day.AddHours(18).AddMinutes(15),from,until,2,out why),"18:15 resumed");
        C(RiesgoRechazoNY.Blocked(new DateTime(2026,7,3,12,58,0),from,until,2,out why),"early cutoff12:58");
        C(RiesgoRechazoNY.Blocked(new DateTime(2027,1,4,10,0,0),from,until,2,out why),"unknown calendar blocked");
        C(RiesgoRechazoNY.CrossedCutoff(Day.AddHours(15).AddMinutes(44),Day.AddHours(18).AddMinutes(16),from,2),"gap across whole block still forces close");
        C(!RiesgoRechazoNY.CrossedCutoff(Day.AddHours(18).AddMinutes(16),Day.AddHours(18).AddMinutes(17),from,2),"cutoff not repeated");
        C(RiesgoRechazoNY.CrossedCutoff(new DateTime(2026,7,3,12,57,0),new DateTime(2026,7,5,18,15,0),from,2),"holiday gap cutoff retained");
        C(!RiesgoRechazoNY.CrossedCutoff(Day.AddHours(10),Day.AddHours(11),from,2),"ordinary minute no crossed cutoff");
        var pause=new PausaRechazoNY();C(pause.Ready(5),"no startup pause");pause.Exit(Day.AddHours(10).AddSeconds(30));
        pause.Bar(Day.AddHours(10),Day.AddHours(10).AddMinutes(1));C(pause.CompleteBars==0,"exit bar excluded");
        for(int i=1;i<=4;i++)pause.Bar(Day.AddHours(10).AddMinutes(i),Day.AddHours(10).AddMinutes(i+1));
        C(!pause.Ready(5),"four full bars insufficient");
        pause.Bar(Day.AddHours(10).AddMinutes(5),Day.AddHours(10).AddMinutes(6));C(pause.Ready(5),"fifth full bar eligible nextopen");
        pause.Bar(Day.AddHours(10).AddMinutes(5),Day.AddHours(10).AddMinutes(6));C(pause.CompleteBars==5,"duplicate bar no increment");
    }
}

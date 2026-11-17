// NivelesImportantesNY v0.1.0. All calculation timestamps are already NY wall time.
// Complete source: engine + NinjaTrader adapter. No orders, MAS dependency or signals.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace NinjaTrader.NinjaScript.NivelesNYv1
{
    public enum NivelEstado { FORMING, FIXED, INCOMPLETE, UNAVAILABLE }
    public enum FamiliaNivel { PREV_RTH, OVERNIGHT, FIRST_HOUR, CURRENT_RTH, RTH_OPEN, PREV_RTH_CLOSE }

    // Immutable observation. Appearance and indicator instance are NOT part of LevelId.
    public sealed class NivelNY
    {
        public string LevelId { get; private set; }
        public FamiliaNivel Family { get; private set; }
        public string FamilyCode { get; private set; }
        public string Side { get; private set; }
        public string Name { get; private set; }
        public string Instrument { get; private set; }
        public string SeriesIdentity { get; private set; }
        public string CalendarVersion { get { return CalendarioSesionesNY.Version; } }
        public string IndicatorVersion { get { return MotorNivelesNY.Version; } }
        public double Price { get; private set; }
        public double PreviousPrice { get; private set; }
        public DateTime SourceStartNY { get; private set; }
        public DateTime SourceEndNY { get; private set; }
        public DateTime TradingDate { get; private set; }
        public DateTime AvailableFromNY { get; private set; }
        public DateTime ObservedAtNY { get; private set; }
        public NivelEstado Status { get; private set; }
        public bool SourceComplete { get; private set; }
        public bool PrefixComplete { get; private set; }
        public string Reason { get; private set; }
        public int AgeSessions { get; private set; }
        public int Priority { get { return 0; } } // Unassigned; no ranking/grouping in v0.1.
        public int ObservedMinutes { get; private set; }
        public int ExpectedMinutes { get; private set; }
        public int Revision { get; private set; }

        internal NivelNY(string id, FamiliaNivel family, string code, string side, string instrument,
            string series, double price, double previous, DateTime start, DateTime end, DateTime day,
            DateTime at, NivelEstado status, bool complete, bool prefix, string reason, int seen, int expected, int revision)
        {
            LevelId=id; Family=family; FamilyCode=code; Side=side; Instrument=instrument; SeriesIdentity=series;
            Name=code+" "+day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            Price=price; PreviousPrice=previous; SourceStartNY=start; SourceEndNY=end; TradingDate=day;
            AvailableFromNY=ObservedAtNY=at; Status=status; SourceComplete=complete; PrefixComplete=prefix;
            Reason=reason; ObservedMinutes=seen; ExpectedMinutes=expected; Revision=revision;
        }
        internal NivelNY WithAge(int age) { NivelNY copy=(NivelNY)MemberwiseClone(); copy.AgeSessions=age; return copy; }
    }

    public sealed class MotorNivelesNY
    {
        public const string Version="0.1.0";
        private readonly object sync=new object();
        private readonly string instrument, series;
        private readonly double tick;
        private readonly int retainRth, retainOther, rollover;
        private readonly SortedDictionary<DateTime,Day> days=new SortedDictionary<DateTime,Day>();
        private readonly Dictionary<string,List<NivelNY>> versions=new Dictionary<string,List<NivelNY>>();
        private readonly List<Completion> completed=new List<Completion>();
        private DateTime ensured=DateTime.MinValue, lastBar=DateTime.MinValue, unsupportedAt=DateTime.MaxValue;
        public DateTime ObservedAtNY { get; private set; }
        public string CoverageStatus { get; private set; }
        public int RetainedRthPeriods { get { lock(sync) return Math.Min(retainRth,completed.Count); } }
        public int CompleteRthPeriods { get { lock(sync) return completed.Count(x=>x.Complete); } }

        private sealed class Completion { public DateTime Day, At; public bool Complete; }
        private sealed class Window
        {
            public DateTime Start, End, Next, LastClose;
            public bool Enabled, Gap, ShortHour;
            public int Count;
            public double High=double.NaN, Low=double.NaN, Close=double.NaN, Open=double.NaN;
            public string DisabledReason="", GapReason="";
            public int Expected { get { return Math.Max(0,(int)(End-Start).TotalMinutes); } }
            public Window(DateTime start,DateTime end,bool enabled,string reason)
            { Start=Next=start; End=end; Enabled=enabled && end>start; DisabledReason=reason; }
            public void Add(DateTime open,DateTime close,double o,double h,double l,double c)
            {
                if(!Enabled || open<Start || close>End) return;
                if(open!=Next) { Gap=true; if(GapReason.Length==0) GapReason="GAP_EXPECTED_"+Next.ToString("s")+"_GOT_"+open.ToString("s"); }
                if(open==Start) Open=o;
                High=double.IsNaN(High)?h:Math.Max(High,h); Low=double.IsNaN(Low)?l:Math.Min(Low,l);
                Count++; Next=close;
                if(close==End) { LastClose=close; Close=c; }
            }
            public bool Complete { get { return Enabled && !Gap && Count==Expected && Next==End && !ShortHour; } }
            public NivelEstado Status(DateTime at)
            {
                if(!Enabled || Count==0) return NivelEstado.UNAVAILABLE;
                if(at>=End) return Complete?NivelEstado.FIXED:NivelEstado.INCOMPLETE;
                return !Gap && Next>=at?NivelEstado.FORMING:NivelEstado.INCOMPLETE;
            }
            public string Reason(DateTime at)
            {
                if(!Enabled) return DisabledReason;
                if(at<=Start && Count==0) return "PERIOD_NOT_STARTED";
                if(Count==0) return "DATA_ABSENT";
                if(ShortHour && at>=End) return "FIRST_HOUR_SHORTENED_BY_OFFICIAL_OR_TEMPLATE_END";
                if(Gap) return GapReason;
                if(at>=End && !Complete) return "MISSING_TAIL_OR_PARTIAL_LOAD";
                if(at<End && Next<at) return "MISSING_KNOWN_MINUTE";
                return at>=End?"COMPLETE_CONTIGUOUS_MINUTES":"FORMING_COVERED_PREFIX";
            }
        }
        private sealed class Day
        {
            public DateTime Date, FuturesEnd;
            public bool Known, HasRth, PublishedPrevious;
            public Window Night,Rth,Hour,Opening;
        }

        public MotorNivelesNY(string instrument,double tickSize,int retainedRth,int retainedOther,int futuresOpenHour,string seriesIdentity)
        {
            if(string.IsNullOrWhiteSpace(instrument) || double.IsNaN(tickSize) || double.IsInfinity(tickSize) || tickSize<=0 ||
                retainedRth<1 || retainedRth>50 || retainedOther<1 || retainedOther>20 || futuresOpenHour<17 || futuresOpenHour>23)
                throw new ArgumentException("Invalid level engine configuration");
            this.instrument=instrument; tick=tickSize; retainRth=retainedRth; retainOther=retainedOther;
            rollover=futuresOpenHour; series=seriesIdentity??""; CoverageStatus="WARMUP_NO_DATA";
        }
        public DateTime TradingDateFor(DateTime barOpenNY)
        { return barOpenNY.TimeOfDay>=TimeSpan.FromHours(rollover)?barOpenNY.Date.AddDays(1):barOpenNY.Date; }
        private double Round(double price) { return double.IsNaN(price)?price:Math.Round(price/tick,MidpointRounding.AwayFromZero)*tick; }
        private Day NewDay(DateTime date,DateTime templateEnd)
        {
            DateTime end; bool regular; string reason;
            bool known=CalendarioSesionesNY.Resolve(date,out end,out regular,out reason);
            if(end!=DateTime.MinValue && templateEnd.Date==date && templateEnd<end) end=templateEnd;
            DateTime start=date.AddHours(9).AddMinutes(30);
            DateTime rthEnd=end==DateTime.MinValue?start:(end<date.AddHours(16)?end:date.AddHours(16));
            bool rth=known && rthEnd>start;
            if(!rth) rthEnd=start;
            DateTime nightEnd=end!=DateTime.MinValue && end<start?end:start;
            string disabled=known?"NO_RTH:"+reason:"CALENDAR_OUT_OF_COVERAGE";
            Day d=new Day { Date=date, Known=known, FuturesEnd=end, HasRth=rth };
            d.Night=new Window(date.AddDays(-1).AddHours(rollover),nightEnd,known && end!=DateTime.MinValue,disabled);
            d.Rth=new Window(start,rthEnd,rth,disabled);
            d.Hour=new Window(start,rthEnd<start.AddHours(1)?rthEnd:start.AddHours(1),rth,disabled);
            d.Hour.ShortHour=rth && d.Hour.Expected<60;
            d.Opening=new Window(start,start.AddMinutes(1),rth,disabled);
            return d;
        }

        // Called once for each CLOSED primary 1-minute bar. No computer clock is used.
        public bool ProcessMinute(DateTime closeNY,double open,double high,double low,double close,DateTime templateEndNY)
        {
            lock(sync)
            {
                if(closeNY.Kind==DateTimeKind.Utc || closeNY.Ticks%TimeSpan.TicksPerMinute!=0)
                    throw new ArgumentException("Expected NY wall timestamp at minute boundary");
                if(closeNY==lastBar) return false;
                if(closeNY<lastBar) throw new ArgumentException("Out-of-order bars require a new engine/reload");
                if(new[]{open,high,low,close}.Any(x=>double.IsNaN(x)||double.IsInfinity(x)) ||
                    high<low || high<open || high<close || low>open || low>close)
                    throw new ArgumentException("Invalid OHLC");
                DateTime barOpen=closeNY.AddMinutes(-1), tradingDate=TradingDateFor(barOpen);
                if(ensured==DateTime.MinValue) ensured=tradingDate.AddDays(-1);
                while(ensured<tradingDate)
                {
                    ensured=ensured.AddDays(1);
                    days.Add(ensured,NewDay(ensured,ensured==tradingDate?templateEndNY:DateTime.MinValue));
                }
                Day current=days[tradingDate];
                if(!current.Known) unsupportedAt=unsupportedAt<closeNY?unsupportedAt:closeNY;
                current.Night.Add(barOpen,closeNY,open,high,low,close);
                current.Rth.Add(barOpen,closeNY,open,high,low,close);
                current.Hour.Add(barOpen,closeNY,open,high,low,close);
                current.Opening.Add(barOpen,closeNY,open,high,low,close);
                foreach(Day d in days.Values)
                {
                    // Frozen windows are not rewritten by later observations.
                    if(d==current || !d.PublishedPrevious)
                    {
                        PublishRange(d,d.Night,FamiliaNivel.OVERNIGHT,"OVERNIGHT",closeNY);
                        PublishRange(d,d.Rth,FamiliaNivel.CURRENT_RTH,"CURRENT_RTH",closeNY);
                        PublishRange(d,d.Hour,FamiliaNivel.FIRST_HOUR,"FIRST_HOUR",closeNY);
                        PublishOpen(d,closeNY);
                    }
                    if(d.HasRth && !d.PublishedPrevious && closeNY>=d.Rth.End)
                    {
                        PublishRange(d,d.Rth,FamiliaNivel.PREV_RTH,"PREV_RTH",closeNY);
                        bool hasClose=d.Rth.LastClose==d.Rth.End;
                        Publish(d,d.Rth,FamiliaNivel.PREV_RTH_CLOSE,"PREV_RTH_CLOSE","CLOSE",
                            hasClose?d.Rth.Close:double.NaN,closeNY,
                            hasClose?d.Rth.Status(closeNY):NivelEstado.UNAVAILABLE,
                            hasClose?d.Rth.Reason(closeNY):"CLOSING_MINUTE_ABSENT");
                        completed.Add(new Completion { Day=d.Date, At=closeNY, Complete=d.Rth.Complete });
                        d.PublishedPrevious=true;
                    }
                }
                lastBar=ObservedAtNY=closeNY;
                Prune();
                CoverageStatus=!current.Known?"CALENDAR_OUT_OF_COVERAGE":
                    "RTH_HISTORY_"+Math.Min(retainRth,completed.Count)+"/"+retainRth+
                    "; COMPLETE="+completed.TakeLastCompatible(retainRth).Count(x=>x.Complete)+
                    (completed.Count<retainRth?"; WARMUP_INSUFFICIENT":"");
                return true;
            }
        }

        private void PublishRange(Day d,Window w,FamiliaNivel family,string prefix,DateTime at)
        {
            Publish(d,w,family,prefix+"_HIGH","HIGH",w.High,at,w.Status(at),w.Reason(at));
            Publish(d,w,family,prefix+"_LOW","LOW",w.Low,at,w.Status(at),w.Reason(at));
        }
        private void PublishOpen(Day d,DateTime at)
        {
            bool found=!double.IsNaN(d.Opening.Open);
            Publish(d,d.Opening,FamiliaNivel.RTH_OPEN,"RTH_OPEN","OPEN",d.Opening.Open,at,
                found?NivelEstado.FIXED:NivelEstado.UNAVAILABLE,
                found?"OPENING_MINUTE_OBSERVED":(!d.Opening.Enabled?d.Opening.DisabledReason:
                    (at<=d.Opening.Start?"PERIOD_NOT_STARTED":"OPENING_MINUTE_ABSENT")));
        }
        private void Publish(Day d,Window w,FamiliaNivel family,string code,string side,double price,DateTime at,NivelEstado status,string reason)
        {
            price=Round(price);
            string id=instrument+"|"+code+"|"+w.Start.ToString("yyyyMMddTHHmm",CultureInfo.InvariantCulture)+
                "|"+w.End.ToString("yyyyMMddTHHmm",CultureInfo.InvariantCulture)+"|"+side;
            List<NivelNY> list;
            if(!versions.TryGetValue(id,out list)) { list=new List<NivelNY>(); versions.Add(id,list); }
            NivelNY previous=list.Count==0?null:list[list.Count-1];
            bool complete=family==FamiliaNivel.RTH_OPEN?!double.IsNaN(price):w.Complete;
            bool prefix=w.Enabled && !w.Gap && w.Count>0 && (w.Next>=at || complete);
            if(previous!=null && previous.Price.Equals(price) && previous.Status==status && previous.Reason==reason &&
                previous.ObservedMinutes==w.Count && previous.SourceComplete==complete && previous.PrefixComplete==prefix) return;
            NivelNY value=new NivelNY(id,family,code,side,instrument,series,price,previous==null?double.NaN:previous.Price,
                w.Start,w.End,d.Date,at,status,complete,prefix,reason,w.Count,w.Expected,list.Count+1);
            // One resulting observation per timestamp; no fabricated sub-minute revisions.
            if(previous!=null && previous.ObservedAtNY==at) list[list.Count-1]=value;
            else list.Add(value);
        }
        private void Prune()
        {
            HashSet<DateTime> rth=new HashSet<DateTime>(completed.TakeLastCompatible(retainRth).Select(x=>x.Day));
            HashSet<DateTime> close=new HashSet<DateTime>(completed.TakeLastCompatible(retainOther).Select(x=>x.Day));
            HashSet<DateTime> other=new HashSet<DateTime>(days.Values.Where(x=>x.Known && x.FuturesEnd!=DateTime.MinValue)
                .Select(x=>x.Date).TakeLastCompatible(retainOther));
            foreach(string key in versions.Keys.ToList())
            {
                NivelNY n=versions[key][versions[key].Count-1];
                bool keep=n.Family==FamiliaNivel.PREV_RTH?rth.Contains(n.TradingDate):
                    n.Family==FamiliaNivel.PREV_RTH_CLOSE?close.Contains(n.TradingDate):other.Contains(n.TradingDate);
                if(!keep) versions.Remove(key);
            }
            int retain=Math.Max(retainRth,retainOther)+2;
            if(completed.Count>retain) completed.RemoveRange(0,completed.Count-retain);
            DateTime earliest=days.Keys.Last().AddDays(-Math.Max(20,retain*4));
            foreach(DateTime key in days.Keys.Where(x=>x<earliest).ToList()) days.Remove(key);
        }
        public ReadOnlyCollection<NivelNY> GetLevelsAt(DateTime asOfNY)
        {
            lock(sync)
            {
                List<NivelNY> result=new List<NivelNY>();
                if(asOfNY>=unsupportedAt) return result.AsReadOnly();
                foreach(List<NivelNY> list in versions.Values)
                {
                    NivelNY n=list.LastOrDefault(x=>x.AvailableFromNY<=asOfNY);
                    if(n==null) continue;
                    int age=completed.Count(x=>x.Day>n.TradingDate && x.At<=asOfNY);
                    if(n.Family==FamiliaNivel.PREV_RTH || n.Family==FamiliaNivel.PREV_RTH_CLOSE) age++;
                    result.Add(n.WithAge(age));
                }
                return result.OrderBy(x=>x.TradingDate).ThenBy(x=>x.FamilyCode,StringComparer.Ordinal).ToList().AsReadOnly();
            }
        }
        public NivelNY GetLevelAt(string levelId,DateTime asOfNY)
        { return GetLevelsAt(asOfNY).FirstOrDefault(x=>x.LevelId==levelId); }
    }
    internal static class CompatibleEnumerable
    {
        internal static IEnumerable<T> TakeLastCompatible<T>(this IEnumerable<T> source,int count)
        { List<T> list=source.ToList(); return list.Skip(Math.Max(0,list.Count-count)); }
    }
}

#if !NIVELES_CORE_ONLY
namespace NinjaTrader.NinjaScript.Indicators
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Windows;
    using System.Windows.Media;
    using System.Xml.Serialization;
    using NinjaTrader.Data;
    using NinjaTrader.Gui;
    using NinjaTrader.Gui.Tools;
    using NinjaTrader.NinjaScript.DrawingTools;
    using NinjaTrader.NinjaScript.NivelesNYv1;

    public class NivelesImportantesNY : Indicator
    {
        private MotorNivelesNY engine;
        private SessionIterator iterator;
        private string error="", prefix;
        private readonly Dictionary<string,NivelNY> painted=new Dictionary<string,NivelNY>();
        private readonly Dictionary<string,List<string>> tags=new Dictionary<string,List<string>>();
        private readonly Queue<string> segments=new Queue<string>();
        private DateTime actualEnd=DateTime.MinValue;

        protected override void OnStateChange()
        {
            if(State==State.SetDefaults)
            {
                Name="NivelesImportantesNY";
                Description="Seis familias de niveles NY, observaciones temporales y cobertura explícita. No opera.";
                Calculate=Calculate.OnBarClose; IsOverlay=true; DrawOnPricePanel=true; DisplayInDataBox=false;
                IsSuspendedWhileInactive=false;
                SesionesRthAnteriores=5; RetencionOtrosPeriodos=1; HoraAperturaFuturosNY=18;
                MostrarPrevRth=MostrarNocturnos=MostrarPrimeraHora=MostrarRthActual=MostrarApertura=MostrarCierreAnterior=true;
                MostrarPanel=MostrarEtiquetas=MostrarHistorialDinamico=true;
                MostrarIncompletos=MostrarUltimoRthFueraSesion=false;
                Grosor=2; TamanoEtiqueta=10; SeparacionEtiquetasTicks=8; MaxSegmentosDibujo=12000;
                ColorPrevRth=Brushes.DodgerBlue; ColorNocturnos=Brushes.MediumPurple; ColorPrimeraHora=Brushes.DarkOrange;
                ColorRthActual=Brushes.Teal; ColorApertura=Brushes.ForestGreen; ColorCierre=Brushes.Gray;
            }
            else if(State==State.DataLoaded)
            {
                prefix=Name+"_"+Guid.NewGuid().ToString("N")+"_";
                if(BarsPeriod.BarsPeriodType!=BarsPeriodType.Minute || BarsPeriod.Value!=1 || Calculate!=Calculate.OnBarClose)
                    error="REQUIERE 1 minuto y OnBarClose; no requiere ticks";
                if(BarsPeriod.MarketDataType!=MarketDataType.Last) error="REQUIERE datos Last (Último)";
                string zone=Core.Globals.GeneralOptions.TimeZoneInfo.Id;
                if(zone!="Eastern Standard Time" && zone!="America/New_York") error="REQUIERE zona de aplicación Nueva York; actual="+zone;
                if(Instrument.MasterInstrument.Name!="MNQ" && Instrument.MasterInstrument.Name!="NQ") error="Primera versión: MNQ o NQ";
                if(error.Length==0)
                {
                    engine=new MotorNivelesNY(Instrument.FullName,TickSize,SesionesRthAnteriores,RetencionOtrosPeriodos,HoraAperturaFuturosNY,
                        "Last/1minute/OnBarClose; template="+Bars.TradingHours.Name+"; masterMerge="+Instrument.MasterInstrument.MergePolicy+"; zone="+zone);
                    iterator=new SessionIterator(Bars);
                }
                if(error.Length>0) Print(Name+" | "+error);
            }
        }
        protected override void OnBarUpdate()
        {
            if(BarsInProgress!=0 || CurrentBar<0) return;
            if(error.Length==0 && engine!=null)
            {
                try
                {
                    if(actualEnd==DateTime.MinValue || Time[0]>actualEnd)
                    { iterator.GetNextSession(Time[0],true); actualEnd=iterator.ActualSessionEnd; }
                    if(engine.ProcessMinute(Time[0],Open[0],High[0],Low[0],Close[0],actualEnd)) PaintLevels();
                }
                catch(Exception ex) { error="HALTED: "+ex.Message; Print(Name+" | "+error); ClearOwnedDrawings(); }
            }
            if(MostrarPanel)
            {
                string text=error.Length>0?error:"Niveles NY v"+MotorNivelesNY.Version+" | "+engine.CoverageStatus+
                    "\n1 minuto cerrado · ETH necesario para nocturno completo\nFIXED / FORMING; incompletos "+(MostrarIncompletos?"visibles":"ocultos");
                Draw.TextFixed(this,prefix+"status",text,TextPosition.TopLeft,Brushes.WhiteSmoke,new SimpleFont("Arial",11),Brushes.DimGray,Brushes.Black,65);
            }
        }
        private bool Visible(NivelNY n)
        {
            if(double.IsNaN(n.Price) || n.Status==NivelEstado.UNAVAILABLE || (!MostrarIncompletos && n.Status==NivelEstado.INCOMPLETE)) return false;
            if(n.Family==FamiliaNivel.CURRENT_RTH && !MostrarUltimoRthFueraSesion && Time[0]>=n.SourceEndNY) return false;
            switch(n.Family)
            {
                case FamiliaNivel.PREV_RTH:return MostrarPrevRth;
                case FamiliaNivel.OVERNIGHT:return MostrarNocturnos;
                case FamiliaNivel.FIRST_HOUR:return MostrarPrimeraHora;
                case FamiliaNivel.CURRENT_RTH:return MostrarRthActual;
                case FamiliaNivel.RTH_OPEN:return MostrarApertura;
                default:return MostrarCierreAnterior;
            }
        }
        private Brush Color(NivelNY n)
        {
            Brush b=n.Family==FamiliaNivel.PREV_RTH?ColorPrevRth:n.Family==FamiliaNivel.OVERNIGHT?ColorNocturnos:
                n.Family==FamiliaNivel.FIRST_HOUR?ColorPrimeraHora:n.Family==FamiliaNivel.CURRENT_RTH?ColorRthActual:
                n.Family==FamiliaNivel.RTH_OPEN?ColorApertura:ColorCierre;
            Brush result=b.Clone(); result.Opacity=Math.Max(.25,1-.12*Math.Max(0,n.AgeSessions-1)); result.Freeze(); return result;
        }
        private void Track(string id,string tag)
        { List<string> own; if(!tags.TryGetValue(id,out own)) { own=new List<string>(); tags.Add(id,own); } if(!own.Contains(tag)) own.Add(tag); }
        private void Segment(NivelNY n,string tag,DateTime start,DateTime end)
        {
            if(end<=start) return;
            DashStyleHelper style=n.Status==NivelEstado.FORMING?DashStyleHelper.Dot:
                n.Status==NivelEstado.INCOMPLETE?DashStyleHelper.DashDot:n.Side=="LOW"?DashStyleHelper.Dash:DashStyleHelper.Solid;
            Draw.Line(this,tag,false,start,n.Price,end,n.Price,Color(n),style,Grosor);
            Track(n.LevelId,tag);
        }
        private void RemoveLevel(string id)
        { List<string> own; if(tags.TryGetValue(id,out own)) foreach(string tag in own) RemoveDrawObject(tag); tags.Remove(id); painted.Remove(id); }
        private void ClearOwnedDrawings() { foreach(string id in tags.Keys.ToList()) RemoveLevel(id); }
        private void PaintLevels()
        {
            List<NivelNY> visible=engine.GetLevelsAt(Time[0]).Where(Visible).OrderBy(x=>x.Price).ThenBy(x=>x.LevelId,StringComparer.Ordinal).ToList();
            HashSet<string> alive=new HashSet<string>(visible.Select(x=>x.LevelId));
            foreach(string id in tags.Keys.Where(x=>!alive.Contains(x)).ToList()) RemoveLevel(id);
            double prior=double.NaN; int row=0;
            foreach(NivelNY n in visible)
            {
                NivelNY old;
                if(painted.TryGetValue(n.LevelId,out old) && old.Revision!=n.Revision && MostrarHistorialDinamico)
                {
                    string tag=prefix+n.LevelId+"|v"+old.Revision;
                    Segment(old,tag,old.AvailableFromNY,n.AvailableFromNY); segments.Enqueue(tag);
                }
                Segment(n,prefix+n.LevelId+"|live",n.AvailableFromNY,Time[0]);
                if(MostrarEtiquetas)
                {
                    row=!double.IsNaN(prior) && n.Price-prior<=SeparacionEtiquetasTicks*TickSize?row+1:0;
                    string tag=prefix+n.LevelId+"|label";
                    Draw.Text(this,tag,false,n.Name+" "+n.Price.ToString("0.#####",CultureInfo.InvariantCulture)+" ["+n.Status+"]",
                        Time[0],n.Price,row*(TamanoEtiqueta+4),Color(n),new SimpleFont("Arial",TamanoEtiqueta),TextAlignment.Left,
                        Brushes.Transparent,Brushes.Transparent,0);
                    Track(n.LevelId,tag); prior=n.Price;
                }
                painted[n.LevelId]=n;
            }
            while(segments.Count>MaxSegmentosDibujo) RemoveDrawObject(segments.Dequeue());
        }
        [Browsable(false),XmlIgnore] public string VersionIndicador { get { return MotorNivelesNY.Version; } }
        [Browsable(false),XmlIgnore] public bool EsOperativo { get { return error.Length==0 && engine!=null && engine.CoverageStatus!="CALENDAR_OUT_OF_COVERAGE"; } }
        [Browsable(false),XmlIgnore] public string EstadoCobertura { get { return error.Length>0?error:engine==null?"NO_DATA":engine.CoverageStatus; } }
        [Browsable(false),XmlIgnore] public ReadOnlyCollection<NivelNY> Niveles { get { Update(); return EsOperativo?engine.GetLevelsAt(engine.ObservedAtNY):new List<NivelNY>().AsReadOnly(); } }
        public ReadOnlyCollection<NivelNY> ObtenerNiveles(DateTime asOfNY) { Update(); return engine==null || error.Length>0?new List<NivelNY>().AsReadOnly():engine.GetLevelsAt(asOfNY); }
        public NivelNY ObtenerNivel(string levelId,DateTime asOfNY) { return ObtenerNiveles(asOfNY).FirstOrDefault(x=>x.LevelId==levelId); }

        [NinjaScriptProperty,Range(1,50),Display(Name="Sesiones RTH anteriores",GroupName="Cálculo",Order=1)] public int SesionesRthAnteriores { get; set; }
        [NinjaScriptProperty,Range(1,20),Display(Name="Retención otras familias (periodos)",GroupName="Cálculo",Order=2)] public int RetencionOtrosPeriodos { get; set; }
        [NinjaScriptProperty,Range(17,23),Display(Name="Apertura futuros NY (hora)",GroupName="Cálculo",Order=3)] public int HoraAperturaFuturosNY { get; set; }
        [Display(Name="RTH anteriores",GroupName="Familias")] public bool MostrarPrevRth { get; set; }
        [Display(Name="Nocturnos",GroupName="Familias")] public bool MostrarNocturnos { get; set; }
        [Display(Name="Primera hora",GroupName="Familias")] public bool MostrarPrimeraHora { get; set; }
        [Display(Name="RTH en curso",GroupName="Familias")] public bool MostrarRthActual { get; set; }
        [Display(Name="Apertura RTH",GroupName="Familias")] public bool MostrarApertura { get; set; }
        [Display(Name="Cierre RTH anterior",GroupName="Familias")] public bool MostrarCierreAnterior { get; set; }
        [Display(Name="Panel de cobertura",GroupName="Visual")] public bool MostrarPanel { get; set; }
        [Display(Name="Etiquetas",GroupName="Visual")] public bool MostrarEtiquetas { get; set; }
        [Display(Name="Historial dinámico escalonado",GroupName="Visual")] public bool MostrarHistorialDinamico { get; set; }
        [Display(Name="Mostrar incompletos",GroupName="Visual")] public bool MostrarIncompletos { get; set; }
        [Display(Name="Último RTH fuera de sesión",GroupName="Visual")] public bool MostrarUltimoRthFueraSesion { get; set; }
        [Range(1,6),Display(Name="Grosor",GroupName="Visual")] public int Grosor { get; set; }
        [Range(8,24),Display(Name="Tamaño etiqueta",GroupName="Visual")] public int TamanoEtiqueta { get; set; }
        [Range(0,200),Display(Name="Separación etiquetas (ticks)",GroupName="Visual")] public int SeparacionEtiquetasTicks { get; set; }
        [Range(100,50000),Display(Name="Máximo segmentos históricos",GroupName="Visual")] public int MaxSegmentosDibujo { get; set; }
        [XmlIgnore,Display(Name="RTH anteriores",GroupName="Colores")] public Brush ColorPrevRth { get; set; }
        [Browsable(false)] public string ColorPrevRthSerial { get { return Serialize.BrushToString(ColorPrevRth); } set { ColorPrevRth=Serialize.StringToBrush(value); } }
        [XmlIgnore,Display(Name="Nocturnos",GroupName="Colores")] public Brush ColorNocturnos { get; set; }
        [Browsable(false)] public string ColorNocturnosSerial { get { return Serialize.BrushToString(ColorNocturnos); } set { ColorNocturnos=Serialize.StringToBrush(value); } }
        [XmlIgnore,Display(Name="Primera hora",GroupName="Colores")] public Brush ColorPrimeraHora { get; set; }
        [Browsable(false)] public string ColorPrimeraHoraSerial { get { return Serialize.BrushToString(ColorPrimeraHora); } set { ColorPrimeraHora=Serialize.StringToBrush(value); } }
        [XmlIgnore,Display(Name="RTH actual",GroupName="Colores")] public Brush ColorRthActual { get; set; }
        [Browsable(false)] public string ColorRthActualSerial { get { return Serialize.BrushToString(ColorRthActual); } set { ColorRthActual=Serialize.StringToBrush(value); } }
        [XmlIgnore,Display(Name="Apertura",GroupName="Colores")] public Brush ColorApertura { get; set; }
        [Browsable(false)] public string ColorAperturaSerial { get { return Serialize.BrushToString(ColorApertura); } set { ColorApertura=Serialize.StringToBrush(value); } }
        [XmlIgnore,Display(Name="Cierre anterior",GroupName="Colores")] public Brush ColorCierre { get; set; }
        [Browsable(false)] public string ColorCierreSerial { get { return Serialize.BrushToString(ColorCierre); } set { ColorCierre=Serialize.StringToBrush(value); } }
    }
}
#endif

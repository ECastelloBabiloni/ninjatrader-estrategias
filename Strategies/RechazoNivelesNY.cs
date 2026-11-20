using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.NivelesNYv1;
using NinjaTrader.NinjaScript.Strategies.RechazoNY;

namespace NinjaTrader.NinjaScript.Strategies
{
    // Development only. Managed bracket handling must be validated in Playback before use.
    public class RechazoNivelesNY : Strategy
    {
        public const string Release="0.2.0-dev";
        private MotorNivelesNY levels;
        private MasFranjasNY mas;
        private DetectorRechazoNY detector;
        private PausaRechazoNY pause;
        private Stochastics stochastic;
        private SessionIterator iterator;
        private DateTime sessionEnd,minuteObserved,eventTime;
        private readonly HashSet<string> executions=new HashSet<string>();
        private readonly Dictionary<string,Order> entries=new Dictionary<string,Order>();
        private readonly Dictionary<string,int> reported=new Dictionary<string,int>();
        private readonly HashSet<string> originIds=new HashSet<string>();
        private StreamWriter events;
        private string run,positionId="",lastBlock="",signalId="",originBand="";
        private SenalNY queued;
        private bool queuedAddition,closing,fatal,closePending,additionUsed,recoveryFilled,watching,entryReserved;
        private sealed class Lot { public int Open,Bought; public double Cost,Realised; }
        private readonly Dictionary<string,Lot> lots=new Dictionary<string,Lot>();
        private int direction,inventory,totalBought,totalSold,firstFilled,expectedFills;
        private double cost,realised,originPrice,originMas,budget,mark,currentK,currentD,stop,target;
        private int lastMinuteIndex=-1;
        private DateTime watchAt;
        private Order activeStop;

        protected override void OnStateChange()
        {
            if(State==State.SetDefaults)
            {
                Name="RechazoNivelesNY"; Description="Rechazo de niveles + estocástico + MAS por franjas. Desarrollo/simulación.";
                Calculate=Calculate.OnBarClose; EntriesPerDirection=2; EntryHandling=EntryHandling.UniqueEntries;
                StopTargetHandling=StopTargetHandling.ByStrategyPosition;
                RealtimeErrorHandling=RealtimeErrorHandling.StopCancelClose; StartBehavior=StartBehavior.WaitUntilFlat;
                IsExitOnSessionCloseStrategy=false; TimeInForce=TimeInForce.Gtc; Slippage=0;
                BarsRequiredToTrade=1; IsInstantiatedOnEachOptimizationIteration=true;
                MinutosSenal=1; SesionesNiveles=5; RetencionOtros=1; PeriodoK=14; PeriodoD=7; Suavizado=3;
                Sobrecompra=80; Sobreventa=20; VentanaRechazo=10; EsperaBarras=5; ProximidadPuntos=5;
                FranjasAsia=FranjasEuropa=FranjasAmerica=5;
                PorcentajeAsia=PorcentajeEuropa=PorcentajeAmerica=15;
                MinContratos=1; MaxContratos=40; ObjetivoNeto=300; PresupuestoNeto=2000;
                MargenNetoDisponible=2000; ComisionIdaVuelta=1.82; Recuperacion=false;
                UmbralRecuperacion=30; ObjetivoRecuperacion=50; VigilanciaPersistente=true;
                ExigirRupturaTrasUmbral=true; InicioBloqueo=new TimeSpan(15,45,0); FinBloqueo=new TimeSpan(18,15,0);
                MargenCierreAnticipado=2;
            }
            else if(State==State.Configure)
            {Calculate=Calculate.OnBarClose;AddDataSeries(BarsPeriodType.Minute,MinutosSenal);}
            else if(State==State.DataLoaded)
            {
                if(BarsPeriod.BarsPeriodType!=BarsPeriodType.Minute||BarsPeriod.Value!=1||BarsPeriod.MarketDataType!=MarketDataType.Last)
                    throw new InvalidOperationException("RechazoNivelesNY: gráfico base de UN minuto Last requerido; temporalidad de señales en MinutosSenal.");
                if(!ExperimentoAMath.IsNewYork(Core.Globals.GeneralOptions.TimeZoneInfo))
                    throw new InvalidOperationException("Configurar la zona de NinjaTrader en Nueva York con DST.");
                if(Instrument.MasterInstrument.Name!="MNQ"&&Instrument.MasterInstrument.Name!="NQ")
                    throw new InvalidOperationException("Primera versión: MNQ/NQ; adaptar límites para NQ.");
                if(Sobreventa>=Sobrecompra||MinContratos>MaxContratos||InicioBloqueo==FinBloqueo)
                    throw new InvalidOperationException("Revisar umbrales, cantidades y bloqueo.");
                levels=new MotorNivelesNY(Instrument.FullName,TickSize,SesionesNiveles,RetencionOtros,18,
                    "Last 1m;"+BarsArray[0].TradingHours.Name+";"+Instrument.MasterInstrument.MergePolicy);
                mas=new MasFranjasNY();detector=new DetectorRechazoNY();pause=new PausaRechazoNY();
                iterator=new SessionIterator(BarsArray[0]);stochastic=RechazoStochNY(Closes[1],PeriodoD,PeriodoK,Suavizado);
                run=Guid.NewGuid().ToString("N");
                string folder=Path.Combine(Core.Globals.UserDataDir,"RechazoNivelesNY");Directory.CreateDirectory(folder);
                events=new StreamWriter(Path.Combine(folder,run+"_events.csv"),false,new System.Text.UTF8Encoding(true));
                events.WriteLine("run,version,time_ny,event,reason,position,signal,instrument,signal_minutes,quantity,bought,sold,average,mark,realised_gross,net_liquidation,fee_reserved,stop,target,origin_band,origin_mas,budget,k,d,detail");
                detector.Event=(kind,s)=>LogSignal(kind,s.Id,s);
                mas.Event=(kind,detail)=>Log(kind,"",detail);
                eventTime=DateTime.MinValue;
                Log("CONFIG","",string.Join(";",new[]{"levels="+SesionesNiveles,"others="+RetencionOtros,"K="+PeriodoK,"D="+PeriodoD,"smooth="+Suavizado,
                    "over="+Sobrecompra,"under="+Sobreventa,"window="+VentanaRechazo,"pause="+EsperaBarras,"proximity="+ProximidadPuntos,
                    "Asia="+FranjasAsia+"/"+PorcentajeAsia,"Europe="+FranjasEuropa+"/"+PorcentajeEuropa,"America="+FranjasAmerica+"/"+PorcentajeAmerica,
                    "min="+MinContratos,"max="+MaxContratos,"target="+ObjetivoNeto,"budget="+PresupuestoNeto,"available_manual="+MargenNetoDisponible,
                    "fee_roundtrip="+ComisionIdaVuelta,"recovery="+Recuperacion,"trigger="+UmbralRecuperacion,"recovery_target="+ObjetivoRecuperacion,
                    "latch="+VigilanciaPersistente,"newbreak="+ExigirRupturaTrasUmbral,"block="+InicioBloqueo+"/"+FinBloqueo,"early_margin="+MargenCierreAnticipado,
                    "template="+BarsArray[0].TradingHours.Name,"master_merge="+Instrument.MasterInstrument.MergePolicy,"calendar="+CalendarioSesionesNY.Version,
                    "tick="+TickSize,"point="+Instrument.MasterInstrument.PointValue,"mvid="+GetType().Assembly.ManifestModule.ModuleVersionId}));
                Print(Name+" | "+folder+" | "+Release+" | ejecución en histórico/Sim101/Playback101 únicamente");
            }
            else if(State==State.Realtime)
            {
                if(Account==null||(Account.Name!="Sim101"&&Account.Name!="Playback101"))
                    throw new InvalidOperationException("RechazoNivelesNY: solo Sim101 o Playback101. No cuentas reales.");
                foreach(string key in entries.Keys.ToList()) if(!Terminal(entries[key].OrderState)) entries[key]=GetRealtimeOrder(entries[key]);
                if(activeStop!=null&&!Terminal(activeStop.OrderState))activeStop=GetRealtimeOrder(activeStop);
            }
            else if(State==State.Terminated && events!=null)
            {Log("END",inventory==0?"FLAT":"OPEN_POSITION_NOT_FINALISED","");events.Dispose();events=null;}
        }
        protected override void OnBarUpdate()
        {
            if(levels==null||CurrentBars[BarsInProgress]<0) return;
            eventTime=Times[BarsInProgress][0];
            if(BarsInProgress==0)
            {
                if(lastMinuteIndex==CurrentBars[0]) return; lastMinuteIndex=CurrentBars[0];
                if(sessionEnd==DateTime.MinValue||eventTime>sessionEnd) {iterator.GetNextSession(eventTime,true);sessionEnd=iterator.ActualSessionEnd;}
                levels.ProcessMinute(eventTime,Opens[0][0],Highs[0][0],Lows[0][0],Closes[0][0],sessionEnd);
                mas.Minute(eventTime,Highs[0][0],Lows[0][0]);
                DateTime previousMinute=minuteObserved;minuteObserved=eventTime;mark=Closes[0][0];
                if(inventory>0&&RiesgoRechazoNY.CrossedCutoff(previousMinute,eventTime,InicioBloqueo,MargenCierreAnticipado))
                {detector.Reset("CUTOFF_RESET");CloseAll("CUTOFF_CROSSED_OR_DATA_GAP");return;}
                string why;
                if(RiesgoRechazoNY.Blocked(eventTime,InicioBloqueo,FinBloqueo,MargenCierreAnticipado,out why))
                {
                    if(lastBlock!=why) {detector.Reset("BLOCK_RESET");queued=null;Log("BLOCK",why,"");lastBlock=why;}
                    CloseAll(why); return;
                }
                if(lastBlock.Length>0) {detector.Reset("RESUME_NEW_SIGNALS");lastBlock="";}
                if(fatal||closing) {CloseAll(fatal?"FATAL":"CLOSING");return;}
                if(inventory>0)
                {
                    if(activeStop==null||activeStop.OrderState==OrderState.Cancelled||activeStop.OrderState==OrderState.Rejected)
                    {fatal=true;CloseAll("STOP_MISSING_OR_CANCELLED");return;}
                    double net=Net(); Log("MARK","MINUTE_CLOSE","intrabar_path_unknown");
                    if(net<=-budget) {CloseAll("BUDGET_MARK_REACHED");return;}
                    if(Recuperacion&&!additionUsed)
                    {
                        bool reached=net<=-budget*UmbralRecuperacion/100.0;
                        if(reached&&!watching) {watching=true;watchAt=eventTime;if(ExigirRupturaTrasUmbral)detector.Reset("RECOVERY_ARM_RESET");Log("RECOVERY_ARM","THRESHOLD_REACHED","");}
                        if(!VigilanciaPersistente&&!reached&&watching) {watching=false;detector.Reset("RECOVERY_DISARM");}
                    }
                }
                TrySubmit(); return;
            }
            if(BarsInProgress!=1||CurrentBars[1]<Math.Max(PeriodoK+Suavizado,PeriodoD+Suavizado)||CurrentBars[0]<0) return;
            currentK=stochastic.K[0];currentD=stochastic.D[0];
            DateTime start=eventTime.AddMinutes(-MinutosSenal);
            pause.Bar(start,eventTime);
            string blockedReason;
            if(fatal||closing||RiesgoRechazoNY.Blocked(eventTime,InicioBloqueo,FinBloqueo,MargenCierreAnticipado,out blockedReason))
            {detector.Reset("BAR_BLOCKED");return;}
            var known=levels.GetLevelsAt(start).Where(n=>n.Status==NivelEstado.FIXED&&n.SourceComplete&&n.Family!=FamiliaNivel.CURRENT_RTH).ToList();
            var signals=detector.Bar(CurrentBars[1],eventTime,Closes[1][1],Closes[1][0],currentK,known,VentanaRechazo,Sobrecompra,Sobreventa,
                inventory>0?originIds:new HashSet<string>());
            foreach(var s in DetectorRechazoNY.Group(signals,known,ProximidadPuntos))
            {
                LogSignal("SIGNAL",s.Id,s);
                string reason=Eligibility(s);
                if(reason.Length>0) {LogSignal("NOT_EXECUTED",reason,s);continue;}
                queued=s;queuedAddition=inventory>0;TrySubmit();
            }
        }
        private bool PendingEntry() {return entryReserved||entries.Values.Any(o=>!Terminal(o.OrderState))||totalBought<expectedFills;}
        private string Eligibility(SenalNY s)
        {
            if(queued!=null||PendingEntry()) return "ENTRY_PENDING";
            if(inventory==0) return pause.Ready(EsperaBarras)?"":"COOLDOWN";
            if(!Recuperacion||additionUsed||!watching) return "POSITION_ACTIVE";
            if(s.Direction!=direction) return "RECOVERY_WRONG_DIRECTION";
            if(ExigirRupturaTrasUmbral&&s.BreakTime<=watchAt) return "BREAK_BEFORE_ARM";
            if(originIds.Contains(s.Level.LevelId)||!RiesgoRechazoNY.Favorable(direction,s.Level.Price,originPrice)) return "RECOVERY_LEVEL_NOT_FAVORABLE";
            return "";
        }
        private void TrySubmit()
        {
            if(queued==null||minuteObserved<queued.ConfirmTime) return;
            SenalNY s=queued;bool add=queuedAddition;queued=null;
            string reason;
            if(fatal||closing||RiesgoRechazoNY.Blocked(minuteObserved,InicioBloqueo,FinBloqueo,MargenCierreAnticipado,out reason))
            {Log("NOT_EXECUTED","SUBMIT_BLOCKED",SignalDetail(s));return;}
            // Reject stale requests after a data gap; no signal queue across sessions.
            if(minuteObserved>s.ConfirmTime) {Log("NOT_EXECUTED","MINUTE_DATA_NOT_AVAILABLE_AT_CONFIRMATION",SignalDetail(s));return;}
            int q;
            if(!add)
            {
                if(inventory!=0||PendingEntry()) {Log("NOT_EXECUTED","POSITION_OR_ORDER_ACTIVE",SignalDetail(s));return;}
                FranjaNY band=MasFranjasNY.Type(s.ConfirmTime);
                int n=band==FranjaNY.Asia?FranjasAsia:band==FranjaNY.Europa?FranjasEuropa:FranjasAmerica;
                double percent=band==FranjaNY.Asia?PorcentajeAsia:band==FranjaNY.Europa?PorcentajeEuropa:PorcentajeAmerica;
                double value;
                if(band==FranjaNY.Ninguna||!mas.TryGet(band,n,out value)) {Log("NOT_EXECUTED","MAS_WARMUP_OR_INVALID",band.ToString());return;}
                Log("MAS_LOOKBACK",band.ToString(),"value="+value+";percent="+percent+";dates="+mas.Sources(band,n));
                double b=Math.Min(PresupuestoNeto,MargenNetoDisponible);
                var plan=ExperimentoAMath.Size((decimal)value,(decimal)percent,(decimal)TickSize,(decimal)Instrument.MasterInstrument.PointValue,
                    MinContratos,MaxContratos,(decimal)b,(decimal)ObjetivoNeto,(decimal)ComisionIdaVuelta);
                if(plan==null) {Log("NOT_EXECUTED","NO_FEASIBLE_SIZE",SignalDetail(s));return;}
                entries.Clear();reported.Clear();lots.Clear();expectedFills=0;totalBought=totalSold=firstFilled=0;realised=cost=0;
                activeStop=null;
                direction=s.Direction;budget=b;originMas=value;originBand=band.ToString();originPrice=mark;
                originIds.Clear();originIds.Add(s.Level.LevelId);foreach(var nlevel in s.Secondary) originIds.Add(nlevel.LevelId);
                positionId=Guid.NewGuid().ToString("N");signalId=s.Id;additionUsed=recoveryFilled=watching=closing=closePending=false;
                q=plan.Quantity;
                SetStopLoss(CalculationMode.Ticks,plan.StopTicks);SetProfitTarget(CalculationMode.Ticks,plan.TargetTicks);
            }
            else
            {
                if(inventory<=0||PendingEntry()||additionUsed||!watching||s.Direction!=direction||!RiesgoRechazoNY.Favorable(direction,mark,originPrice))
                {Log("NOT_EXECUTED","RECOVERY_RECHECK_FAILED",SignalDetail(s));return;}
                q=RiesgoRechazoNY.Addition(firstFilled,inventory,MaxContratos);
                if(q==0) {Log("NOT_EXECUTED","NO_REMAINING_CAPACITY",SignalDetail(s));return;}
                var projected=RiesgoRechazoNY.Levels(direction,inventory+q,(cost+q*mark)/(inventory+q),realised,totalBought+q,
                    Instrument.MasterInstrument.PointValue,TickSize,ComisionIdaVuelta,budget,ObjetivoRecuperacion);
                if(direction*(mark-projected.Stop)<=0||direction*(projected.Target-mark)<=0)
                {Log("NOT_EXECUTED","RECOVERY_BRACKET_NOT_FEASIBLE",SignalDetail(s));return;}
                additionUsed=true;signalId=s.Id;
            }
            Log("ENTRY_REQUEST",add?"RECOVERY":"FIRST",SignalDetail(s)+";requested="+q);
            entryReserved=true; // Reserve BEFORE submission/callbacks, so another signal cannot duplicate the request.
            if(direction>0) EnterLong(0,q,add?"RN_E2":"RN_E1"); else EnterShort(0,q,add?"RN_E2":"RN_E1");
        }
        protected override void OnOrderUpdate(Order order,double limitPrice,double stopPrice,int quantity,int filled,double averageFillPrice,
            OrderState orderState,DateTime time,ErrorCode error,string comment)
        {
            eventTime=time;
            if(order.Name=="Stop loss") activeStop=order;
            if(order.Name=="RN_E1"||order.Name=="RN_E2")
            {
                entries[order.Name]=order;reported[order.Name]=filled;expectedFills=reported.Values.Sum();
                if(Terminal(orderState)&&totalBought>=expectedFills) entryReserved=false;
            }
            if(order.Name=="RN_CLOSE"&&Terminal(orderState)) closePending=false;
            Log("ORDER",order.Name+":"+orderState,"id="+order.OrderId+";qty="+quantity+";filled="+filled+";limit="+limitPrice+";stop="+stopPrice+";error="+error+";comment="+comment);
            if(orderState==OrderState.Rejected||error!=ErrorCode.NoError)
            {fatal=true;CloseAll("ORDER_REJECTION_OR_ERROR");}
            if(closing && (order.Name=="RN_E1"||order.Name=="RN_E2") && !Terminal(orderState)) CancelEntries();
        }
        protected override void OnExecutionUpdate(Execution execution,string executionId,double price,int quantity,MarketPosition marketPosition,string orderId,DateTime time)
        {
            if(!executions.Add(executionId)||execution.Order==null) return;
            eventTime=time;mark=price;string name=execution.Order.Name;
            if(name=="RN_E1"||name=="RN_E2")
            {
                bool adverseRecovery=name=="RN_E2"&&!RiesgoRechazoNY.Favorable(direction,price,originPrice);
                cost+=quantity*price;inventory+=quantity;totalBought+=quantity;
                Lot lot;if(!lots.TryGetValue(name,out lot)) {lot=new Lot();lots.Add(name,lot);}
                lot.Open+=quantity;lot.Bought+=quantity;lot.Cost+=quantity*price;
                if(name=="RN_E2") recoveryFilled=true;
                if(entries.Values.All(o=>Terminal(o.OrderState))&&totalBought>=expectedFills) entryReserved=false;
                if(name=="RN_E1") {firstFilled+=quantity;originPrice=cost/inventory;}
                Log("FILL_ENTRY",name,"execution="+executionId+";order="+orderId+";quantity="+quantity+";price="+price);
                if(inventory>MaxContratos){fatal=true;CloseAll("EXPOSURE_EXCEEDS_CONFIGURED_MAXIMUM");return;}
                if(adverseRecovery) {CloseAll("RECOVERY_FILL_NOT_FAVORABLE");return;}
                if(closing||fatal) {CloseAll("LATE_ENTRY_DURING_CLOSE");return;}
                Protect();
            }
            else
            {
                if(quantity>inventory) {fatal=true;Log("ERROR","EXIT_EXCEEDS_LEDGER",executionId);return;}
                double average=cost/inventory;
                realised+=direction*(price-average)*quantity*Instrument.MasterInstrument.PointValue;
                cost-=average*quantity;inventory-=quantity;totalSold+=quantity;
                int remaining=quantity;
                foreach(string lotName in new[]{"RN_E1","RN_E2"})
                {
                    Lot lot;if(!lots.TryGetValue(lotName,out lot)||lot.Open==0) continue;
                    int n=Math.Min(remaining,lot.Open);double a=lot.Cost/lot.Open;
                    lot.Realised+=direction*(price-a)*n*Instrument.MasterInstrument.PointValue;
                    lot.Open-=n;lot.Cost-=n*a;remaining-=n;
                }
                Log("FILL_EXIT",name,"execution="+executionId+";order="+orderId+";quantity="+quantity+";price="+price);
                closing=true;queued=null;CancelEntries();
                if(inventory==0)
                {
                    cost=0;pause.Exit(time);watching=false;detector.Reset("FLAT_RESET");
                    Log("POSITION_RESULT",name,"net="+(realised-totalBought*ComisionIdaVuelta));
                    foreach(var item in lots) Log("ENTRY_RESULT",item.Key,"allocation=FIFO;gross="+I(item.Value.Realised)+
                        ";fee="+I(item.Value.Bought*ComisionIdaVuelta)+";net="+I(item.Value.Realised-item.Value.Bought*ComisionIdaVuelta));
                    if(events!=null)events.Flush();
                    if(!PendingEntry()) {closing=false;closePending=false;}
                }
                else CloseAll("PARTIAL_EXIT_FINISH_JOINT");
            }
        }
        private void Protect()
        {
            try
            {
                var p=RiesgoRechazoNY.Levels(direction,inventory,cost/inventory,realised,totalBought,
                    Instrument.MasterInstrument.PointValue,TickSize,ComisionIdaVuelta,budget,recoveryFilled?ObjetivoRecuperacion:ObjetivoNeto);
                double market=State==State.Historical?mark:direction>0?GetCurrentBid():GetCurrentAsk();
                if(market<=0) market=mark;
                if(direction*(market-p.Stop)<=0||direction*(p.Target-market)<=0) {CloseAll("BRACKET_ALREADY_REACHED_OR_INVALID");return;}
                Log("PROTECTION_RECALC","JOINT","old_sl="+stop+";old_tp="+target+";new_sl="+p.Stop+";new_tp="+p.Target+";loss="+p.ExpectedLoss+";gain="+p.ExpectedGain);
                stop=p.Stop;target=p.Target;
                SetStopLoss(CalculationMode.Price,stop);SetProfitTarget(CalculationMode.Price,target);
            }
            catch(Exception e) {fatal=true;Log("ERROR","RISK_CALCULATION",e.Message);CloseAll("INVALID_RISK");}
        }
        private void CancelEntries()
        {foreach(var o in entries.Values.ToList()) if(o.OrderState==OrderState.Working||o.OrderState==OrderState.Accepted||o.OrderState==OrderState.PartFilled) CancelOrder(o);}
        private void CloseAll(string reason)
        {
            queued=null;closing=true;CancelEntries();
            if(inventory>0&&!closePending)
            {
                closePending=true;Log("CLOSE_REQUEST",reason,"");
                if(direction>0) ExitLong(0,inventory,"RN_CLOSE","");else ExitShort(0,inventory,"RN_CLOSE","");
            }
            if(inventory==0&&!PendingEntry()) {closing=false;closePending=false;}
        }
        private static bool Terminal(OrderState s) {return s==OrderState.Filled||s==OrderState.Cancelled||s==OrderState.Rejected;}
        private double Net() {return realised+(inventory>0?direction*(mark-cost/inventory)*inventory*Instrument.MasterInstrument.PointValue:0)-totalBought*ComisionIdaVuelta;}
        private static string SignalDetail(SenalNY s)
        {
            return "signal="+s.Id+";direction="+s.Direction+";break="+s.BreakTime.ToString("s")+";confirm="+s.ConfirmTime.ToString("s")+";break_k="+s.BreakK+
                ";level="+s.Level.LevelId+";price="+s.Level.Price+";available="+s.Level.AvailableFromNY.ToString("s")+
                ";secondary="+string.Join("/",s.Secondary.Select(n=>n.LevelId+"@"+n.Price+"@"+n.AvailableFromNY.ToString("s")));
        }
        private static string Csv(object o) {return "\""+Convert.ToString(o,CultureInfo.InvariantCulture).Replace("\"","\"\"")+"\"";}
        private static string I(double value) {return value.ToString("R",CultureInfo.InvariantCulture);}
        private void LogSignal(string kind,string reason,SenalNY s)
        {string saved=signalId;signalId=s.Id;Log(kind,reason,SignalDetail(s));signalId=saved;}
        private void Log(string kind,string reason,string detail)
        {
            if(events==null) return;
            events.WriteLine(string.Join(",",new object[]{run,Release,eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"),kind,reason,positionId,signalId,
                Instrument.FullName,MinutosSenal,inventory,totalBought,totalSold,inventory>0?cost/inventory:0,mark,realised,Net(),totalBought*ComisionIdaVuelta,
                stop,target,originBand,originMas,budget,currentK,currentD,detail}.Select(Csv)));
        }
        [NinjaScriptProperty,Range(1,60),Display(Name="Minutos de las señales",GroupName="Señal")] public int MinutosSenal{get;set;}
        [NinjaScriptProperty,Range(1,50),Display(Name="Sesiones niveles RTH",GroupName="Niveles")] public int SesionesNiveles{get;set;}
        [NinjaScriptProperty,Range(1,20),Display(Name="Retención otras familias",GroupName="Niveles")] public int RetencionOtros{get;set;}
        [NinjaScriptProperty,Range(0,1000),Display(Name="Proximidad puntos",GroupName="Niveles")] public double ProximidadPuntos{get;set;}
        [NinjaScriptProperty,Range(1,100),Display(Name="K",GroupName="Señal")] public int PeriodoK{get;set;}
        [NinjaScriptProperty,Range(1,100),Display(Name="D",GroupName="Señal")] public int PeriodoD{get;set;}
        [NinjaScriptProperty,Range(1,100),Display(Name="Suavizado",GroupName="Señal")] public int Suavizado{get;set;}
        [NinjaScriptProperty,Range(0,100),Display(Name="Sobrecompra",GroupName="Señal")] public double Sobrecompra{get;set;}
        [NinjaScriptProperty,Range(0,100),Display(Name="Sobreventa",GroupName="Señal")] public double Sobreventa{get;set;}
        [NinjaScriptProperty,Range(1,100),Display(Name="Ventana rechazo",GroupName="Señal")] public int VentanaRechazo{get;set;}
        [NinjaScriptProperty,Range(0,100),Display(Name="Espera barras completas",GroupName="Señal")] public int EsperaBarras{get;set;}
        [NinjaScriptProperty,Range(1,50),Display(Name="Franjas Asia",GroupName="MAS")] public int FranjasAsia{get;set;}
        [NinjaScriptProperty,Range(1,50),Display(Name="Franjas Europa",GroupName="MAS")] public int FranjasEuropa{get;set;}
        [NinjaScriptProperty,Range(1,50),Display(Name="Franjas América",GroupName="MAS")] public int FranjasAmerica{get;set;}
        [NinjaScriptProperty,Range(.01,100),Display(Name="Porcentaje Asia",GroupName="MAS")] public double PorcentajeAsia{get;set;}
        [NinjaScriptProperty,Range(.01,100),Display(Name="Porcentaje Europa",GroupName="MAS")] public double PorcentajeEuropa{get;set;}
        [NinjaScriptProperty,Range(.01,100),Display(Name="Porcentaje América",GroupName="MAS")] public double PorcentajeAmerica{get;set;}
        [NinjaScriptProperty,Range(1,1000),Display(Name="Mínimo contratos",GroupName="Riesgo")] public int MinContratos{get;set;}
        [NinjaScriptProperty,Range(1,1000),Display(Name="Máximo total contratos",GroupName="Riesgo")] public int MaxContratos{get;set;}
        [NinjaScriptProperty,Range(1,100000),Display(Name="Objetivo neto USD",GroupName="Riesgo")] public double ObjetivoNeto{get;set;}
        [NinjaScriptProperty,Range(1,100000),Display(Name="Presupuesto neto USD",GroupName="Riesgo")] public double PresupuestoNeto{get;set;}
        [NinjaScriptProperty,Range(1,100000),Display(Name="Margen neto disponible MANUAL",GroupName="Riesgo")] public double MargenNetoDisponible{get;set;}
        [NinjaScriptProperty,Range(0,1000),Display(Name="Comisión ida/vuelta USD",GroupName="Riesgo")] public double ComisionIdaVuelta{get;set;}
        [NinjaScriptProperty,Display(Name="Habilitar recuperación",GroupName="Recuperación")] public bool Recuperacion{get;set;}
        [NinjaScriptProperty,Range(1,99),Display(Name="Umbral pérdida %",GroupName="Recuperación")] public double UmbralRecuperacion{get;set;}
        [NinjaScriptProperty,Range(1,100000),Display(Name="Objetivo conjunto USD",GroupName="Recuperación")] public double ObjetivoRecuperacion{get;set;}
        [NinjaScriptProperty,Display(Name="Vigilancia persistente",GroupName="Recuperación")] public bool VigilanciaPersistente{get;set;}
        [NinjaScriptProperty,Display(Name="Ruptura posterior al umbral",GroupName="Recuperación")] public bool ExigirRupturaTrasUmbral{get;set;}
        [NinjaScriptProperty,Display(Name="Inicio bloqueo NY",GroupName="Horario")] public TimeSpan InicioBloqueo{get;set;}
        [NinjaScriptProperty,Display(Name="Fin bloqueo NY",GroupName="Horario")] public TimeSpan FinBloqueo{get;set;}
        [NinjaScriptProperty,Range(1,30),Display(Name="Margen cierre anticipado minutos",GroupName="Horario")] public int MargenCierreAnticipado{get;set;}
    }
}

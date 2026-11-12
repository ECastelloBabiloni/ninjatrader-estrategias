using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class AperturaNY_MAS_ExperimentoA : Strategy
    {
        private const string Entry = "ExpA_Long", TimedExit = "ExpA_Close";
        private MAS_SesionNY mas;
        private SessionIterator sessionIterator;
        private DateTime sessionEnd, templateEnd, effectiveClose, closeRequestTime;
        private string referenceIncluded = "", referenceExcluded = "";
        private bool latePositionReported;
        private ExperimentoAPlan plan;
        private readonly HashSet<DateTime> attempted = new HashSet<DateTime>();
        private readonly HashSet<string> executions = new HashSet<string>();
        private Order entryOrder, closeOrder;
        private DateTime tradeDate, eventTime;
        private bool closing, completed, fatal, cancelEntry, cancelSent, closeRequested;
        private bool entryTerminal;
        private int reportedEntryFills, bought, sold, inventory;
        private double activeMas, buyValue, sellValue, inventoryCost, realised;
        private double mae, mfe, maeGross, mfeGross, cumulativeNet, lastMark;
        private string exitReasons = "", zoneId, runId;
        private StreamWriter csv;
        private DateTime lastExitTime;
        private double lowestEntry, highestEntry;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AperturaNY_MAS_ExperimentoA";
                Description = "Compras NY + MAS. Gráfico de 1 minuto; señal al cierre y compra a mercado en siguiente apertura.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StartBehavior = StartBehavior.WaitUntilFlat;
                IsExitOnSessionCloseStrategy = false;
                TimeInForce = TimeInForce.Gtc;
                Slippage = 0;
                BarsRequiredToTrade = 1;
                IsInstantiatedOnEachOptimizationIteration = true;
                HoraEntrada = new TimeSpan(9, 35, 0);
                HoraCierre = new TimeSpan(15, 0, 0);
                MargenCierreMinutos = 2;
                SesionesMAS = 5;
                TipoMedia = TipoMediaMAS.Simple;
                PorcentajeMAS = 40;
                MinContratos = 1; MaxContratos = 40;
                TP_Neto_USD = 1500; SL_Neto_USD = 2000;
                ComisionPorContrato = 1.82;
                Diagnostico = true;
            }
            else if (State == State.Configure)
            {
                // Host the minute series required by MAS; no tick series is loaded.
                Calculate = Calculate.OnBarClose; // Override stale templates with OnEachTick.
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                ValidateConfiguration();
                sessionIterator = new SessionIterator(Bars);
                mas = MAS_SesionNYCalendario(SesionesMAS, TipoMedia, PorcentajeMAS, Diagnostico);
                mas.Calculate = Calculate.OnBarClose;
                runId = Guid.NewGuid().ToString("N");
                string folder = Path.Combine(Core.Globals.UserDataDir, "ExperimentoA");
                Directory.CreateDirectory(folder);
                csv = new StreamWriter(Path.Combine(folder, runId + ".csv"), false, new System.Text.UTF8Encoding(true));
                csv.AutoFlush = false;
                csv.WriteLine("run,instrument,mode,session,event_time_ny,series,zone,tick_size,point_value,entry_time_ny,close_time_ny,roundtrip_fee,event,reason,requested,filled,exited,open,mas,reference,sl_points,tp_points,reference_difference,expected_commission,expected_loss,expected_gain,entry_average,exit_average,mark,gross_realised,gross_unrealised,commission_paid,net_liquidation,mae_net,mfe_net,mae_gross,mfe_gross,equity_net,excursion_resolution,bar_high,bar_low,timestamp_kind,release,calendar,mas_included,mas_excluded,session_end_ny,template_end_ny,effective_close_ny,close_request_ny,margin_minutes,mas_sessions,mas_type,mas_percent,min_contracts,max_contracts,tp_net,sl_budget,assembly_mvid");
                Print(Name + " | CSV: " + folder + "\\" + runId + ".csv | Zona: " + zoneId);
            }
            else if (State == State.Realtime)
            {
                // Map historical order references before any cancellation in realtime.
                if (entryOrder != null && !Terminal(entryOrder.OrderState)) entryOrder = GetRealtimeOrder(entryOrder);
                if (closeOrder != null && !Terminal(closeOrder.OrderState)) closeOrder = GetRealtimeOrder(closeOrder);
                if (Account == null || (Account.Name != "Sim101" && Account.Name != "Playback101"))
                    throw new InvalidOperationException("Experimento A: solo Sim101 o Playback101; no habilitar en cuentas de fondeo.");
            }
            else if (State == State.Terminated && csv != null)
            {
                Log("END", inventory > 0 ? "INCOMPLETE_OPEN_POSITION" : "TERMINATED");
                csv.Dispose(); csv = null;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBars[0] < 1) return;
            // OnBarClose: 09:35 is the CLOSED interval 09:34-09:35.
            // A market order submitted here fills at the next available opening/event.
            eventTime = Times[0][0];
            lastMark = Closes[0][0];
            LogClosedBarRange();
            if (plan != null && !completed)
            {
                if (inventory > 0 && eventTime >= sessionEnd && !latePositionReported)
                {
                    latePositionReported = true;
                    Log("WARNING", "SESSION_END_PASSED_WITH_OPEN_POSITION: missing events or unfilled exit; inspect FILL timestamps");
                }
                if (eventTime >= tradeDate.Add(HoraEntrada).AddMinutes(1)) RequestCancel();
                if (eventTime >= effectiveClose) closing = true;
                if (closing) { RequestCancel(); Flatten(); }
                if (inventory > 0) Mark("MINUTE_CLOSE_SAMPLED");
                FinishIfSettled();
            }
            if (fatal || inventory != 0 || (plan != null && !completed) ||
                !ExperimentoAMath.IsEntryBar(eventTime, HoraEntrada) || eventTime.TimeOfDay >= HoraCierre ||
                attempted.Contains(eventTime.Date)) return;

            mas.Update();
            referenceIncluded = mas.FechaAplicacion == eventTime.Date ? mas.SesionesIncluidas : "";
            referenceExcluded = mas.FechaAplicacion == eventTime.Date ? mas.SesionesExcluidas : "";
            string scheduleError;
            if (!PrepareSchedule(out scheduleError)) {
                attempted.Add(eventTime.Date); ClearForSkip(); Log("SKIP", scheduleError); return;
            }
            // Force hosted publication; reject stale or unavailable references instead of substituting zero.
            if (!mas.EsValida || mas.FechaAplicacion != eventTime.Date || mas.SesionesUtilizadas != SesionesMAS ||
                !FinitePositive(mas.MAS_Activa_Puntos) || mas.UltimaSesionIncluida >= eventTime.Date)
            {
                attempted.Add(eventTime.Date);
                ClearForSkip();
                Log("SKIP", "MAS unavailable/stale: " + mas.EstadoCalculo);
                return;
            }
            ExperimentoAPlan next = ExperimentoAMath.Size((decimal)mas.MAS_Activa_Puntos, (decimal)PorcentajeMAS,
                (decimal)TickSize, (decimal)Instrument.MasterInstrument.PointValue, MinContratos, MaxContratos,
                (decimal)SL_Neto_USD, (decimal)TP_Neto_USD, (decimal)ComisionPorContrato);
            attempted.Add(eventTime.Date); // Set BEFORE EnterLong: Playback can execute synchronously.
            if (next == null) { ClearForSkip(); Log("SKIP", "NO_VALID_SIZE"); return; }
            ResetCampaign(next);
            // Managed engine submits OCO brackets on each actual fill at fixed tick offsets.
            // Never recalculate distances from the remaining/filled quantity.
            SetStopLoss(Entry, CalculationMode.Ticks, plan.StopTicks, false);
            SetProfitTarget(Entry, CalculationMode.Ticks, plan.TargetTicks);
            Log("SUBMIT", "CLOSED_MINUTE_NEXT_OPEN");
            EnterLong(0, plan.Quantity, Entry);
        }

        private bool PrepareSchedule(out string error)
        {
            error=""; sessionEnd=templateEnd=effectiveClose=closeRequestTime=DateTime.MinValue;
            if (Instrument.MasterInstrument.Name != "MNQ" && Instrument.MasterInstrument.Name != "NQ") { error="UNSUPPORTED_CALENDAR_INSTRUMENT"; return false; }
            bool regular; string reason;
            if (!CalendarioSesionesNY.Resolve(eventTime.Date,out sessionEnd,out regular,out reason)) { error=reason; return false; }
            if (sessionEnd==DateTime.MinValue) { error=reason; return false; }
            sessionIterator.GetNextSession(eventTime,true);
            templateEnd=sessionIterator.ActualSessionEnd;
            if (sessionIterator.ActualSessionBegin>eventTime || templateEnd<=eventTime || templateEnd.Date!=eventTime.Date) { error="UNRELIABLE_TEMPLATE_SESSION_BOUNDARY"; return false; }
            sessionEnd = sessionEnd<templateEnd?sessionEnd:templateEnd;
            effectiveClose=CalendarioSesionesNY.Cutoff(eventTime.Date,HoraCierre,sessionEnd,MargenCierreMinutos);
            if(effectiveClose<eventTime.Date.Add(HoraEntrada).AddMinutes(1)) { error="NO_ENTRY_WINDOW_BEFORE_EFFECTIVE_CLOSE"; return false; }
            return true;
        }

        private void ResetCampaign(ExperimentoAPlan next)
        {
            plan = next; tradeDate = eventTime.Date; activeMas = mas.MAS_Activa_Puntos;
            entryOrder = closeOrder = null;
            closing = completed = cancelEntry = cancelSent = closeRequested = entryTerminal = false;
            latePositionReported = false;
            reportedEntryFills = bought = sold = inventory = 0;
            buyValue = sellValue = inventoryCost = realised = mae = mfe = maeGross = mfeGross = 0;
            exitReasons = "";
            lastExitTime = DateTime.MinValue;
            lowestEntry = double.MaxValue; highestEntry = double.MinValue;
        }

        private void ClearForSkip()
        {
            plan = null; tradeDate = eventTime.Date; activeMas = double.NaN;
            bought = sold = inventory = 0;
            buyValue = sellValue = inventoryCost = realised = mae = mfe = maeGross = mfeGross = 0;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null) return;
            if (order.Name == Entry)
            {
                entryOrder = order;
                reportedEntryFills = filled;
                entryTerminal = Terminal(orderState);
                if (orderState == OrderState.Cancelled) cancelSent = true;
            }
            if (order.Name == TimedExit)
            {
                closeOrder = order;
                if (Terminal(orderState)) closeRequested = false;
            }
            // The native managed StopCancelClose handler remains responsible for fail-safe rejection handling.
            if (orderState == OrderState.Rejected || error != ErrorCode.NoError)
            {
                fatal = true; closing = true; cancelEntry = true;
                Print(Name + " | ORDER ERROR " + time + " | " + order.Name + " | " + error + " | " + nativeError);
            }
            LogAt(time, "ORDER", string.Format(CultureInfo.InvariantCulture,
                "{0}:{1}:quantity={2}:filled={3}:limit={4:R}:stop={5:R}:average={6:R}:error={7}:{8}",
                order.Name, orderState, quantity, filled, limitPrice, stopPrice, averageFillPrice, error, nativeError));
            // A cancellation intent can precede the first order callback; keep it latched.
            if (order.Name == Entry && cancelEntry) RequestCancel();
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || plan == null || quantity <= 0) return;
            if (!string.IsNullOrEmpty(executionId) && !executions.Add(executionId)) return;
            eventTime = time;
            string name = execution.Name;
            if (name == Entry)
            {
                bought += quantity; buyValue += quantity * price;
                inventoryCost += quantity * price; inventory += quantity;
                lowestEntry = Math.Min(lowestEntry, price); highestEntry = Math.Max(highestEntry, price);
                // Historical minute fills may carry the next bar's CLOSE label.
                // Use closed-bar scheduling there; do not treat that label as wall-clock execution time.
                if (State != State.Historical && time >= tradeDate.Add(HoraEntrada).AddMinutes(1)) RequestCancel();
                if ((State != State.Historical && time >= effectiveClose) || sold > 0) closing = true;
            }
            else if (execution.Order != null && execution.Order.OrderAction == OrderAction.Sell)
            {
                if (quantity > inventory)
                {
                    fatal = true;
                    LogAt(time, "ERROR", "EXIT_EXCEEDS_LEDGER: inspect account and native log");
                    return;
                }
                double average = inventoryCost / inventory;
                realised += (price - average) * quantity * Instrument.MasterInstrument.PointValue;
                inventoryCost -= average * quantity; inventory -= quantity;
                sold += quantity; sellValue += quantity * price;
                // Keep the closing label of the exit's minute for OHLC audit at bar close.
                lastExitTime = State == State.Historical ? time : time.AddTicks(
                    (TimeSpan.TicksPerMinute - time.Ticks % TimeSpan.TicksPerMinute) % TimeSpan.TicksPerMinute);
                if (inventory == 0) inventoryCost = 0;
                if (!exitReasons.Split('|').Contains(name)) exitReasons += (exitReasons.Length == 0 ? "" : "|") + name;
                // Once an exit starts, stop any remaining entry. A racing fill is protected and flattened.
                RequestCancel();
            }
            else return;
            lastMark = price;
            UpdateExcursions();
            LogAt(time, "FILL", name + ":qty=" + quantity + ":price=" + price.ToString("R", CultureInfo.InvariantCulture));
            if (closing) { RequestCancel(); Flatten(); }
            FinishIfSettled();
        }

        private void RequestCancel()
        {
            cancelEntry = true;
            if (entryOrder == null || cancelSent || Terminal(entryOrder.OrderState)) return;
            OrderState s = entryOrder.OrderState;
            if (s == OrderState.Working || s == OrderState.Accepted || s == OrderState.PartFilled)
            {
                cancelSent = true; // Before callback reentrancy.
                CancelOrder(entryOrder);
            }
        }

        private void Flatten()
        {
            // Managed ExitLong uses actual strategy position; no requested-size assumptions.
            if (inventory <= 0 || closeRequested || Position.MarketPosition != MarketPosition.Long) return;
            closeRequested = true;
            closeRequestTime = eventTime;
            Log("CLOSE_REQUEST", eventTime >= sessionEnd ? "LATE_NO_EVENT_BEFORE_SESSION_END" : fatal ? "ORDER_ERROR" : (sold > 0 ? "EXIT_RACE" : "TIME_CLOSE"));
            ExitLong(0, Position.Quantity, TimedExit, Entry);
        }

        private void FinishIfSettled()
        {
            if (plan == null || completed || !entryTerminal || bought < reportedEntryFills || inventory != 0) return;
            completed = true;
            double net = realised - bought * ComisionPorContrato;
            cumulativeNet += net;
            Log(bought > 0 ? "TRADE" : "NO_FILL", exitReasons.Length > 0 ? exitReasons : "ENTRY_CANCELLED_OR_REJECTED");
            if (csv != null) csv.Flush();
        }

        private double Unrealised()
        {
            return (lastMark * inventory - inventoryCost) * Instrument.MasterInstrument.PointValue;
        }
        private double PaidCommission() { return (bought + sold) * ComisionPorContrato / 2.0; }
        private double NetLiquidation() { return realised + Unrealised() - bought * ComisionPorContrato; }
        private void UpdateExcursions()
        {
            double value = NetLiquidation(); // Includes full round trip reserve on all filled entries.
            mae = Math.Min(mae, value); mfe = Math.Max(mfe, value);
            double gross = realised + Unrealised();
            maeGross = Math.Min(maeGross, gross); mfeGross = Math.Max(mfeGross, gross);
        }
        private void Mark(string reason) { UpdateExcursions(); Log("MARK", reason); }

        private void LogClosedBarRange()
        {
            if (plan == null || bought == 0 || eventTime.Date != tradeDate ||
                eventTime <= tradeDate.Add(HoraEntrada) || (completed && eventTime > lastExitTime)) return;
            // Whole-bar OHLC is audit data, not the exact path while contracts were open.
            bool possibleBoth = ExperimentoAMath.MayTouchBoth(Highs[0][0], Lows[0][0],
                lowestEntry, highestEntry, (double)plan.StopPoints, (double)plan.TargetPoints);
            Log("BAR_RANGE", possibleBoth ? "POTENTIAL_SL_TP_SAME_BAR_SEQUENCE_UNKNOWN" : "OHLC_SEQUENCE_UNKNOWN");
        }

        private void LogAt(DateTime time, string kind, string reason)
        {
            DateTime saved = eventTime;
            eventTime = time;
            Log(kind, reason);
            eventTime = saved;
        }
        private void Log(string kind, string reason)
        {
            if (csv == null) return;
            double unrealised = plan == null ? 0 : Unrealised();
            double net = plan == null ? 0 : NetLiquidation();
            object[] fields = { runId, Instrument.FullName, State.ToString(), tradeDate == default(DateTime) ? "" : tradeDate.ToString("yyyy-MM-dd"),
                eventTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff"), "primary:1minute:OnBarClose", zoneId, TickSize,
                Instrument.MasterInstrument.PointValue, HoraEntrada, HoraCierre, ComisionPorContrato, kind, reason,
                plan == null ? 0 : plan.Quantity, bought, sold, inventory, activeMas,
                plan == null ? 0m : plan.Reference, plan == null ? 0m : plan.StopPoints,
                plan == null ? 0m : plan.TargetPoints, plan == null ? 0m : plan.StopPoints-plan.Reference,
                plan == null ? 0m : plan.Commission, plan == null ? 0m : plan.Loss, plan == null ? 0m : plan.Gain,
                bought == 0 ? 0 : buyValue/bought, sold == 0 ? 0 : sellValue/sold, lastMark,
                realised, unrealised, PaidCommission(), net, mae, mfe, maeGross, mfeGross, cumulativeNet + (completed ? 0 : net),
                "minute_closes_and_fills_not_intrabar", kind == "BAR_RANGE" ? Highs[0][0] : double.NaN,
                kind == "BAR_RANGE" ? Lows[0][0] : double.NaN,
                kind == "FILL" ? (State == State.Historical ? "historical_engine_fill_timestamp" : "execution_timestamp") : "closed_bar_or_order_callback", CalendarioSesionesNY.Release, CalendarioSesionesNY.Version,
                referenceIncluded, referenceExcluded,
                sessionEnd, templateEnd, effectiveClose, closeRequestTime, MargenCierreMinutos, SesionesMAS, TipoMedia, PorcentajeMAS,
                MinContratos, MaxContratos, TP_Neto_USD, SL_Neto_USD, GetType().Assembly.ManifestModule.ModuleVersionId };
            csv.WriteLine(string.Join(",", fields.Select(v => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"")));
            if (Diagnostico && kind != "MARK") Print(Name + " | " + eventTime + " NY | " + kind + " | " + reason);
        }

        private static bool Terminal(OrderState state)
        {
            return state == OrderState.Filled || state == OrderState.Cancelled || state == OrderState.Rejected;
        }
        private static bool FinitePositive(double x) { return x > 0 && !double.IsNaN(x) && !double.IsInfinity(x); }
        private void ValidateConfiguration()
        {
            TimeZoneInfo zone = Core.Globals.GeneralOptions.TimeZoneInfo;
            zoneId = zone == null ? "null" : zone.Id;
            if (!ExperimentoAMath.IsNewYork(zone))
                throw new InvalidOperationException("Configure NinjaTrader Tools > Options > General > Time zone en Eastern Time (New York), con horario estacional. Zona actual: " + zoneId);
            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
                throw new InvalidOperationException("Experimento A requiere gráfico de 1 Minuto y cálculo al cierre de barra.");
            // MAS consumes closed minute bars in historical mode. Tick Replay would call
            // its historical branch intrabar and freeze an incomplete minute on its first tick.
            // This version does not require or load tick history.
            if (BarsArray.Any(b => b.IsTickReplay))
                throw new InvalidOperationException("Experimento A de 1 minuto requiere Tick Replay desactivado.");
            if (HoraEntrada < TimeSpan.Zero || HoraEntrada >= TimeSpan.FromDays(1) || HoraCierre >= TimeSpan.FromDays(1) ||
                HoraCierre < HoraEntrada.Add(TimeSpan.FromMinutes(1)) || MinContratos < 1 || MaxContratos < MinContratos ||
                !FinitePositive(TickSize) || !FinitePositive(Instrument.MasterInstrument.PointValue) ||
                !FinitePositive(PorcentajeMAS) || !FinitePositive(SL_Neto_USD) || !FinitePositive(TP_Neto_USD) ||
                HoraEntrada.Ticks % TimeSpan.TicksPerMinute != 0 || HoraCierre.Ticks % TimeSpan.TicksPerMinute != 0 ||
                MargenCierreMinutos < 2 || MargenCierreMinutos > 120 || SesionesMAS < 1 || SesionesMAS > 100 || ComisionPorContrato < 0 || double.IsNaN(ComisionPorContrato) || double.IsInfinity(ComisionPorContrato))
                throw new ArgumentException("Parámetros Experimento A no válidos.");
        }

        [NinjaScriptProperty, Display(Name="Hora entrada NY", GroupName="Horario", Order=1)] public TimeSpan HoraEntrada { get; set; }
        [NinjaScriptProperty, Display(Name="Hora cierre NY", GroupName="Horario", Order=2)] public TimeSpan HoraCierre { get; set; }
        [NinjaScriptProperty, Range(2,120), Display(Name="Margen cierre minutos", GroupName="Horario", Order=3)] public int MargenCierreMinutos { get; set; }
        [NinjaScriptProperty, Range(1,100), Display(Name="Sesiones MAS", GroupName="MAS", Order=1)] public int SesionesMAS { get; set; }
        [NinjaScriptProperty, Display(Name="Tipo media", GroupName="MAS", Order=2)] public TipoMediaMAS TipoMedia { get; set; }
        [NinjaScriptProperty, Range(0.0001,double.MaxValue), Display(Name="Porcentaje MAS", GroupName="MAS", Order=3)] public double PorcentajeMAS { get; set; }
        [NinjaScriptProperty, Range(1,int.MaxValue), Display(Name="Mínimo contratos", GroupName="Gestión", Order=1)] public int MinContratos { get; set; }
        [NinjaScriptProperty, Range(1,int.MaxValue), Display(Name="Máximo contratos", GroupName="Gestión", Order=2)] public int MaxContratos { get; set; }
        [NinjaScriptProperty, Range(0.01,double.MaxValue), Display(Name="Objetivo neto USD", GroupName="Gestión", Order=3)] public double TP_Neto_USD { get; set; }
        [NinjaScriptProperty, Range(0.01,double.MaxValue), Display(Name="Pérdida presupuestada USD", GroupName="Gestión", Order=4)] public double SL_Neto_USD { get; set; }
        [NinjaScriptProperty, Range(0,double.MaxValue), Display(Name="Comisión contrato ida/vuelta USD", GroupName="Gestión", Order=5)] public double ComisionPorContrato { get; set; }
        [NinjaScriptProperty, Display(Name="Diagnóstico", GroupName="Registro", Order=1)] public bool Diagnostico { get; set; }
    }
}

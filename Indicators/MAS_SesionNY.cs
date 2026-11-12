// MAS_SesionNY - NinjaTrader 8
// Media de Amplitud de Sesión NY: indicador informativo; no envía órdenes.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public enum ModoSesionesMAS { Compatibilidad, RegularesCalendario }
	public enum TipoMediaMAS { Simple, PonderadaLineal }

	public class MAS_SesionNY : Indicator
	{
		[Browsable(false)] public ModoSesionesMAS ModoSeleccion { get; set; }
		[Browsable(false), XmlIgnore] public string SesionesIncluidas { get; private set; }
		[Browsable(false), XmlIgnore] public string SesionesExcluidas { get; private set; }
		private const int MinutosSesionRegular = 390;
		private readonly TimeSpan inicioRth = new TimeSpan(9, 30, 0);
		private readonly TimeSpan finRth = new TimeSpan(16, 0, 0);
		private readonly List<SesionMas> sesiones = new List<SesionMas>();
		private readonly Dictionary<DateTime, ReferenciaMas> referencias = new Dictionary<DateTime, ReferenciaMas>();
		private SesionMas sesionEnCurso;
		private DateTime ultimaAperturaMinuto = DateTime.MinValue;
		private string ultimoPanel = string.Empty;

		private class SesionMas
		{
			public DateTime Fecha;
			public DateTime SiguienteApertura;
			public double Maximo = double.MinValue;
			public double Minimo = double.MaxValue;
			public int Barras;
			public bool Incompleta;
			public bool Finalizada;
			public string Motivo = string.Empty;
			public double Amplitud { get { return Maximo - Minimo; } }
		}

		private class ReferenciaMas
		{
			public DateTime Fecha;
			public bool Valida;
			public string Estado;
			public string Excluidas = "";
			public double Simple = double.NaN;
			public double Ponderada = double.NaN;
			public double Activa = double.NaN;
			public int Ticks = -1;
			public double Puntos = double.NaN;
			public List<SesionMas> Usadas = new List<SesionMas>();
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "MAS_SesionNY";
				Description = "Media de amplitud de sesiones regulares NY anteriores; no opera ni calcula contratos.";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DrawOnPricePanel = true;
				DisplayInDataBox = false;
				SesionesMedia = 5;
				TipoMedia = TipoMediaMAS.Simple;
				PorcentajeMAS = 15.0;
				MostrarPanel = true;
				MostrarComparacion = false;
				MostrarDetalleSesiones = false;
				Diagnostico = false;
				AddPlot(Brushes.Transparent, "MAS_Simple_Puntos");
				AddPlot(Brushes.Transparent, "MAS_Ponderada_Puntos");
				AddPlot(Brushes.Transparent, "MAS_Activa_Puntos");
				AddPlot(Brushes.Transparent, "Distancia_SLTP_Ticks");
				AddPlot(Brushes.Transparent, "MAS_Valida");
			}
			else if (State == State.Configure)
			{
				// Serie fija interna: evita que el periodo del gráfico cambie el cálculo.
				AddDataSeries(BarsPeriodType.Minute, 1);
			}
			else if (State == State.DataLoaded)
			{
				ValidarParametros();
				Print("MAS_SesionNY | Serie interna 1 minuto. Zona de aplicación: " + Core.Globals.GeneralOptions.TimeZoneInfo.Id);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1)
			{
				if (CurrentBars[1] < 1)
					return;

				// With an OnBarClose host, [0] is already closed in realtime as well.
				if (State == State.Historical || Calculate == Calculate.OnBarClose)
					ProcesarMinuto(0, Times[1][0]);
				else if (IsFirstTickOfBar)
					ProcesarMinuto(1, Times[1][1]);
				return;
			}

			if (BarsInProgress != 0 || CurrentBars[0] < 0)
				return;
			PublicarReferencia(Time[0].Date);
		}

		private void ProcesarMinuto(int barsAgo, DateTime cierre)
		{
			// NinjaTrader etiqueta las barras temporales por el cierre del intervalo.
			DateTime apertura = cierre.AddMinutes(-1);
			if (apertura == ultimaAperturaMinuto)
				return;
			ultimaAperturaMinuto = apertura;

			// Si faltó el cierre de la sesión anterior, queda registrada como incompleta.
			if (sesionEnCurso != null && sesionEnCurso.Fecha < apertura.Date && !sesionEnCurso.Finalizada)
				FinalizarSesion("No se completó el intervalo 09:30–16:00.");

			AsegurarReferencia(apertura.Date);

			// apertura es el cierre etiquetado menos un minuto; Time[] ya está en zona de aplicación.
			// Se incluyen 09:30–09:31 hasta 15:59–16:00: [09:30, 16:00).
			if (apertura.TimeOfDay < inicioRth || apertura.TimeOfDay >= finRth)
				return;

			if (sesionEnCurso == null || sesionEnCurso.Fecha != apertura.Date)
			{
				if (sesionEnCurso != null && !sesionEnCurso.Finalizada)
					FinalizarSesion("Cambio de fecha antes del cierre de sesión.");
				sesionEnCurso = new SesionMas
				{
					Fecha = apertura.Date,
					SiguienteApertura = apertura.Date.Add(inicioRth)
				};
				sesiones.Add(sesionEnCurso);
			}

			if (apertura != sesionEnCurso.SiguienteApertura)
			{
				sesionEnCurso.Incompleta = true;
				sesionEnCurso.Motivo = "Se esperaba " + sesionEnCurso.SiguienteApertura.ToString("HH:mm") + " y llegó " + apertura.ToString("HH:mm") + ".";
				DiagnosticoLinea("Sesión " + apertura.ToString("yyyy-MM-dd") + " incompleta: " + sesionEnCurso.Motivo);
			}

			sesionEnCurso.Barras++;
			sesionEnCurso.Maximo = Math.Max(sesionEnCurso.Maximo, Highs[1][barsAgo]);
			sesionEnCurso.Minimo = Math.Min(sesionEnCurso.Minimo, Lows[1][barsAgo]);
			sesionEnCurso.SiguienteApertura = apertura.AddMinutes(1);

			if (apertura.AddMinutes(1).TimeOfDay == finRth)
				FinalizarSesion(string.Empty);
		}

		private void FinalizarSesion(string motivoAdicional)
		{
			if (sesionEnCurso == null || sesionEnCurso.Finalizada)
				return;

			if (!string.IsNullOrEmpty(motivoAdicional))
			{
				sesionEnCurso.Incompleta = true;
				sesionEnCurso.Motivo = motivoAdicional;
			}
			if (sesionEnCurso.Barras != MinutosSesionRegular)
			{
				sesionEnCurso.Incompleta = true;
				if (string.IsNullOrEmpty(sesionEnCurso.Motivo))
					sesionEnCurso.Motivo = "Cobertura " + sesionEnCurso.Barras + "/" + MinutosSesionRegular + " minutos.";
			}
			if (sesionEnCurso.Maximo <= sesionEnCurso.Minimo || double.IsInfinity(sesionEnCurso.Maximo) || double.IsInfinity(sesionEnCurso.Minimo))
			{
				sesionEnCurso.Incompleta = true;
				sesionEnCurso.Motivo = "Amplitud no válida.";
			}

			sesionEnCurso.Finalizada = true;
			DiagnosticoLinea("Sesión " + sesionEnCurso.Fecha.ToString("yyyy-MM-dd") + ": " +
				(sesionEnCurso.Incompleta ? "no válida; " + sesionEnCurso.Motivo : "válida; amplitud=" + sesionEnCurso.Amplitud));
		}

		private void AsegurarReferencia(DateTime fecha)
		{
			if (referencias.ContainsKey(fecha))
				return;

			ReferenciaMas referencia = new ReferenciaMas { Fecha = fecha, Estado = "Datos insuficientes." };
			List<SesionMas> anteriores = sesiones.Where(s => s.Fecha < fecha).OrderByDescending(s => s.Fecha).ToList();

			if (ModoSeleccion == ModoSesionesMAS.Compatibilidad && anteriores.Count < SesionesMedia)
			{
				referencia.Estado = "Datos insuficientes: " + anteriores.Count + " de " + SesionesMedia + " sesiones anteriores.";
				referencias[fecha] = referencia;
				return;
			}

			List<SesionMas> seleccion = anteriores.Take(SesionesMedia).OrderBy(s => s.Fecha).ToList();
			if (ModoSeleccion == ModoSesionesMAS.RegularesCalendario)
            {
                List<DateTime> dates; string excluded, error;
                bool ok = CalendarioSesionesNY.Select(fecha, SesionesMedia, d => {
                    SesionMas found = anteriores.FirstOrDefault(x => x.Fecha == d);
                    return found == null ? "REGULAR_DATA_ABSENT" :
                        (!found.Finalizada || found.Incompleta ? "REGULAR_DATA_INCOMPLETE:" + (string.IsNullOrEmpty(found.Motivo) ? "No se completó el intervalo 09:30–16:00." : found.Motivo) : "");
                }, out dates, out excluded, out error);
                referencia.Excluidas = excluded;
                seleccion = dates.Select(d => anteriores.First(x => x.Fecha == d)).ToList();
                if (!ok) { referencia.Estado = error; referencia.Usadas = seleccion; referencias[fecha] = referencia; return; }
            }
            SesionMas defectuosa = seleccion.FirstOrDefault(s => !s.Finalizada || s.Incompleta);
			if (defectuosa != null)
			{
				referencia.Estado = "Sesión no válida " + defectuosa.Fecha.ToString("yyyy-MM-dd") + ": " + defectuosa.Motivo;
				referencia.Usadas = seleccion;
				referencias[fecha] = referencia;
				return;
			}

			double suma = seleccion.Sum(s => s.Amplitud);
			double sumaPonderada = 0;
			for (int i = 0; i < seleccion.Count; i++)
				sumaPonderada += (i + 1) * seleccion[i].Amplitud;

			referencia.Simple = suma / seleccion.Count;
			referencia.Ponderada = sumaPonderada / (seleccion.Count * (seleccion.Count + 1) / 2.0);
			referencia.Activa = TipoMedia == TipoMediaMAS.Simple ? referencia.Simple : referencia.Ponderada;
			double distanciaBruta = referencia.Activa * (PorcentajeMAS / 100.0);
			referencia.Ticks = (int)Math.Ceiling((distanciaBruta / TickSize) - 1e-10);
			referencia.Puntos = referencia.Ticks * TickSize;
			referencia.Valida = referencia.Ticks > 0;
			referencia.Estado = referencia.Valida ? "Calculado para hoy." : "Distancia no utilizable.";
			referencia.Usadas = seleccion;
			referencias[fecha] = referencia;
			DiagnosticoLinea("Referencia " + fecha.ToString("yyyy-MM-dd") + ": MAS=" + referencia.Activa + ", ticks=" + referencia.Ticks + ".");
		}

		private void PublicarReferencia(DateTime fecha)
		{
			if (ModoSeleccion == ModoSesionesMAS.RegularesCalendario && sesionEnCurso != null && sesionEnCurso.Fecha < fecha && !sesionEnCurso.Finalizada)
				FinalizarSesion("No se completó el intervalo 09:30–16:00.");
			AsegurarReferencia(fecha);
			ReferenciaMas referencia = referencias[fecha];
			ActualizarPropiedades(referencia);

			Values[0][0] = referencia.Valida ? referencia.Simple : double.NaN;
			Values[1][0] = referencia.Valida ? referencia.Ponderada : double.NaN;
			Values[2][0] = referencia.Valida ? referencia.Activa : double.NaN;
			Values[3][0] = referencia.Valida ? referencia.Ticks : double.NaN;
			Values[4][0] = referencia.Valida ? 1 : 0;

			if (MostrarPanel)
				ActualizarPanel(referencia);
		}

		private void ActualizarPropiedades(ReferenciaMas referencia)
		{
			FechaAplicacion = referencia.Fecha;
			EsValida = referencia.Valida;
			SesionesUtilizadas = referencia.Usadas.Count;
			MAS_Simple_Puntos = referencia.Simple;
			MAS_Ponderada_Puntos = referencia.Ponderada;
			MAS_Activa_Puntos = referencia.Activa;
			MAS_Activa_Ticks = referencia.Valida ? referencia.Activa / TickSize : double.NaN;
			DistanciaSLTPTicks = referencia.Valida ? referencia.Ticks : -1;
			DistanciaSLTPPuntos = referencia.Puntos;
			EstadoCalculo = referencia.Estado;
			SesionesIncluidas = string.Join("|", referencia.Usadas.Select(x => x.Fecha.ToString("yyyy-MM-dd")));
			SesionesExcluidas = referencia.Excluidas;
			UltimaSesionIncluida = referencia.Usadas.Count == 0 ? DateTime.MinValue : referencia.Usadas.Last().Fecha;
		}

		private void ActualizarPanel(ReferenciaMas r)
		{
			string texto = "MAS · Sesión NY\n" +
				"Fecha: " + r.Fecha.ToString("yyyy-MM-dd") + "\n" +
				"Método: " + (TipoMedia == TipoMediaMAS.Simple ? "Simple" : "Ponderada lineal") + "\n" +
				"Sesiones: " + r.Usadas.Count + "/" + SesionesMedia + " · 09:30–16:00 NY\n";

			if (r.Valida)
			{
				texto += "MAS activa: " + Formato(r.Activa) + " puntos · " + Formato(r.Activa / TickSize) + " ticks\n" +
					"Porcentaje: " + PorcentajeMAS.ToString("0.##") + " %\n" +
					"Distancia SL / TP: " + Formato(r.Puntos) + " puntos · " + r.Ticks + " ticks\n";
				if (MostrarComparacion)
					texto += "Simple: " + Formato(r.Simple) + " · Ponderada: " + Formato(r.Ponderada) + "\n";
				if (MostrarDetalleSesiones)
					foreach (SesionMas s in r.Usadas)
						texto += s.Fecha.ToString("MM-dd") + ": " + Formato(s.Amplitud) + " puntos\n";
			}
			texto += "Estado: " + r.Estado;

			if (texto == ultimoPanel) return;
			ultimoPanel = texto;
			Draw.TextFixed(this, "MAS_SesionNY_Panel", texto, TextPosition.TopLeft, Brushes.WhiteSmoke,
				new SimpleFont("Arial", 12), Brushes.DimGray, Brushes.Black, 65);
		}

		private string Formato(double valor) { return double.IsNaN(valor) ? "No disponible" : valor.ToString("0.#####"); }
		private void DiagnosticoLinea(string texto) { if (Diagnostico) Print("MAS_SesionNY | " + texto); }

		private void ValidarParametros()
		{
			if (SesionesMedia < 1 || PorcentajeMAS <= 0 || double.IsNaN(PorcentajeMAS) || double.IsInfinity(PorcentajeMAS))
				throw new ArgumentException("Parámetros MAS_SesionNY no válidos.");
		}

		[NinjaScriptProperty, Range(1, 100), Display(Name="Sesiones media", GroupName="MAS", Order=1)] public int SesionesMedia { get; set; }
		[NinjaScriptProperty, Display(Name="Tipo de media", GroupName="MAS", Order=2)] public NinjaTrader.NinjaScript.Indicators.TipoMediaMAS TipoMedia { get; set; }
		[NinjaScriptProperty, Range(0.0001, double.MaxValue), Display(Name="Porcentaje MAS", GroupName="MAS", Order=3)] public double PorcentajeMAS { get; set; }
		[NinjaScriptProperty, Display(Name="Mostrar panel", GroupName="Visual", Order=1)] public bool MostrarPanel { get; set; }
		[NinjaScriptProperty, Display(Name="Mostrar comparación", GroupName="Visual", Order=2)] public bool MostrarComparacion { get; set; }
		[NinjaScriptProperty, Display(Name="Mostrar detalle sesiones", GroupName="Visual", Order=3)] public bool MostrarDetalleSesiones { get; set; }
		[NinjaScriptProperty, Display(Name="Diagnóstico", GroupName="Diagnóstico", Order=1)] public bool Diagnostico { get; set; }

		[Browsable(false), XmlIgnore] public Series<double> MAS_Simple_Serie { get { return Values[0]; } }
		[Browsable(false), XmlIgnore] public Series<double> MAS_Ponderada_Serie { get { return Values[1]; } }
		[Browsable(false), XmlIgnore] public Series<double> MAS_Activa_Serie { get { return Values[2]; } }
		[Browsable(false), XmlIgnore] public Series<double> DistanciaSLTP_Ticks_Serie { get { return Values[3]; } }
		[Browsable(false), XmlIgnore] public Series<double> Valida_Serie { get { return Values[4]; } }

		[Browsable(false), XmlIgnore] public DateTime FechaAplicacion { get; private set; }
		[Browsable(false), XmlIgnore] public DateTime UltimaSesionIncluida { get; private set; }
		[Browsable(false), XmlIgnore] public bool EsValida { get; private set; }
		[Browsable(false), XmlIgnore] public int SesionesUtilizadas { get; private set; }
		[Browsable(false), XmlIgnore] public double MAS_Simple_Puntos { get; private set; }
		[Browsable(false), XmlIgnore] public double MAS_Ponderada_Puntos { get; private set; }
		[Browsable(false), XmlIgnore] public double MAS_Activa_Puntos { get; private set; }
		[Browsable(false), XmlIgnore] public double MAS_Activa_Ticks { get; private set; }
		[Browsable(false), XmlIgnore] public int DistanciaSLTPTicks { get; private set; }
		[Browsable(false), XmlIgnore] public double DistanciaSLTPPuntos { get; private set; }
		[Browsable(false), XmlIgnore] public string EstadoCalculo { get; private set; }
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MAS_SesionNY[] cacheMAS_SesionNY;
		public MAS_SesionNY MAS_SesionNY(int sesionesMedia, NinjaTrader.NinjaScript.Indicators.TipoMediaMAS tipoMedia, double porcentajeMAS, bool mostrarPanel, bool mostrarComparacion, bool mostrarDetalleSesiones, bool diagnostico)
		{
			return MAS_SesionNY(Input, sesionesMedia, tipoMedia, porcentajeMAS, mostrarPanel, mostrarComparacion, mostrarDetalleSesiones, diagnostico);
		}

		public MAS_SesionNY MAS_SesionNY(ISeries<double> input, int sesionesMedia, NinjaTrader.NinjaScript.Indicators.TipoMediaMAS tipoMedia, double porcentajeMAS, bool mostrarPanel, bool mostrarComparacion, bool mostrarDetalleSesiones, bool diagnostico)
		{
			if (cacheMAS_SesionNY != null)
				for (int idx = 0; idx < cacheMAS_SesionNY.Length; idx++)
					if (cacheMAS_SesionNY[idx] != null && cacheMAS_SesionNY[idx].SesionesMedia == sesionesMedia && cacheMAS_SesionNY[idx].TipoMedia == tipoMedia && cacheMAS_SesionNY[idx].PorcentajeMAS == porcentajeMAS && cacheMAS_SesionNY[idx].MostrarPanel == mostrarPanel && cacheMAS_SesionNY[idx].MostrarComparacion == mostrarComparacion && cacheMAS_SesionNY[idx].MostrarDetalleSesiones == mostrarDetalleSesiones && cacheMAS_SesionNY[idx].Diagnostico == diagnostico && cacheMAS_SesionNY[idx].EqualsInput(input))
						return cacheMAS_SesionNY[idx];
			return CacheIndicator<MAS_SesionNY>(new MAS_SesionNY(){ SesionesMedia = sesionesMedia, TipoMedia = tipoMedia, PorcentajeMAS = porcentajeMAS, MostrarPanel = mostrarPanel, MostrarComparacion = mostrarComparacion, MostrarDetalleSesiones = mostrarDetalleSesiones, Diagnostico = diagnostico }, input, ref cacheMAS_SesionNY);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MAS_SesionNY MAS_SesionNY(int sesionesMedia, NinjaTrader.NinjaScript.Indicators.TipoMediaMAS tipoMedia, double porcentajeMAS, bool mostrarPanel, bool mostrarComparacion, bool mostrarDetalleSesiones, bool diagnostico)
		{
			return indicator.MAS_SesionNY(Input, sesionesMedia, tipoMedia, porcentajeMAS, mostrarPanel, mostrarComparacion, mostrarDetalleSesiones, diagnostico);
		}

		public Indicators.MAS_SesionNY MAS_SesionNY(ISeries<double> input , int sesionesMedia, NinjaTrader.NinjaScript.Indicators.TipoMediaMAS tipoMedia, double porcentajeMAS, bool mostrarPanel, bool mostrarComparacion, bool mostrarDetalleSesiones, bool diagnostico)
		{
			return indicator.MAS_SesionNY(input, sesionesMedia, tipoMedia, porcentajeMAS, mostrarPanel, mostrarComparacion, mostrarDetalleSesiones, diagnostico);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MAS_SesionNY MAS_SesionNY(int sesionesMedia, NinjaTrader.NinjaScript.Indicators.TipoMediaMAS tipoMedia, double porcentajeMAS, bool mostrarPanel, bool mostrarComparacion, bool mostrarDetalleSesiones, bool diagnostico)
		{
			return indicator.MAS_SesionNY(Input, sesionesMedia, tipoMedia, porcentajeMAS, mostrarPanel, mostrarComparacion, mostrarDetalleSesiones, diagnostico);
		}

		public Indicators.MAS_SesionNY MAS_SesionNY(ISeries<double> input , int sesionesMedia, NinjaTrader.NinjaScript.Indicators.TipoMediaMAS tipoMedia, double porcentajeMAS, bool mostrarPanel, bool mostrarComparacion, bool mostrarDetalleSesiones, bool diagnostico)
		{
			return indicator.MAS_SesionNY(input, sesionesMedia, tipoMedia, porcentajeMAS, mostrarPanel, mostrarComparacion, mostrarDetalleSesiones, diagnostico);
		}
	}
}

#endregion


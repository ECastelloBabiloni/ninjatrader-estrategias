using NinjaTrader.NinjaScript.Indicators;
namespace NinjaTrader.NinjaScript.Indicators {
 public partial class Indicator {
  private MAS_SesionNY[] cacheMASCalendario;
  public MAS_SesionNY MAS_SesionNYCalendario(ISeries<double> input,int n,TipoMediaMAS tipo,double porcentaje,bool diagnostico) {
   if(cacheMASCalendario!=null) foreach(var x in cacheMASCalendario)
    if(x.ModoSeleccion==ModoSesionesMAS.RegularesCalendario && x.SesionesMedia==n && x.TipoMedia==tipo && x.PorcentajeMAS==porcentaje && x.Diagnostico==diagnostico && x.EqualsInput(input)) return x;
   return CacheIndicator<MAS_SesionNY>(new MAS_SesionNY { SesionesMedia=n, TipoMedia=tipo, PorcentajeMAS=porcentaje,
    MostrarPanel=false, MostrarComparacion=false, MostrarDetalleSesiones=false, Diagnostico=diagnostico,
    ModoSeleccion=ModoSesionesMAS.RegularesCalendario },input,ref cacheMASCalendario);
  }
 }
}
namespace NinjaTrader.NinjaScript.Strategies {
 public partial class Strategy {
  public MAS_SesionNY MAS_SesionNYCalendario(int n,TipoMediaMAS tipo,double porcentaje,bool diagnostico) {
   return indicator.MAS_SesionNYCalendario(Input,n,tipo,porcentaje,diagnostico);
  }
 }
}

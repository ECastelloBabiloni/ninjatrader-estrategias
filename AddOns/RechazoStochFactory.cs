// Unique accessor to the STANDARD NinjaTrader Stochastics; no oscillator reimplementation.
namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator
    {
        private Stochastics[] cacheRechazoStoch;
        public Stochastics RechazoStochNY(ISeries<double> input,int d,int k,int smooth)
        {
            if(cacheRechazoStoch!=null) foreach(var x in cacheRechazoStoch)
                if(x.PeriodD==d&&x.PeriodK==k&&x.Smooth==smooth&&x.EqualsInput(input)) return x;
            return CacheIndicator<Stochastics>(new Stochastics {PeriodD=d,PeriodK=k,Smooth=smooth},input,ref cacheRechazoStoch);
        }
    }
}
namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy
    {
        public Indicators.Stochastics RechazoStochNY(ISeries<double> input,int d,int k,int smooth)
        {return indicator.RechazoStochNY(input,d,k,smooth);}
    }
}

using System;
using NinjaTrader.NinjaScript.Strategies;
class Validation
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    static void Main()
    {
        var p = ExperimentoAMath.Size(250m,40m,.25m,2m,1,40,2000m,1500m,1.82m);
        Check(p.Quantity==10 && p.StopPoints==99m && p.TargetPoints==76m && p.Commission==18.20m && p.Loss==1998.20m && p.Gain==1501.80m,"Notion example");
        Console.WriteLine("EJEMPLO: N=10 SL=99 TP=76 C=18.20 perdida=1998.20 ganancia=1501.80 USD");
        foreach(decimal mas in new decimal[]{1m,25m,100m,250m,1000m})
        foreach(decimal point in new decimal[]{2m,20m,50m})
        foreach(decimal tick in new decimal[]{.01m,.25m,1m})
        foreach(decimal fee in new decimal[]{0m,1.82m,5m})
        {
            var best=ExperimentoAMath.Size(mas,40m,tick,point,1,40,2000m,1500m,fee);
            Check(best!=null,"candidate exists");
            Check(best.Loss<=2000m && best.Gain>=1500m && best.StopTicks>=1 && best.TargetTicks>=1,"bounds");
            for(int n=1;n<=40;n++)
            {
                decimal stop=decimal.Floor((2000m-n*fee)/(n*point*tick))*tick;
                if(stop<tick) continue;
                decimal delta=Math.Abs(stop-mas*.4m), chosen=Math.Abs(best.StopPoints-mas*.4m);
                Check(chosen<=delta && (chosen!=delta || best.Quantity<=n),"optimal size and tie");
            }
        }
        Check(ExperimentoAMath.Size(250m,40m,.25m,2m,1,40,1m,1500m,2m)==null,"no candidate");
        bool invalid=false; try { ExperimentoAMath.Size(1,40,.25m,2,2,1,2000,1500,0); } catch(ArgumentException) { invalid=true; }
        Check(invalid,"invalid settings");
        var ny=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var madrid=TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        Check(ExperimentoAMath.IsNewYork(ny),"NY zone");
        Check(!ExperimentoAMath.IsNewYork(madrid),"Madrid rejected");
        Check(!ExperimentoAMath.IsNewYork(TimeZoneInfo.CreateCustomTimeZone("fixed",TimeSpan.FromHours(-5),"fixed","fixed")),"fixed offset rejected");
        string[] dates={"2026-03-06","2026-03-09","2026-03-30","2026-10-23","2026-10-28","2026-11-03"};
        int[] hours={15,14,15,15,14,15};
        for(int i=0;i<dates.Length;i++)
        {
            DateTime date=DateTime.ParseExact(dates[i],"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
            var start=date.AddHours(9).AddMinutes(35);
            Check(!ExperimentoAMath.IsEntryBar(start.AddMinutes(-1),start.TimeOfDay),"no entry at 09:34 close");
            Check(ExperimentoAMath.IsEntryBar(start,start.TimeOfDay),"signal at 09:35 close for next open");
            Check(!ExperimentoAMath.IsEntryBar(start.AddMinutes(1),start.TimeOfDay),"no late signal at 09:36");
            Check(!ExperimentoAMath.InWindow(start.AddTicks(-1),start.TimeOfDay),"before");
            Check(ExperimentoAMath.InWindow(start,start.TimeOfDay),"start included");
            Check(ExperimentoAMath.InWindow(start.AddMinutes(1).AddTicks(-1),start.TimeOfDay),"last tick");
            Check(!ExperimentoAMath.InWindow(start.AddMinutes(1),start.TimeOfDay),"end excluded");
            Check(TimeZoneInfo.ConvertTime(start,ny,madrid).Hour==hours[i],"DST entry");
            foreach(var time in new[]{new TimeSpan(9,30,0),new TimeSpan(15,0,0),new TimeSpan(16,0,0)})
            {
                var market=date.Add(time);
                var local=TimeZoneInfo.ConvertTime(market,ny,madrid);
                Check(local-market==TimeSpan.FromHours(hours[i]-9),"DST MAS and close");
            }
            Console.WriteLine("HORARIO: "+dates[i]+" entrada NY 09:35 Madrid "+TimeZoneInfo.ConvertTime(start,ny,madrid).ToString("HH:mm")+"; cierre NY 15:00 Madrid "+TimeZoneInfo.ConvertTime(date.AddHours(15),ny,madrid).ToString("HH:mm"));
        }
        Check(ExperimentoAMath.InWindow(new DateTime(2026,11,3,10,2,30),new TimeSpan(10,2,0)),"configurable entry");
        Check(ExperimentoAMath.IsEntryBar(new DateTime(2026,11,3,10,2,0),new TimeSpan(10,2,0)),"configurable closed-bar signal");
        Check(!ExperimentoAMath.IsEntryBar(new DateTime(2026,11,3,10,2,30),new TimeSpan(10,2,0)),"no intrabar signal");
        Check(ExperimentoAMath.MayTouchBoth(20076,19901,20000,20000,99,76),"both levels touched inclusively");
        Check(!ExperimentoAMath.MayTouchBoth(20075.75,19901,20000,20000,99,76),"stop only");
        Check(!ExperimentoAMath.MayTouchBoth(20076,19901.25,20000,20000,99,76),"target only");
        Check(ExperimentoAMath.MayTouchBoth(20076,19902,20000,20001,99,76),"conservative partial-fill range flag");
        Console.WriteLine("PASS: "+count+" comprobaciones; 135 configuraciones monetarias. Sin ejecutar NinjaTrader ni backtests.");
    }
}

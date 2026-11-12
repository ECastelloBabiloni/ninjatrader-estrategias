using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript;
class CalendarValidation {
 static int count;
 static void Check(bool b,string name) { if(!b) throw new Exception(name); count++; }
 static DateTime D(string s) { return DateTime.ParseExact(s,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture); }
 static void Main() {
  foreach(string day in new[]{"2026-09-08","2026-09-09","2026-09-10","2026-09-11","2026-09-14"}) {
   List<DateTime> dates; string excluded,error;
   Check(CalendarioSesionesNY.Select(D(day),5,d=>"",out dates,out excluded,out error),day);
   Check(dates.Count==5 && dates.All(d=>d<D(day)) && !dates.Contains(D("2026-09-07")),"exclude holiday/current");
   Console.WriteLine(day+" INCLUDED="+string.Join("|",dates.Select(d=>d.ToString("yyyy-MM-dd")))+" EXCLUDED="+excluded);
  }
  List<DateTime> included; string exclusions,problem;
  Check(!CalendarioSesionesNY.Select(D("2026-09-08"),5,d=>d==D("2026-09-04")?"REGULAR_DATA_ABSENT":"",out included,out exclusions,out problem) && problem.Contains("2026-09-04:REGULAR_DATA_ABSENT"),"entire missing regular day blocks");
  Check(!CalendarioSesionesNY.Select(D("2026-09-08"),5,d=>d==D("2026-09-04")?"REGULAR_DATA_INCOMPLETE:389/390":"",out included,out exclusions,out problem),"truncated day blocks");
  DateTime end; bool regular; string reason;
  foreach(string day in new[]{"2026-06-19","2026-07-03","2026-09-07"}) {
   Check(CalendarioSesionesNY.Resolve(D(day),out end,out regular,out reason) && !regular && end==D(day).AddHours(13),"13 NY holiday end");
   var close=CalendarioSesionesNY.Cutoff(D(day),TimeSpan.FromHours(15),end,2);
   Check(close==D(day).AddHours(12).AddMinutes(58) && close.AddMinutes(1)<end,"request/following minute label before halt");
   Console.WriteLine(day+" REQUEST="+close.ToString("HH:mm")+" next-minute-label="+close.AddMinutes(1).ToString("HH:mm")+" END="+end.ToString("HH:mm")+" (synthetic schedule; no fill assertion)");
  }
  Check(CalendarioSesionesNY.Resolve(D("2025-07-03"),out end,out regular,out reason) && end.TimeOfDay==new TimeSpan(13,15,0),"different short session end");
  Check(CalendarioSesionesNY.Resolve(D("2026-04-03"),out end,out regular,out reason) && end.TimeOfDay==new TimeSpan(9,15,0),"Good Friday before entry");
  foreach(string day in new[]{"2024-12-25","2025-12-25","2026-01-01"})
   Check(CalendarioSesionesNY.Resolve(D(day),out end,out regular,out reason) && end==DateTime.MinValue && !regular,"fully closed "+day);
  foreach(string day in new[]{"2026-03-06","2026-03-09","2026-03-30","2026-10-23","2026-10-28","2026-11-03"}) {
   Check(CalendarioSesionesNY.Resolve(D(day),out end,out regular,out reason) && regular && end.Hour==17,"DST "+day);
   Check(CalendarioSesionesNY.Cutoff(D(day),TimeSpan.FromHours(15),end,2).Hour==15,"normal configured close");
  }
  Check(!CalendarioSesionesNY.Resolve(D("2027-01-04"),out end,out regular,out reason),"unknown year blocks");
  Check(!CalendarioSesionesNY.Select(D("2024-01-02"),5,d=>"",out included,out exclusions,out problem),"warmup beyond coverage blocks");
  Console.WriteLine("PASS: "+count+" calendar checks; synthetic data only. Platform backtests required.");
 }
}

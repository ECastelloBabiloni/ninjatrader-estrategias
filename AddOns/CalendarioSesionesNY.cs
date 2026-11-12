using System;
using System.Collections.Generic;
namespace NinjaTrader.NinjaScript
{
    // Finite, reviewed snapshot. Unknown dates never fall back to normal trading.
    public static class CalendarioSesionesNY
    {
        public const string Version = "CME-ETH-5119-2024-2026";
        public const string SourceSha256 = "370b17f23eeea694e686394b5fdb9b55681089c22d5232d5e6a354a314325620";
        public const string Release = "fix-mas-sesiones-cierre-anticipado-2026-11-12";
        private class ExceptionDay { public int End; public string Reason; public ExceptionDay(int end,string reason) { End=end; Reason=reason; } }
        private static readonly Dictionary<string,ExceptionDay> exceptions = new Dictionary<string,ExceptionDay> {
            { "2024-01-01", new ExceptionDay(0, "New Year's Day") },
            { "2024-01-15", new ExceptionDay(1200, "Martin Luther King, Jr. Day") },
            { "2024-02-19", new ExceptionDay(1200, "President's Day") },
            { "2024-03-28", new ExceptionDay(1600, "Day before Good Friday") },
            { "2024-03-29", new ExceptionDay(0, "Good Friday") },
            { "2024-05-27", new ExceptionDay(1200, "Memorial Day") },
            { "2024-06-19", new ExceptionDay(1200, "Juneteenth") },
            { "2024-07-03", new ExceptionDay(1215, "Day before Independence Day") },
            { "2024-07-04", new ExceptionDay(1200, "Independence Day") },
            { "2024-09-02", new ExceptionDay(1200, "Labor Day") },
            { "2024-11-28", new ExceptionDay(1200, "Thanksgiving Day") },
            { "2024-11-29", new ExceptionDay(1215, "Day after Thanksgiving Day") },
            { "2024-12-24", new ExceptionDay(1215, "Christmas Eve") },
            { "2024-12-25", new ExceptionDay(0, "Christmas Day") },
            { "2025-01-01", new ExceptionDay(0, "New Year's Day") },
            { "2025-01-09", new ExceptionDay(830, "National Day of Mourning") },
            { "2025-01-20", new ExceptionDay(1200, "Martin Luther King, Jr. Day") },
            { "2025-02-17", new ExceptionDay(1200, "President's Day") },
            { "2025-04-17", new ExceptionDay(1600, "Day before Good Friday") },
            { "2025-04-18", new ExceptionDay(0, "Good Friday") },
            { "2025-05-26", new ExceptionDay(1200, "Memorial Day") },
            { "2025-06-19", new ExceptionDay(1200, "Juneteenth") },
            { "2025-07-03", new ExceptionDay(1215, "Day before Independence Day") },
            { "2025-07-04", new ExceptionDay(1200, "Independence Day") },
            { "2025-09-01", new ExceptionDay(1200, "Labor Day") },
            { "2025-11-27", new ExceptionDay(1200, "Thanksgiving Day") },
            { "2025-11-28", new ExceptionDay(1215, "Day after Thanksgiving Day") },
            { "2025-12-24", new ExceptionDay(1215, "Christmas Eve") },
            { "2025-12-25", new ExceptionDay(0, "Christmas Day") },
            { "2026-01-01", new ExceptionDay(0, "New Year's Day") },
            { "2026-01-19", new ExceptionDay(1200, "Martin Luther King, Jr. Day") },
            { "2026-02-16", new ExceptionDay(1200, "President's Day") },
            { "2026-04-02", new ExceptionDay(1600, "Day before Good Friday") },
            { "2026-04-03", new ExceptionDay(815, "Good Friday") },
            { "2026-05-25", new ExceptionDay(1200, "Memorial Day") },
            { "2026-06-19", new ExceptionDay(1200, "Juneteenth") },
            { "2026-07-03", new ExceptionDay(1200, "Independence Day") },
            { "2026-09-07", new ExceptionDay(1200, "Labor Day") },
            { "2026-11-26", new ExceptionDay(1200, "Thanksgiving Day") },
            { "2026-11-27", new ExceptionDay(1215, "Day after Thanksgiving Day") },
            { "2026-12-24", new ExceptionDay(1215, "Christmas Eve") },
            { "2026-12-25", new ExceptionDay(0, "Christmas Day") },
        };
        public static bool Resolve(DateTime date, out DateTime end, out bool regular, out string reason)
        {
            date=date.Date; end=DateTime.MinValue; regular=false;
            reason="CALENDAR_OUT_OF_COVERAGE:"+date.ToString("yyyy-MM-dd");
            if(date.Year<2024 || date.Year>2026) return false;
            if(date.DayOfWeek==DayOfWeek.Saturday || date.DayOfWeek==DayOfWeek.Sunday) { reason="CLOSED_WEEKEND"; return true; }
            ExceptionDay exception; int hhmm=1600;
            reason="REGULAR";
            if(exceptions.TryGetValue(date.ToString("yyyy-MM-dd"),out exception)) { hhmm=exception.End; reason=exception.Reason; }
            if(hhmm==0) { reason="CLOSED:"+reason; return true; }
            // Source times are Chicago wall times; application timestamps are already NY.
            end=TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(date.AddHours(hhmm/100).AddMinutes(hhmm%100),DateTimeKind.Unspecified),
                TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"),TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
            regular=end.TimeOfDay==TimeSpan.FromHours(17);
            if(!regular) reason="SHORTENED:"+reason;
            return true;
        }
        public static bool Select(DateTime date,int count,Func<DateTime,string> defect,out List<DateTime> included,out string excluded,out string error)
        {
            included=new List<DateTime>(); excluded=""; error="";
            DateTime end; bool regular; string reason;
            if(!Resolve(date,out end,out regular,out reason)) { error=reason; return false; }
            for(DateTime d=date.Date.AddDays(-1); included.Count<count; d=d.AddDays(-1)) {
                if(!Resolve(d,out end,out regular,out reason)) { error=reason; return false; }
                if(!regular) { excluded+=(excluded.Length==0?"":"|")+d.ToString("yyyy-MM-dd")+":"+reason; continue; }
                string problem=defect(d);
                if(!string.IsNullOrEmpty(problem)) { error=d.ToString("yyyy-MM-dd")+":"+problem; return false; }
                included.Add(d);
            }
            included.Reverse(); return true;
        }
        public static DateTime Cutoff(DateTime date,TimeSpan configured,DateTime end,int margin) {
            return date.Add(configured)<end.AddMinutes(-margin)?date.Add(configured):end.AddMinutes(-margin);
        }
    }
}

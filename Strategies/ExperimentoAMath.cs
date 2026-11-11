using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    // Pure calculations: also compiled and exercised outside NinjaTrader.
    public sealed class ExperimentoAPlan
    {
        public int Quantity;
        public int StopTicks;
        public int TargetTicks;
        public decimal Reference, StopPoints, TargetPoints, Commission, Loss, Gain;
    }

    public static class ExperimentoAMath
    {
        public static ExperimentoAPlan Size(decimal mas, decimal percentage, decimal tick,
            decimal pointValue, int minimum, int maximum, decimal loss, decimal target, decimal fee)
        {
            if (mas <= 0 || percentage <= 0 || tick <= 0 || pointValue <= 0 ||
                minimum < 1 || maximum < minimum || loss <= 0 || target <= 0 || fee < 0)
                throw new ArgumentException("Configuración monetaria no válida.");
            decimal reference = mas * percentage / 100m;
            ExperimentoAPlan best = null;
            for (int n = minimum; ; n++)
            {
                decimal commission = n * fee;
                decimal st = decimal.Floor((loss - commission) / (n * pointValue * tick));
                decimal tt = decimal.Ceiling((target + commission) / (n * pointValue * tick));
                if (st >= 1 && tt >= 1 && st <= int.MaxValue && tt <= int.MaxValue)
                {
                    decimal stop = st * tick, take = tt * tick;
                    decimal predictedLoss = stop * n * pointValue + commission;
                    decimal predictedGain = take * n * pointValue - commission;
                    if (predictedLoss <= loss && predictedGain >= target &&
                        (best == null || Math.Abs(stop - reference) < Math.Abs(best.StopPoints - reference)))
                        best = new ExperimentoAPlan { Quantity = n, StopTicks = (int)st,
                            TargetTicks = (int)tt, Reference = reference, StopPoints = stop,
                            TargetPoints = take, Commission = commission, Loss = predictedLoss, Gain = predictedGain };
                }
                if (n == maximum) break; // Also avoids overflow at Int32.MaxValue.
            }
            return best; // Exact ties retain the smaller quantity.
        }

        public static bool InWindow(DateTime marketTime, TimeSpan entry)
        {
            DateTime start = marketTime.Date.Add(entry);
            return marketTime >= start && marketTime < start.AddMinutes(1);
        }

        public static bool IsEntryBar(DateTime closedBarTime, TimeSpan entry)
        {
            return closedBarTime.TimeOfDay == entry;
        }

        public static bool MayTouchBoth(double high, double low, double lowestEntry,
            double highestEntry, double stop, double target)
        {
            return high >= lowestEntry + target && low <= highestEntry - stop;
        }

        public static bool IsNewYork(TimeZoneInfo zone)
        {
            if (zone == null || !zone.SupportsDaylightSavingTime) return false;
            TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            return zone.HasSameRules(eastern);
        }
    }
}

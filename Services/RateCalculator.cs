using eMRBCS.Models;

namespace eMRBCS.Services
{
    public sealed record CommodityLine(string Label, int From, int To, decimal Quantity, decimal Rate, decimal Amount, bool IsMinimum);

    public static class RateCalculator
    {
        /// <summary>
        /// rates   = Billing_rates rows of ONE consumer type + meter size, ordered by bill_id:
        ///           row 0 = minimum (0-10), row 1 = 11-20, row 2 = 21-30 ... last row = open-ended (51-up).
        /// tierSize = width of one block (10 for your districts). commoditiesto is NOT used.
        /// Amounts are not rounded here (round at the call site).
        /// </summary>
        public static List<CommodityLine> Breakdown(IList<Billing_rates>? rates, float usage, int minUsage = 0, int tierSize = 10)
        {
            var lines = new List<CommodityLine>();
            if (rates == null || rates.Count == 0) return lines;
            if (tierSize <= 0) tierSize = 10;

            int remaining = Math.Max(0, (int)usage - Math.Max(0, minUsage));

            // row 0: minimum charge, always billed, covers 0..tierSize
            decimal minRate = (decimal)rates[0].brate;
            lines.Add(new CommodityLine($"Minimum 0-{tierSize}", 0, tierSize, tierSize, minRate, minRate, true));

            int covered = tierSize;
            remaining = Math.Max(0, remaining - tierSize);

            for (int i = 1; i < rates.Count && remaining > 0; i++)
            {
                bool isLast = i == rates.Count - 1;                       // last row = "51-up"
                int take = isLast ? remaining : Math.Min(remaining, tierSize);
                decimal rate = (decimal)rates[i].brate;

                int from = covered + 1;
                int to = covered + take;

                lines.Add(new CommodityLine($"{from}-{to}", from, to, take, rate, take * rate, false));

                covered += take;
                remaining -= take;
            }
            return lines;
        }
    }
}
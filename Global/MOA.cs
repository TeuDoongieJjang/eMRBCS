using System.Globalization;
namespace eMRBCS.Global
{
    public static class MOA
    {
        private static readonly string[] BillDateFormats =
        {
            "yyyy-MM-dd hh:mm:ss tt", "yyyy-MM-dd h:mm:ss tt", "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd", "yyyy-dd-MM", "yyyy-M-d",
            "MM-dd-yyyy", "dd-MM-yyyy", "M/d/yyyy", "d/M/yyyy",
            "MM/d/yyyy", "dd/M/yyyy", "M/dd/yyyy", "d/MM/yyyy",
            "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy h:mm:ss tt", "yyyy-MM-ddTHH:mm:ss"
        };

        /// Never throws. Falls back to today when the text is empty/unknown.
        public static DateTime ParseBillDate(string? text, DateTime? fallback = null)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Trim();
                if (DateTime.TryParseExact(text, BillDateFormats, CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out var d)) return d;
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            }
            return fallback ?? DateTime.Today;
        }
        public static string GetClientLogo(string clientCode)
        {
            if (string.IsNullOrEmpty(clientCode))
                return "MLS_Header.bmp";

            if (clientCode.Contains("[SJWD]")) return "SJWD_Header.bmp";
            if (clientCode.Contains("[MVWD]")) return "MVWD_Header.bmp";
            if (clientCode.Contains("[IWD]")) return "IndangWD_Header.bmp";
            if (clientCode.Contains("[CWD]")) return "CalambaWD_Header.bmp";
            if (clientCode.Contains("[PLAWD]")) return "PlaWD_Header.bmp";

            return "MLS_Header.bmp";
        }

        public static string Left(string text, int length)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > length ? text.Substring(0, length) : text;
        }
    }
}

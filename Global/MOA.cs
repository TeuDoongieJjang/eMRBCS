using System;
using System.Collections.Generic;
using System.Text;

namespace eMRBCS.Global
{
    public static class MOA
    {
        public static string GetClientLogo(string clientCode)
        {
            if (string.IsNullOrEmpty(clientCode))
                return "MLS_Header.bmp";

            if (clientCode.Contains("[SJWD]")) return "SJWD_Header.bmp";
            if (clientCode.Contains("[MVWD]")) return "MVWD_Header.bmp";
            if (clientCode.Contains("[IWD]")) return "IndangWD_Header.bmp";
            if (clientCode.Contains("[CWD]")) return "CalambaWD_Header.bmp";
            if (clientCode.Contains("[PlaWD]")) return "PlaWD_Header.bmp";

            return "MLS_Header.bmp";
        }

        public static string Left(string text, int length)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > length ? text.Substring(0, length) : text;
        }
    }
}

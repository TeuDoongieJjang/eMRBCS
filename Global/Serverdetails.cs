
using eMRBCS.Models;

namespace eMRBCS.Global
{
    internal class Serverdetails
    {
        public static int Validnetwork { get; set; } = 0;
        public static int NetworkID { get; set; } = 1;
        public static string ImportServerIPAddress { get; set; } = "192.168.1.10";
        public static string ImportSharedFolder { get; set; } = "SharedFolder";
        public static string ImportServerUserName { get; set; } = "";
        public static string ImportServerPassword { get; set; } = "";

        public static string ExportServerIPAddress { get; set; } = "192.168.1.10";
        public static string ExportSharedFolder { get; set; } = "SharedFolder";
        public static string ExportServerUserName { get; set; } = "";
        public static string ExportServerPassword { get; set; } = "";
        public static Server_transactions Server_Transactions { get; set; }
    }
}

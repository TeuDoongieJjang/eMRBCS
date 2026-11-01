
using eMRBCS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Global
{
    internal class Serverdetails
    {
        public static int Validnetwork { get; set; } = 0;
        public static int NetworkID { get; set; } = 1;
        public static string ImportServerIPAddress { get; set; } = "192.168.1.10";
        public static string ImportSharedFolder { get; set; } = "SharedFolder/SOURCE/";
        public static string ImportServerUserName { get; set; } = "";
        public static string ImportServerPassword { get; set; } = "";

        public static string ExportServerIPAddress { get; set; } = "192.168.1.10";
        public static string ExportSharedFolder { get; set; } = "SharedFolder/OUTPUT/";
        public static string ExportServerUserName { get; set; } = "";
        public static string ExportServerPassword { get; set; } = "";
        public static Server_transactions Server_Transactions { get; set; }
    }
}

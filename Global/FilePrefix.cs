using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Global
{
    internal class FilePrefix
    {
        public static string configPrefix { get; set; } = "yyyyMMdd";        
        public static string ConsumerFile { get; set; } = "_CONSUMERS.txt";
        public static string RemarksFile { get; set; } = "REMARKS.txt";
        public static string UsersFile { get; set; } = "USERS.txt";
        public static string BillratesFile { get; set; } = "BILLING_RATES.txt";
        public static string DailyTransactionFile { get; set; } = "_Daily_Transactions.txt";

    }
}

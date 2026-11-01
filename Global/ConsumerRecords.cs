using eMRBCS.Models;
using eMRBCS.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Global
{
    internal class ConsumerRecords
    {
        public static List<Grid> gridConsumerUnbilledCollection { get; set; }       
        public static Grid gridConsumerRecords { get; set; }
        public static List<Grid> gridConsumerBilledCollection { get; set; }
        public static Grid gridConsumerBilledRecords { get; set; }
        public static int gridBilled_rowindex { get; set; }
        public static List<Consumers> lstconsumers { get; set; }
        public static int acctID { get; set; }
        public static string acctNo { get; set; }
        public static int noofrecordsBilled { get; set; } = 0;
        public static int noofrecords_import { get; set; } = 0;
        public static int consumer_selectedtype { get; set; } = 0;
        public static bool isSearching { get; set; }
        public static int searchResult { get; set; }
        public static int isReload { get; set; }
        public static StackLayout UconsumerStacks { get; set; }
        public static StackLayout BconsumerStacks { get; set; }
        public static int batchctr_u { get; set; } = 0;
        public static int batchctr_b { get; set; } = 0;

        public static double Uscrollheight { get; set; } = 0;

        public static double Bscrollheight { get; set; } = 0;

        public static bool isFromCache { get; set; } = false;

        public static bool isGridReloaded { get; set; } = false;

        public static List<int> Ubatch_loaded { get; set; }

        public static List<int> Bbatch_loaded { get; set; }

        public static ReadingConsumers consumerPage { get; set; }

        public static List<Daily_transactions> daily_Transactions { get; set; }

        public static List<string> AutoID_UIndex { get; set; }
        public static List<string> AutoID_BIndex { get; set; }

        public static string ErrorSendData { get; set; }

    }
}

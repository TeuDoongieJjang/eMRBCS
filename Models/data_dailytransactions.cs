using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    internal class Daily_transactions
    {
        [PrimaryKey, AutoIncrement]
        public int trxno { get; set; }

        public int isbilled { get; set; }

        public string accountno { get; set; }

        public string consumer_name { get; set; }

        public string address { get; set; }

        public string meterno { get; set; }

        public int consumer_type { get; set; }

        public float prevreading { get; set; }

        public float presreading { get; set; }

        public float usage { get; set; }
        public string billdate { get; set; }
        public float billamount { get; set; }
        public string remarks { get; set; }
        public float arrears { get; set; }

        public float other_charges { get; set; }

        public string billmonth { get; set; }
        public float penaltyamt { get; set; }

        public string duedate { get; set; }

        public string device_user { get; set; }

        public float surcharge { get; set; }

        public float pca_amount { get; set; }

        public string ornumber { get; set; }

        public string collection_date { get; set; }
        public float amount_collected { get; set; }

        public int number_of_print { get; set; }

        public long seqno { get; set; }

        public string remarks_explanation { get; set; }

        public string trx_start { get; set; }
        public string trx_end { get; set; }
        public string timestamp { get; set; }

    }
}

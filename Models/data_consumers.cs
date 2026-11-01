using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    public class Consumers
    {
        [PrimaryKey, AutoIncrement]
        public int accountID { get; set; }
        public string accountNo { get; set; }
        public string consumer_name { get; set; }
        public string address { get; set; }
        public string meterno { get; set; }
        public float prevreading { get; set; }
        public float presreading { get; set; }
        public string consumer_status { get; set; }
        public float arrears { get; set; }
        public float average_usage { get; set; }
        public float special_penalty_rate { get; set; }
        public string billdate { get; set; }        
        public string duedate { get; set; }
        public int metersize { get; set; }
        public float othercharges { get; set; }
        public int consumer_type { get; set; }
        public long seqno { get; set; }
        public string othercharges_details { get; set; }
        public float agecurrent { get; set; }
        public float penaltyamt { get; set; }
        public int isnopenalty { get; set; }
        public int minusage { get;  set; }
        public string combine_rates { get; set; }
        public float change_meter_usage { get; set; }
        public string  zonecode { get;set; }
        public string prevreadingdate { get; set; }    
        public int unpaidmonths { get; set; }
        public string waterbill_details { get; set; }
        public float sewerage { get; set; }
        public string parameters { get;set; }
        public int status { get; set; }
        public string ornumber { get; set; }
        public string ordate { get; set; }
        public double amountcollected { get; set; }
        public string billedby { get; set; }
        public string remarks { get; set; }
        public float total_bill    { get; set; }        
        public string collectiondate    { get; set; }  
        public float usage { get; set; }
        public float currentbill { get; set; }
        public int less_usage { get; set; }
        public int isflat_rate { get; set; }
        public int iszero_min { get; set; }
        public string Notif { get; set; }
    }
}

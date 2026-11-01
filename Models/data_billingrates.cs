using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    public class Billing_rates
    {
        [PrimaryKey, AutoIncrement]
        public int bill_id { get; set; }
        public int consumer_type { get; set; }
        public string constype_description { get; set; }
        public int metersize { get; set; }
        public int commoditiesto { get; set; }
        public float brate { get; set; }
        public float pca_rate { get; set; }
        public int graceperioddue { get; set; }
        public float sewerage { get; set; }
        public float ftax { get; set; }

    }
}

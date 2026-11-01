using eMRBCS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Global
{
    internal class OtherRecords
    {
        public static List<Remarks> LstRemarks { get; set; }

        public static int noofremarks { get; set; }

        public static List<Billing_rates> LstBillrates { get; set; }

        public static int noofbillrates { get; set; }

        public static List<Users> LstUsers { get; set; }

        public static int noofusers { get; set; }

    }
}

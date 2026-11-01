using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    internal class User_activity
    {
        [PrimaryKey, AutoIncrement]
        public int id { get; set; }
        public string form_use { get; set; }
        public string log_datetime { get; set; }
        public string username { get; set; }
        public string status { get; set; }
        public string activity { get; set; }
        public int billedconsumer { get; set; }
        public int collectedconsumer { get; set; }
        public int unbilledconsumer { get; set; }

        public int lvlaccess { get; set; }
    }
}

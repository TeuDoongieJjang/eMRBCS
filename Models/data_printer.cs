using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    internal class Printerconfig
    {
        [PrimaryKey, AutoIncrement]
        public int id { get; set; }

        public string profile_name { get; set; }
        public string bluetooth_address { get; set; }
        public string profileconfig { get; set; }

        public int linespacing { get; set; }

        public int isdefault { get; set; }



    }
}

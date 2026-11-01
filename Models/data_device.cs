using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    internal class Device_info
    {
        [PrimaryKey, AutoIncrement]
        public int id { get; set; }
        public string aid { get; set; }
        public string name { get; set; }
        public string version { get; set; }
        public string company_name { get; set; }
        public string logo_filename { get; set; }
    }
}

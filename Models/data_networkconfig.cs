using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    public class Networkconfig
    {
        [PrimaryKey, AutoIncrement]
        public int id { get; set; }
        public string import_ipaddress { get; set; }
        public string import_folder { get; set; }
        public string import_user { get; set; }
        public string import_pass { get; set; }
        public string export_ipaddress { get; set; }
        public string export_folder { get; set; }
        public string export_user { get; set; }
        public string export_pass { get; set; }
    }
}

using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    public class Remarks
    {
        [PrimaryKey, AutoIncrement]
        public int remarkid { get; set; }
        public int codeid { get; set; }
        public string description { get; set; }
    }
}

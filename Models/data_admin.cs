using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    internal class Users_admin
    {
        [PrimaryKey, AutoIncrement]
        public int id { get; set; }

        public string username { get; set; }

        public string password { get; set; }

        public string key { get; set; }

        public int role { get; set; }

        public string name { get; set; }

        public string position { get; set; }
    }
}

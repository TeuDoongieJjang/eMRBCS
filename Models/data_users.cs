using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    public class Users
    {
        [PrimaryKey, AutoIncrement]
        public int userid { get; set; }
        public string usercode { get; set; }
        public string username { get; set; }
        public string password { get; set; }

        public string fullname { get; set; }

        public string billdate { get; set; }

        public string lasttrx_saved { get; set; }
        public string msgmo { get; set; }

        public string billmonth { get; set; }
        public string trx_mode { get; set; }

        public string operator_password { get; set; }




    }
}

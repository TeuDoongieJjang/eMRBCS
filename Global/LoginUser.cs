using eMRBCS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Global
{
    internal class LoginUser
    {
        public static int Id { get; set; }
        public static string username { get; set; }
        public static string user_fullname { get; set; }       
        public static string user_position { get; set; }

        public static int role { get; set; } //1 - master, 2 - admin, 3 - user

        public static Users login_user { get; set; }

    }
}

using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eMRBCS.Models
{
    public class Server_transactions
    {
        [PrimaryKey, AutoIncrement]
        public int id { get; set; }
        public string latest_importdate { get; set; }
        public int consumers_file { get; set; }
        public int remark_file { get; set; }
        public int billrate_file { get; set; }
        public int users_file { get; set; }
        public string latest_exportdate { get; set; }
        public int daily_transaction_file { get; set; }
        public string user_import { get; set; }
        public string user_export { get; set; }

    }
}

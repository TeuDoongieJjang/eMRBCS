using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SQLite;
using eMRBCS.Models;

namespace eMRBCS.Services
{
    public class sqlitecon
    {
        private readonly SQLiteAsyncConnection _database;

        public sqlitecon(string dbPath)
        {
            _database = new SQLiteAsyncConnection(dbPath);            
        }

        public Task<List<Remarks>> GetItemsAsync()
        {
            return _database.Table<Remarks>().ToListAsync();
        }

        public Task<Remarks> GetItemAsync(int id)
        {
            return _database.Table<Remarks>().Where(i => i.remarkid == id).FirstOrDefaultAsync();
        }

        public Task<int> SaveItemAsync(Remarks item)
        {
            if (item.codeid != 0)
            {
                return _database.UpdateAsync(item);
            }
            else
            {
                return _database.InsertAsync(item);
            }
        }

        public Task<int> DeleteItemAsync(Remarks item)
        {
            return _database.DeleteAsync(item);
        }
    }
}

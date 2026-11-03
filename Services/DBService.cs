using eMRBCS.Global;
using eMRBCS.Models;
using SQLite;
using System.Windows.Input;




namespace eMRBCS.Services
{
    internal class DBService
    {
        private static SQLiteConnection Database;
        string targetfilename = database_info.dbName;

        List<Grid> gridConsumers;
        Grid tmpgridConsumer;
        int rowindex = 0;
        int batchctr = 0;
        private ICommand FrameTappedCommand { get; set; }

        int noofrecords = 0;

        List<Remarks> remark_items;
        List<Consumers> consumer_items;
        List<Billing_rates> billrates_items;
        List<Users> user_items;

        GridService gridDataService;

        private void InitializeDatabase()
        {
            if (Database == null)
            {
                string dbPath = Path.Combine(FileSystem.AppDataDirectory, targetfilename);
                Database = new SQLiteConnection(dbPath);
            }
        }

        private static int CheckRecordExists(string recordKey, int tableType)
        {
            int isExists = 0;
            try
            {
                switch (tableType)
                {
                    case 0:
                        var consumer = Database.Table<Consumers>().FirstOrDefault(p => p.accountNo == recordKey);
                        isExists = consumer == null ? 0 : 1;
                        break;
                    case 1:
                        var remark = Database.Table<Remarks>().FirstOrDefault(p => p.remarkid == int.Parse(recordKey));
                        isExists = remark == null ? 0 : 1;
                        break;
                    case 2:
                        var billrate = Database.Table<Billing_rates>().FirstOrDefault(p => p.bill_id == int.Parse(recordKey));
                        isExists = billrate == null ? 0 : 1;
                        break;
                }
            }
            catch (Exception ex)
            {
                isExists = 0;
            }

            return isExists;

        }

        private static float ParseFloat(string value)
        {
            if (float.TryParse(value, out float result))
            {
                return result;
            }
            else
            {
                return 0f;
            }
        }

        public async Task<bool> UserReadAndProcessileAsync(string sourcefile)
        {

            int userid;
            string usercode;
            string username;
            string password;
            string fullname;
            string billdate;
            string lasttrx_saved;
            string msgmo;
            int billmonth;
            int trx_mode;

            string operator_password;

            InitializeDatabase();

            user_items = new List<Users>();

            string filePath = sourcefile;

            using (StreamReader reader = new StreamReader(filePath))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    string[] items = line.Split('~');
                    username = items[2].Trim();
                    //msgmo = items[7].Trim();

                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        Users newItem = new Users
                        {
                            usercode = items[0].Trim(),
                            username = items[2].Trim(),
                            password = items[3].Trim(),
                            fullname = items[4].Trim(),
                            billdate = items[5].Trim(),
                            lasttrx_saved = items[6].Trim(),
                            msgmo = items[7].Trim(),
                            billmonth = items[8].Trim(),
                            trx_mode = items[9].Trim(),
                            operator_password = items[10].Trim()

                        };

                        user_items.Add(newItem);
                    }
                }

            }
            bool isDone = await UserSaveItemsToDatabase(user_items);
            return isDone;
        }

        private async Task<bool> UserSaveItemsToDatabase(List<Users> items)
        {
            noofrecords = 0;
            bool isSuccess = true;
            await Task.Run(() =>
            {
                DeleteAllRecords("Users");
                foreach (var item in items)
                {
                    try
                    {
                        noofrecords++;
                        Database.Insert(item);
                    }
                    catch (Exception ex)
                    {

                    }

                }
                OtherRecords.noofusers = noofrecords;
                OtherRecords.LstUsers = items;
            });
            return isSuccess;
        }

        public async Task<bool> ConsumerReadAndProcessileAsync(string sourcefile)
        {
            string accountNo;
            string consumer_name;
            string address;
            string meterno;
            float prevreading;
            float presreading;
            string consumer_status;
            float arrears;
            float average_usage;
            float special_penalty_rate;
            string billdate;
            string duedate;
            int metersize;
            float othercharges;
            int consumer_type;
            long seqno;
            string othercharges_details;
            float agecurrent;
            float penaltyamt;
            int isnopenalty;
            int minusage;
            float combine_rates;
            float change_meter_usage;
            string zonecode;
            string prevreadingdate;
            int unpaidmonths;
            string waterbill_details;
            float sewerage;
            string parameters;
            int status;

            InitializeDatabase();

            consumer_items = new List<Consumers>();

            string filePath = sourcefile;

            using (StreamReader reader = new StreamReader(filePath))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    string[] items = line.Split('~');
                    accountNo = items[0].Trim();

                    if (!string.IsNullOrWhiteSpace(accountNo))
                    {
                        Consumers newItem = new Consumers
                        {
                            accountNo = items[0].Trim(),
                            consumer_name = items[1].Trim(),
                            address = items[2].Trim(),
                            meterno = items[3].Trim(),
                            prevreading = ParseFloat(items[4]),
                            presreading = 0,
                            consumer_status = items[6].Trim(),
                            arrears = ParseFloat(items[7]),
                            average_usage = ParseFloat(items[8]),
                            special_penalty_rate = ParseFloat(items[9]),
                            billdate = items[10].Trim(),
                            duedate = items[11].Trim(),
                            metersize = int.Parse(items[12].Trim()),
                            othercharges = ParseFloat(items[13].Trim()),
                            consumer_type = int.Parse(items[14].Trim()),
                            seqno = long.Parse(items[15].Trim()),
                            othercharges_details = items[16].Trim(),
                            agecurrent = int.Parse(items[17].Trim()),
                            penaltyamt = ParseFloat(items[18].Trim()),
                            isnopenalty = items[19].Trim() == "Y" ? 1 : 0,
                            minusage = int.Parse(items[20].Trim()),
                            combine_rates = items[21].Trim(),
                            change_meter_usage = ParseFloat(items[22].Trim()),
                            zonecode = items[23].Trim(),
                            prevreadingdate = items[24].Trim(),
                            unpaidmonths = int.Parse(items[25].Trim()),
                            waterbill_details = items[26].Trim(),
                            sewerage = ParseFloat(items[27].Trim()),
                            parameters = items[28].Trim(),
                            status = 0 //Default for Unbilled
                        };

                        consumer_items.Add(newItem);

                    }

                }
                ConsumerRecords.lstconsumers = consumer_items;
            }

            bool isDone = await ConsumerSaveItemsToDatabase(consumer_items);
            gridDataService = new GridService();
            //Initialize Grid for Bills
            ConsumerRecords.gridConsumerBilledCollection = new List<Grid>();
            ConsumerRecords.gridBilled_rowindex = 0;

            ConsumerRecords.AutoID_UIndex = new List<string>();
            ConsumerRecords.AutoID_BIndex = new List<string>();

            isDone = await gridDataService.ConsumerToGrid(consumer_items);
            return isDone;

        }

        private void DeleteAllRecords(string tableName)
        {
            string querycmd = "DELETE FROM " + tableName.ToLower();
            Database.Execute(querycmd);
            querycmd = "DELETE FROM sqlite_sequence WHERE name = '" + tableName.ToLower() + "'";
            Database.Execute(querycmd);
        }



        public async Task<bool> RequestForConsumers()
        {
            bool isDone = true;
            await LoadConsumersToList();
            gridDataService = new GridService();
            isDone = await gridDataService.ConsumerToGrid(ConsumerRecords.lstconsumers);
            return isDone;
        }

        private async Task LoadConsumersToList()
        {
            bool isDone = true;
            await Task.Run(() =>
            {
                ConsumerRecords.lstconsumers = Database.Table<Consumers>().OrderBy(c => c.accountID).ToList();
                ConsumerRecords.noofrecords_import = ConsumerRecords.lstconsumers.Count;
            });

        }

        public async Task<List<Consumers>> LoadReportConsumersToList(int rptRequest)
        {
            List<Consumers> _newlist = new List<Consumers>();
            await Task.Run(() =>
            {
                if (rptRequest == 1)
                {
                    _newlist = Database.Table<Consumers>().Where(c => c.status == 1).OrderBy(c => c.accountID).ToList();
                }


            });
            return _newlist;
        }

        public async Task<List<Consumers>> LoadReportUnbilledConsumers(int rptRequest)
        {
            List<Consumers> _newlist = new List<Consumers>();
            await Task.Run(() =>
            {
                if (rptRequest == 1)
                {
                    _newlist = Database.Table<Consumers>().Where(c => c.status != 1).OrderBy(c => c.accountID).ToList();
                }


            });
            return _newlist;
        }

        public async Task<List<Consumers>> LoadReportConsumerswithRemarks(int rptRequest)
        {
            List<Consumers> _newlist = new List<Consumers>();
            await Task.Run(() =>
            {
                if (rptRequest == 1)
                {
                    _newlist = Database.Table<Consumers>().Where(c => c.remarks != "" && c.remarks != "Normal Reading").OrderBy(c => c.accountID).ToList();
                }
            });
            return _newlist;
        }

        private async Task<bool> ConsumerSaveItemsToDatabase(List<Consumers> items)
        {
            bool isSuccess = true;
            int noofrecords = 0;
            await Task.Run(() =>
            {
                try
                {
                    DeleteAllRecords("Consumers");
                    //Arrange by seq no before saving to DB
                    items.OrderBy(c => c.seqno).ToList();
                    foreach (var item in items)
                    {
                        try
                        {
                            Database.Insert(item);
                            noofrecords++;
                        }
                        catch (Exception ex1)
                        {

                        }
                    }
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }
            });
            ConsumerRecords.noofrecords_import = noofrecords;
            return isSuccess;
        }

        public async Task<bool> ConsumerUpdateItemToDatabase(Consumers consumer)
        {
            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    //var consumer = Database.Table<Consumers>().FirstOrDefault(p => p.accountNo == _consumer.accountNo);
                    //consumer.presreading = _consumer.presreading;
                    consumer.status = 1;
                    consumer.billedby = LoginUser.login_user.fullname;
                    consumer.billdate = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
                    //consumer.total_bill = _consumer.total_bill;
                    //consumer.usage = _consumer.usage;
                    //consumer.currentbill = -consumer.currentbill;
                    Database.Update(consumer);
                    ConsumerRecords.noofrecordsBilled++;

                    //Update also the one in the list
                    var consumer_list = ConsumerRecords.lstconsumers.Where(c => c.accountNo == consumer.accountNo).FirstOrDefault();
                    if (consumer_list != null)
                    {
                        consumer_list = consumer;
                    }
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }
            });

            return isSuccess;
        }

        public async Task<bool> RemarkReadAndProcessFileAsync(string sourcefile)
        {

            string newcodeid;
            string newdescription;

            InitializeDatabase();

            remark_items = new List<Remarks>();

            string filePath = Path.Combine(FileSystem.AppDataDirectory, sourcefile);

            using (StreamReader reader = new StreamReader(filePath))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    string[] items = line.Split('~');
                    newcodeid = items[0].Trim();
                    newdescription = items[1].Trim();

                    if (!string.IsNullOrWhiteSpace(newcodeid))
                    {
                        Remarks newItem = new Remarks
                        {
                            codeid = int.Parse(newcodeid),
                            description = newdescription
                        };

                        remark_items.Add(newItem);

                    }

                }
            }

            bool isDone = await RemarksSaveItemsToDatabase(remark_items);
            OtherRecords.LstRemarks = new List<Remarks>();
            OtherRecords.LstRemarks = remark_items;
            return isDone;

        }

        public async Task<List<Remarks>> GetAllRemarks()
        {
            remark_items = new List<Remarks>();
            await Task.Run(() =>
            {

                remark_items = Database.Table<Remarks>().ToList();

            });
            return remark_items;
        }

        private async Task<bool> RemarksSaveItemsToDatabase(List<Remarks> items)
        {
            bool isSuccess = true;
            noofrecords = 0;
            await Task.Run(() =>
            {
                DeleteAllRecords("Remarks");
                foreach (var item in items)
                {
                    try
                    {
                        Database.Insert(item);
                        noofrecords++;
                    }
                    catch (Exception ex)
                    {


                    }


                }
            });
            OtherRecords.noofremarks = noofrecords;
            return isSuccess;
        }

        public async Task<bool> BillRatesReadAndProcessFileAsync(string sourcefile)
        {

            int consumer_type;
            string constype_description;
            int metersize;
            int commoditiesto;
            float brate;
            float pca_rate;
            int graceperioddue;
            float sewerage;

            InitializeDatabase();

            billrates_items = new List<Billing_rates>();

            string filePath = Path.Combine(FileSystem.AppDataDirectory, sourcefile);

            using (StreamReader reader = new StreamReader(filePath))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    string[] items = line.Split('~');
                    consumer_type = int.Parse(items[0].Trim());

                    if (consumer_type != 0)
                    {
                        Billing_rates newItem = new Billing_rates
                        {
                            consumer_type = int.Parse(items[0].Trim()),
                            constype_description = items[1].Trim(),
                            metersize = int.Parse(items[2].Trim()),
                            commoditiesto = int.Parse(items[3].Trim()),
                            brate = ParseFloat(items[4].Trim()),
                            pca_rate = ParseFloat(items[5].Trim()),
                            graceperioddue = int.Parse(items[6].Trim()),
                            sewerage = ParseFloat(items[7].Trim())

                        };

                        billrates_items.Add(newItem);
                    }

                }
            }

            bool isDone = await BillratesSaveItemsToDatabase(billrates_items);
            OtherRecords.LstBillrates = new List<Billing_rates>();
            OtherRecords.LstBillrates = billrates_items;
            return isDone;

        }

        private async Task<bool> BillratesSaveItemsToDatabase(List<Billing_rates> items)
        {
            bool isSuccess = true;
            noofrecords = 0;
            await Task.Run(() =>
            {
                DeleteAllRecords("Billing_rates");
                foreach (var item in items)
                {
                    try
                    {
                        noofrecords++;
                        Database.Insert(item);
                    }
                    catch (Exception ex)
                    {

                    }

                }
            });

            OtherRecords.noofbillrates = noofrecords;
            return isSuccess;
        }

        public async Task<List<Billing_rates>> GetAllBillrates()
        {
            billrates_items = new List<Billing_rates>();
            await Task.Run(() =>
            {

                billrates_items = Database.Table<Billing_rates>().ToList();

            });
            return billrates_items;
        }

        public async Task<List<Billing_rates>> GetBillrates(int consumerType, int metersize)
        {
            List<Billing_rates> billrates = new List<Billing_rates>();
            await Task.Run(() =>
            {

                billrates = Database.Table<Billing_rates>().Where(b => b.consumer_type == consumerType && b.metersize == metersize).ToList();

            });
            return billrates;
        }




        public async Task<bool> NetworkConfigSave()
        {
            bool isSuccess = true;
            await Task.Run(() =>
            {
                InitializeDatabase();
                Networkconfig networkconfig = new Networkconfig
                {
                    id = Serverdetails.NetworkID,
                    import_ipaddress = Serverdetails.ImportServerIPAddress,
                    import_folder = Serverdetails.ImportSharedFolder,
                    import_user = Serverdetails.ImportServerUserName,
                    import_pass = Serverdetails.ImportServerPassword,
                    export_ipaddress = Serverdetails.ExportServerIPAddress,
                    export_folder = Serverdetails.ExportSharedFolder,
                    export_user = Serverdetails.ExportServerUserName,
                    export_pass = Serverdetails.ExportServerPassword
                };

                NetworkSaveItemsToDatabase(networkconfig);
            });

            return isSuccess;
        }

        public async Task<Networkconfig> GetExistingNetwork()
        {
            Networkconfig networkconfig = new Networkconfig();
            await Task.Run(() =>
            {
                InitializeDatabase();
                networkconfig = Database.Table<Networkconfig>().OrderBy(r => r.id).FirstOrDefault();
            });
            return networkconfig;
        }

        private void NetworkSaveItemsToDatabase(Networkconfig item)
        {
            Database.Update(item);

        }

        public async Task<Users> GetLoginUser(string username, string password)
        {
            Users loginuser = new Users();
            await Task.Run(() =>
            {
                InitializeDatabase();
                loginuser = Database.Table<Users>().FirstOrDefault(u => u.username == username && u.password == password);
            });

            return loginuser;

        }

        public async Task<Users_admin> GeAdminUser(string username, string password)
        {
            Users_admin loginuser = new Users_admin();
            await Task.Run(() =>
            {
                InitializeDatabase();
                loginuser = Database.Table<Users_admin>().FirstOrDefault(u => u.username == username && u.password == password);

            });

            return loginuser;

        }

        public async Task<bool> UpdateAdminUser(string username, string password, string fullname, string position)
        {
            Users_admin _user = new Users_admin();
            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();
                    _user = Database.Table<Users_admin>().FirstOrDefault(u => u.username == username);
                    if (_user != null)
                    {
                        _user.username = username;
                        _user.name = fullname;
                        _user.position = position;
                        _user.password = password;
                        Database.Update(_user);

                        Users updatedUser = new Users
                        {
                            userid = _user.id,
                            username = _user.username,
                            fullname = _user.name,
                            trx_mode = _user.position,
                            password = password,
                        };
                        LoginUser.login_user = updatedUser;
                        LoginUser.user_position = _user.position;
                    }
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }


            });

            return isSuccess;

        }

        public async Task<bool> SaveAdminOperators(string username, string password, string fullname, string position)
        {
            Users_admin _user = new Users_admin();
            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();
                    _user = Database.Table<Users_admin>().FirstOrDefault(u => u.username == username);
                    if (_user != null)
                    {
                        _user.username = username;
                        _user.name = fullname;
                        _user.position = position;
                        _user.password = password;
                        Database.Update(_user);
                    }
                    else
                    {
                        _user = new Users_admin
                        {
                            username = username,
                            name = fullname,
                            position = position,
                            password = password,
                            role = 2
                        };
                        Database.Insert(_user);

                    }
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }


            });

            return isSuccess;

        }

        public async Task<List<Users_admin>> LoadAllAdmins()
        {
            List<Users_admin> _adminusers = new List<Users_admin>();

            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();
                    _adminusers = Database.Table<Users_admin>().Where(u => u.role == 2).ToList();
                }
                catch (Exception ex)
                {

                }


            });

            return _adminusers;

        }

        public async Task<bool> SaveUserActivity(string formname, string activity, string status)
        {

            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();
                    User_activity _useractivity = new User_activity
                    {
                        form_use = formname,
                        log_datetime = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt"),
                        username = LoginUser.login_user.username,
                        status = status,
                        activity = activity,
                        billedconsumer = ConsumerRecords.noofrecordsBilled,
                        unbilledconsumer = ConsumerRecords.noofrecords_import,
                        lvlaccess = LoginUser.role
                    };
                    Database.Insert(_useractivity);

                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }


            });

            return isSuccess;


        }

        public async Task<Server_transactions> GetLatestImportDate()
        {
            Server_transactions server_transactions = new Server_transactions();
            await Task.Run(() =>
            {
                InitializeDatabase();
                server_transactions = Database.Table<Server_transactions>().Where(s => s.user_import == LoginUser.login_user.username).OrderByDescending(s => s.id).FirstOrDefault();
            });

            return server_transactions;

        }



        public async Task<List<User_activity>> GetAllUserActivity(int role)
        {
            List<User_activity> userActivity_items = new List<User_activity>();
            await Task.Run(() =>
            {
                InitializeDatabase();
                userActivity_items = Database.Table<User_activity>().Where(u => u.lvlaccess > role).OrderBy(u => u.id).ToList();
            });

            return userActivity_items;

        }


        public async Task<bool> ServerImportSave(int consumerfile, int remarkfile, int billratefile, int userfile)
        {
            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();

                    Server_transactions server_Transactions = new Server_transactions
                    {
                        latest_importdate = DateTime.Now.ToString(),
                        consumers_file = consumerfile,
                        billrate_file = billratefile,
                        remark_file = remarkfile,
                        users_file = userfile,
                        user_import = LoginUser.login_user.username
                    };

                    Database.Insert(server_Transactions);
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }

            });

            return isSuccess;
        }

        public async Task<bool> ServerExportUpdate()
        {
            bool isSuccess = true;
            Server_transactions _server_transactions = new Server_transactions();
            _server_transactions = await GetLatestImportDate();
            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();
                    if (_server_transactions != null)
                    {
                        _server_transactions.latest_exportdate = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
                        _server_transactions.daily_transaction_file = 1;
                        _server_transactions.user_export = LoginUser.login_user.username;
                        Database.Update(_server_transactions);
                    }
                    else
                    {
                        Server_transactions server_Transactions = new Server_transactions
                        {
                            latest_importdate = DateTime.Now.ToString(),
                            consumers_file = 0,
                            billrate_file = 0,
                            remark_file = 0,
                            users_file = 0,
                            user_import = LoginUser.login_user.username
                        };

                        Database.Insert(server_Transactions);
                    }

                }
                catch (Exception ex)
                {

                    ConsumerRecords.ErrorSendData = "Updating Server Transaction. " + ex.Message;
                    isSuccess = false;
                }

            });

            return isSuccess;
        }

        public async Task<bool> PrepareTransaction()
        {
            bool isSuccess = true;
            List<Daily_transactions> dailyTrans = new List<Daily_transactions>();
            await Task.Run(() =>
            {

                InitializeDatabase();
                DeleteAllRecords("daily_transactions");
                //Get billed consumers
                List<Consumers> consumers = new List<Consumers>();
                //consumers = Database.Table<Consumers>().Where(c => c.status==1).ToList();
                consumers = Database.Table<Consumers>().Where(c => c.status == 1).OrderByDescending(c => c.billdate).ToList();
                if (consumers.Count > 0)
                {
                    foreach (Consumers consumer in consumers)
                    {
                        try
                        {
                            Daily_transactions _dailytrans = new Daily_transactions
                            {
                                isbilled = 1,
                                accountno = consumer.accountNo,
                                consumer_name = consumer.consumer_name,
                                address = consumer.address,
                                meterno = consumer.meterno,
                                consumer_type = consumer.consumer_type,
                                prevreading = consumer.prevreading,
                                presreading = consumer.presreading,
                                //usage = consumer.presreading - consumer.presreading,
                                usage = consumer.usage,
                                billdate = consumer.billdate,
                                billamount = consumer.total_bill,
                                remarks = consumer.remarks,
                                arrears = consumer.arrears,
                                other_charges = 0,
                                billmonth = consumer.billdate,
                                device_user = LoginUser.login_user.fullname,
                                seqno = consumer.seqno,
                                timestamp = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")

                            };
                            Database.Insert(_dailytrans);
                            dailyTrans.Add(_dailytrans);
                        }
                        catch (Exception ex1)
                        {
                            isSuccess = false;
                        }


                    }
                    ConsumerRecords.daily_Transactions = dailyTrans;
                }


            });


            return isSuccess;
        }


        public async Task<List<Printerconfig>> GetAllPrinterConfig()
        {
            List<Printerconfig> printer_items = new List<Printerconfig>();
            await Task.Run(() =>
            {
                InitializeDatabase();
                printer_items = Database.Table<Printerconfig>().ToList();

            });
            return printer_items;
        }

        public async Task<Printerconfig> GetDefaultPrinterConfig()
        {
            Printerconfig printer_item = new Printerconfig();
            await Task.Run(() =>
            {
                InitializeDatabase();
                printer_item = Database.Table<Printerconfig>().Where(p => p.isdefault == 1).FirstOrDefault();

            });
            return printer_item;
        }

        public async Task<bool> PrinterconfigSave(string configname, string bluetooth_address, string printerconfig, int linespacing, int isupdate, int printerID, int isdefault)
        {
            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();

                    Printerconfig printer_new = new Printerconfig
                    {
                        profile_name = configname,
                        bluetooth_address = bluetooth_address,
                        profileconfig = printerconfig,
                        linespacing = linespacing,
                        isdefault = isdefault
                    };

                    if (isupdate == 0)
                    {
                        Database.Insert(printer_new);
                    }
                    else
                    {
                        printer_new.id = printerID;
                        Database.Update(printer_new);
                    }


                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }

            });

            return isSuccess;
        }

        public async Task<bool> PrinterconfigDelete(int printerID)
        {
            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    InitializeDatabase();
                    // SQLite-net allows deleting by Primary Key (id)
                    Database.Delete<Printerconfig>(printerID);
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                }
            });

            return isSuccess;
        }


    }


}
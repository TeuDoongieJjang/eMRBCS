using eMRBCS.Global;
using eMRBCS.Models;
using SMBLibrary;
using SMBLibrary.Client;
using System.Net;

namespace MSRBC.Services
{
    internal class FileService
    {
        private string config_ipaddress = "";
        private string config_folder = "";
        private string config_username = "";
        private string config_password = "";

        public const string ImportSubFolder = "SOURCE";

        public const string ExportSubFolder = "OUTPUT";

        public string consumerFile = "";
        public string remarksFile = "";
        public string billrateFile = "";
        public string userfile = "";

        private sealed class SmbSession : IDisposable
        {
            public SMB2Client Client;
            public ISMBFileStore Store;
            public string BasePath = "";          // NEW: sub-folder path inside the share ("" = share root)

            public void Dispose()
            {
                try { Store?.Disconnect(); } catch { }
                try { Client?.Logoff(); } catch { }
                try { Client?.Disconnect(); } catch { }
            }
        }

        private static (string Share, string SubPath) SplitSharePath(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured)) return ("", "");

            string s = configured.Trim().Replace('/', '\\');
            bool isUnc = s.StartsWith("\\\\");          // \\server\share\sub...

            var parts = s.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (isUnc && parts.Count > 0) parts.RemoveAt(0);   // drop the server name
            if (parts.Count == 0) return ("", "");

            return (parts[0], string.Join("\\", parts.Skip(1)));
        }

        // NEW: joins the configured sub-path and a relative name
        private static string CombinePath(string basePath, string name)
        {
            name = (name ?? "").Replace('/', '\\').Trim('\\');
            if (string.IsNullOrEmpty(basePath)) return name;
            if (string.IsNullOrEmpty(name)) return basePath;
            return basePath + "\\" + name;
        }

        private SmbSession OpenSession(int isImport)
        {
            System.Diagnostics.Debug.WriteLine("Before GetServerConfig");
            var config = GetServerConfig(isImport);

            if (string.IsNullOrWhiteSpace(config.IP))
                throw new Exception("Server IP address / host name is not configured.");
            if (string.IsNullOrWhiteSpace(config.ShareName))
                throw new Exception("Shared folder name is not configured.");

            IPAddress ipAddr;
            if (!IPAddress.TryParse(config.IP, out ipAddr))
            {
                var addresses = Dns.GetHostAddresses(config.IP);
                if (addresses.Length == 0)
                    throw new Exception("Could not resolve the server IP or Hostname.");
                ipAddr = addresses[0];
            }

            System.Diagnostics.Debug.WriteLine("Before SMBConnect");
            var client = new SMB2Client();
            try
            {
                if (!client.Connect(ipAddr, SMBTransportType.DirectTCPTransport))
                    throw new Exception("Could not connect to the server on port 445.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Could not connect to the server on port 445. Underlying: {ex.Message}", ex);
            }

            System.Diagnostics.Debug.WriteLine("Before SMBLogin");
            NTStatus status = client.Login(String.Empty, config.Username, config.Password);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                client.Disconnect();
                throw new Exception($"Authentication failed. Status: {status}");
            }

            System.Diagnostics.Debug.WriteLine("Before SMBTreeConnect");
            ISMBFileStore store = client.TreeConnect(config.ShareName, out status);
            if (status != NTStatus.STATUS_SUCCESS)
            {
                client.Logoff();
                client.Disconnect();
                throw new Exception($"Could not access shared folder '{config.ShareName}'. Status: {status}");
            }

            return new SmbSession { Client = client, Store = store, BasePath = config.SubPath };
        }

        public async Task<string> TestConnectionAsync(int isImport)
        {
            string message = "success;Connected.";
            await Task.Run(() =>
            {
                try
                {
                    using var session = OpenSession(isImport);
                }
                catch (Exception ex)
                {
                    message = "failed;" + ex.Message;
                }
            });
            return message;
        }

        public async Task<(bool Success, string Message, List<string> Files)> ListFilesAsync(int isImport, string subFolder)
        {
            var files = new List<string>();
            string message = "";
            bool success = true;

            await Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("Before OpenSession");
                    using var session = OpenSession(isImport);
                    string listPath = CombinePath(session.BasePath, subFolder);

                    System.Diagnostics.Debug.WriteLine("Before CreaterFile");
                    object handle;
                    FileStatus fileStatus;
                    NTStatus status = session.Store.CreateFile(
                        out handle,
                        out fileStatus,
                        listPath,
                        AccessMask.GENERIC_READ,
                        SMBLibrary.FileAttributes.Directory,
                        ShareAccess.Read | ShareAccess.Write,
                        CreateDisposition.FILE_OPEN,
                        CreateOptions.FILE_DIRECTORY_FILE,
                        null);

                    if (status != NTStatus.STATUS_SUCCESS)
                        throw new Exception($"Could not open folder '{listPath}'. Status: {status}");

                    System.Diagnostics.Debug.WriteLine("Before QueryDirectoty");
                    List<QueryDirectoryFileInformation> list;
                    status = session.Store.QueryDirectory(out list, handle, "*", FileInformationClass.FileDirectoryInformation);
                    session.Store.CloseFile(handle);

                    if (list == null && status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                        throw new Exception($"Could not list folder '{subFolder}'. Status: {status}");

                    if (list != null)
                    {
                        foreach (var entry in list)
                        {
                            if (entry is FileDirectoryInformation info)
                            {
                                bool isFolder = (info.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;
                                if (!isFolder && info.FileName != "." && info.FileName != "..")
                                    files.Add(info.FileName);
                            }
                        }
                    }

                    files.Sort(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    success = false;
                    message = ex.Message;
                }
            });

            return (success, message, files);
        }

        public async Task<string> DownloadFileAsync(int isImport, string remotePath, string localPath)
        {
            string message = "success;File downloaded.";

            await Task.Run(() =>
            {
                try
                {
                    using var session = OpenSession(isImport);
                    string fullRemote = CombinePath(session.BasePath, remotePath);
                    object handle;
                    FileStatus fileStatus;
                    NTStatus status = session.Store.CreateFile(
                        out handle,
                        out fileStatus,
                        fullRemote,
                        AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                        SMBLibrary.FileAttributes.Normal,
                        ShareAccess.Read,
                        CreateDisposition.FILE_OPEN,
                        CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                        null);

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        message = $"failed;File '{fullRemote}' not found or access denied. Status: {status}";
                        return;
                    }

                    try
                    {
                        using (var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                        {
                            long offset = 0;
                            int chunk = (int)session.Client.MaxReadSize;

                            while (true)
                            {
                                byte[] data;
                                status = session.Store.ReadFile(out data, handle, offset, chunk);

                                if (status == NTStatus.STATUS_END_OF_FILE)
                                    break;
                                if (status != NTStatus.STATUS_SUCCESS)
                                    throw new Exception($"Error reading file. Status: {status}");
                                if (data == null || data.Length == 0)
                                    break;

                                fs.Write(data, 0, data.Length);
                                offset += data.Length;
                            }
                        }
                    }
                    finally
                    {
                        session.Store.CloseFile(handle);
                    }
                }
                catch (Exception ex)
                {
                    message = "failed;" + ex.Message;
                    try { if (System.IO.File.Exists(localPath)) System.IO.File.Delete(localPath); } catch { }
                }
            });

            return message;
        }

        public async Task<string> UploadFileAsync(int isImport, string localPath, string remotePath)
        {
            string message = "success;File uploaded.";

            await Task.Run(() =>
            {
                try
                {
                    if (!System.IO.File.Exists(localPath))
                    {
                        message = "failed;Local file not found.";
                        return;
                    }

                    using var session = OpenSession(isImport);
                    string fullRemote = CombinePath(session.BasePath, remotePath);
                    object handle;
                    FileStatus fileStatus;
                    NTStatus status = session.Store.CreateFile(
                        out handle,
                        out fileStatus,
                        fullRemote,
                        AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                        SMBLibrary.FileAttributes.Normal,
                        ShareAccess.None,
                        CreateDisposition.FILE_OVERWRITE_IF,
                        CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                        null);

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        message = $"failed;Could not create file '{fullRemote}' on the server. Status: {status}";
                        return;
                    }

                    try
                    {
                        using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read))
                        {
                            int maxWrite = (int)session.Client.MaxWriteSize;
                            byte[] buffer = new byte[maxWrite];
                            long writeOffset = 0;
                            int bytesRead;

                            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                byte[] dataToWrite = buffer;
                                if (bytesRead < maxWrite)
                                {
                                    dataToWrite = new byte[bytesRead];
                                    Array.Copy(buffer, dataToWrite, bytesRead);
                                }

                                int written;
                                status = session.Store.WriteFile(out written, handle, writeOffset, dataToWrite);
                                if (status != NTStatus.STATUS_SUCCESS)
                                    throw new Exception($"Error writing chunk to server. Status: {status}");

                                writeOffset += bytesRead;
                            }
                        }
                    }
                    finally
                    {
                        session.Store.CloseFile(handle);
                    }
                }
                catch (Exception ex)
                {
                    message = "failed;" + ex.Message;
                }
            });

            return message;
        }

        private (string IP, string ShareName, string SubPath, string Username, string Password) GetServerConfig(int isImport)   // CHANGED
        {
            string rawFolder;
            if (isImport == 1)
            {
                config_ipaddress = Serverdetails.ImportServerIPAddress;
                rawFolder = Serverdetails.ImportSharedFolder;
                config_username = Serverdetails.ImportServerUserName;
                config_password = Serverdetails.ImportServerPassword;
            }
            else
            {
                config_ipaddress = Serverdetails.ExportServerIPAddress;
                rawFolder = Serverdetails.ExportSharedFolder;
                config_username = Serverdetails.ExportServerUserName;
                config_password = Serverdetails.ExportServerPassword;
            }

            var (share, sub) = SplitSharePath(rawFolder);   // NEW
            config_folder = share;

            return (config_ipaddress, share, sub, config_username, config_password);
        }

        public async Task<string[]> ImportAllFilesAsync()
        {
            string[] results = new string[4];

            await Task.Run(() =>
            {
                SMB2Client client = null;
                ISMBFileStore fileStore = null;
                bool loggedIn = false;

                try
                {
                    string datePart = DateTime.Now.ToString(FilePrefix.configPrefix);

                    string[] remoteNames = new string[4];
                    remoteNames[0] = LoginUser.login_user.username + "_" + datePart + FilePrefix.ConsumerFile;
                    remoteNames[1] = FilePrefix.RemarksFile;
                    remoteNames[2] = FilePrefix.BillratesFile;
                    remoteNames[3] = FilePrefix.UsersFile;

                    string[] localPaths = new string[4];
                    for (int i = 0; i < 4; i++)
                        localPaths[i] = Path.Combine(FileSystem.AppDataDirectory, remoteNames[i]);

                    consumerFile = localPaths[0];
                    remarksFile = localPaths[1];
                    billrateFile = localPaths[2];
                    userfile = localPaths[3];

                    using var session = OpenSession(1);



                    // Download the 4 files
                    int maxRead = (int)session.Client.MaxReadSize;
                    for (int i = 0; i < 4; i++)
                    {
                        try
                        {
                            results[i] = DownloadOne(session.Store, session.BasePath, maxRead, remoteNames[i], localPaths[i]);
                        }
                        catch (Exception exFile)
                        {
                            results[i] = "failed;Error downloading '" + remoteNames[i] + "'. " + exFile.Message;
                        }
                    }
                }
                catch (Exception ex)
                {
                    for (int i = 0; i < 4; i++)
                        if (results[i] == null)
                            results[i] = "failed;An error occurred during file transfer. " + ex.Message;
                }
                finally
                {
                    try { fileStore?.Disconnect(); } catch { }
                    try { if (loggedIn) client?.Logoff(); } catch { }
                    try { client?.Disconnect(); } catch { }
                }
            });

            return results;
        }



        private string DownloadOne(ISMBFileStore fileStore, string basePath, int maxRead, string remoteName, string localFile)
        {
            string remotePath = CombinePath(basePath, remoteName);

            object fileHandle;
            FileStatus fileStatus;
            NTStatus status = fileStore.CreateFile(
                out fileHandle,
                out fileStatus,
                remotePath,
                AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                SMBLibrary.FileAttributes.Normal,
                ShareAccess.Read,
                CreateDisposition.FILE_OPEN,
                CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                null);

            if (status != NTStatus.STATUS_SUCCESS)
                return $"failed;File '{remotePath}' not found or access denied. Status: {status}";

            string tmpFile = localFile + ".tmp";
            try
            {
                using (FileStream fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write))
                {
                    long offset = 0;
                    while (true)
                    {
                        byte[] data;
                        status = fileStore.ReadFile(out data, fileHandle, offset, maxRead);

                        if (status == NTStatus.STATUS_END_OF_FILE) break;
                        if (status != NTStatus.STATUS_SUCCESS)
                            return $"failed;Read error on '{remotePath}'. Status: {status}";
                        if (data == null || data.Length == 0) break;

                        fs.Write(data, 0, data.Length);
                        offset += data.Length;
                    }
                }

                File.Move(tmpFile, localFile, true);   // overwrite the old local copy
                return "success;no error";
            }
            finally
            {
                fileStore.CloseFile(fileHandle);
                try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
            }
        }


        public async Task<string> ImportFileAsync(int dataFileType)
        {

            string return_message = "success;no error";

            await Task.Run(() =>
            {
                string targetfile = DateTime.Now.ToString(FilePrefix.configPrefix);

                switch (dataFileType)
                {
                    case 0: //Consumer
                        targetfile = LoginUser.login_user.username + "_" + targetfile + FilePrefix.ConsumerFile;
                        consumerFile = Path.Combine(FileSystem.AppDataDirectory, targetfile);
                        break;
                    case 1: //Remarks
                        targetfile = FilePrefix.RemarksFile;
                        remarksFile = Path.Combine(FileSystem.AppDataDirectory, targetfile);
                        break;
                    case 2: //Billing rates
                        targetfile = FilePrefix.BillratesFile;
                        billrateFile = Path.Combine(FileSystem.AppDataDirectory, targetfile);
                        break;
                    case 3: //User
                        targetfile = FilePrefix.UsersFile;
                        userfile = Path.Combine(FileSystem.AppDataDirectory, targetfile);
                        break;
                }

                string localfile = Path.Combine(FileSystem.AppDataDirectory, targetfile);


                try
                {
                    using var session = OpenSession(1);


                    targetfile = CombinePath(session.BasePath, targetfile);
                    config_folder = targetfile;

                    // 6. Open the File
                    object fileHandle;
                    FileStatus fileStatus;

                    NTStatus status = session.Store.CreateFile(
                        out fileHandle,
                        out fileStatus,
                        targetfile,
                        AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
                        SMBLibrary.FileAttributes.Normal, // <-- Add SMBLibrary. prefix here
                        ShareAccess.Read,
                        CreateDisposition.FILE_OPEN,
                        CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                        null);

                    if (status == NTStatus.STATUS_SUCCESS)
                    {
                        // 7. Read the File
                        using (FileStream fs = new FileStream(localfile, FileMode.Create, FileAccess.Write))
                        {
                            long bytesReadOffset = 0;
                            byte[] data;

                            while (true)
                            {
                                status = session.Store.ReadFile(out data, fileHandle, bytesReadOffset, (int)session.Client.MaxReadSize);

                                if (status != NTStatus.STATUS_SUCCESS || data == null || data.Length == 0)
                                {
                                    break;
                                }

                                fs.Write(data, 0, data.Length);
                                bytesReadOffset += data.Length;
                            }
                        }
                        session.Store.CloseFile(fileHandle);
                    }
                    else
                    {
                        return_message = $"failed;File '{targetfile}' not found or access denied. Status: {status}";
                    }

                    // 8. Clean up Connection
                    session.Store.Disconnect();
                    session.Client.Logoff();
                    session.Client.Disconnect();
                }
                catch (Exception ex)
                {
                    return_message = "failed;An error occurred during file transfer. " + ex.Message;
                }

            });


            return return_message;


        }



        public async Task<bool> ExportFileAsync()
        {
            bool isSuccess = true;

            await Task.Run(() =>
            {
                try
                {
                    string targetfile = DateTime.Now.ToString(FilePrefix.configPrefix);
                    string filereport = LoginUser.login_user.username + "_" + targetfile + FilePrefix.DailyTransactionFile;
                    string localFilePath = Path.Combine(FileSystem.AppDataDirectory, filereport);
                    using var session = OpenSession(0);
                    // 1. Get Configuration (using the GetServerConfig method we created earlier)
                    string remotePath = CombinePath(session.BasePath, filereport);

                    // 6. Create or Overwrite the Remote File
                    object fileHandle;
                    FileStatus fileStatus;
                    System.Diagnostics.Debug.WriteLine("Before SMB CREATEFILE");
                    NTStatus status = session.Store.CreateFile(
                        out fileHandle,
                        out fileStatus,
                        remotePath,
                        AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
                        SMBLibrary.FileAttributes.Normal,
                        ShareAccess.None,
                        CreateDisposition.FILE_OVERWRITE_IF,
                        CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
                        null);

                    if (status != NTStatus.STATUS_SUCCESS)
                    {
                        throw new Exception($"Could not create file '{filereport}' on the server. Status: {status}");
                    }

                    // 7. Read Local File and Upload in Chunks
                    using (FileStream fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read))
                    {
                        int maxWriteSize = (int)session.Client.MaxWriteSize;
                        byte[] buffer = new byte[maxWriteSize];
                        int bytesRead;
                        long writeOffset = 0;

                        while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            byte[] dataToWrite = buffer;

                            // Shrink the array for the final chunk if it is smaller than the buffer
                            if (bytesRead < maxWriteSize)
                            {
                                dataToWrite = new byte[bytesRead];
                                Array.Copy(buffer, dataToWrite, bytesRead);
                            }

                            int numberOfBytesWritten;
                            status = session.Store.WriteFile(out numberOfBytesWritten, fileHandle, writeOffset, dataToWrite);

                            if (status != NTStatus.STATUS_SUCCESS)
                            {
                                throw new Exception($"Error writing chunk to server. Status: {status}");
                            }

                            writeOffset += bytesRead;
                        }
                    }

                    // 8. Clean up Connection
                    session.Store.CloseFile(fileHandle);
                    session.Store.Disconnect();
                    session.Client.Logoff();
                    session.Client.Disconnect();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("=== SMB EXCEPTION EXPOR FILE ASYNC ===");
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    System.Diagnostics.Debug.WriteLine("=====================");
                    ConsumerRecords.ErrorSendData = "Sending File. " + ex.Message;
                    isSuccess = false;
                }
            });

            return isSuccess;
        }
        public async Task<bool> WriteDailyTrans()
        {
            bool isSuccess = true;
            await Task.Run(() =>
            {
                try
                {
                    string targetfile = DateTime.Now.ToString(FilePrefix.configPrefix);
                    string filereport = LoginUser.login_user.username + "_" + targetfile + FilePrefix.DailyTransactionFile;
                    string localfile = Path.Combine(FileSystem.AppDataDirectory, filereport);
                    var transactions = ConsumerRecords.daily_Transactions;
                    if (transactions == null || transactions.Count == 0)
                    {
                        throw new Exception("There are no billed transactions to write.");
                    }

                    using (StreamWriter writer = new StreamWriter(localfile, false))
                    {
                        // Write data line by line
                        string linetrans = "";
                        foreach (Daily_transactions dailytrans in transactions)
                        {

                            linetrans = dailytrans.isbilled.ToString() + "~" + dailytrans.accountno + "~" + dailytrans.consumer_name + "~" + dailytrans.address + "~" + dailytrans.meterno + "~" + dailytrans.consumer_type.ToString()
                                      + "~" + dailytrans.prevreading.ToString() + "~" + dailytrans.presreading.ToString() + "~" + dailytrans.usage.ToString() + "~" + dailytrans.billdate + "~" + dailytrans.billamount.ToString()
                                      + "~" + dailytrans.remarks + "~" + dailytrans.arrears.ToString() + "~" + dailytrans.other_charges.ToString() + "~" + dailytrans.billmonth + "~" + dailytrans.penaltyamt.ToString()
                                      + "~" + dailytrans.duedate + "~" + dailytrans.device_user + "~" + dailytrans.surcharge.ToString() + dailytrans.pca_amount.ToString() + "~" + dailytrans.ornumber + "~" + dailytrans.collection_date
                                      + "~" + dailytrans.amount_collected.ToString() + "~" + dailytrans.number_of_print.ToString() + "~" + dailytrans.seqno.ToString() + "~" + dailytrans.remarks_explanation
                                      + "~" + dailytrans.trx_start + "~" + dailytrans.trx_end + "~" + dailytrans.timestamp;
                            writer.WriteLine(linetrans);
                        }



                    }

                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("=== SMB EXCEPTION WRITE DAILY TRANS ===");
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    System.Diagnostics.Debug.WriteLine("=====================");
                    ConsumerRecords.ErrorSendData = "Writing to File. " + ex.Message;
                    isSuccess = false;
                }
            });
            return isSuccess;
        }

        public async Task<bool> CheckSMBStatus()
        {
            bool isSuccess = true;

            await Task.Run(() =>
            {
                try
                {
                    using var session = OpenSession(1);
                    // 1. Get Configuration (1 = Import server settings)


                    // If execution reaches here, the connection and folder access are completely successful.

                    // 6. Clean up Connection
                    session.Store.Disconnect();
                    session.Client.Logoff();
                    session.Client.Disconnect();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("=== SMB CONNECTION CHECK SMB STATUS ===");
                    System.Diagnostics.Debug.WriteLine(ex.ToString());
                    System.Diagnostics.Debug.WriteLine("==================================");
                    isSuccess = false;
                }
            });

            return isSuccess;
        }

    }


}

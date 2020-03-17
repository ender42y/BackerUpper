using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Backer_Upper.Models
{
    class Crawler
    {
        private string RootSource;
        private string RootTarget;
        private int Mode;// 0 = replace only changed, copy everything new. 1 = replace all. 2 = replace none. 

        private int FileCount;

        private List<string> ErrorFiles;
        private List<string> PermissionFiles;

        private List<string> AllFiles;

        private double FileTotals;
        private double BytesCopied;
        private int FilesCopied;

        private bool Finished;
        private bool Canceled;

        //lock for threaded access
        private readonly object ListLock = new object();
        private readonly object IOLock = new object();
        private readonly object ErrorLock = new object();


        public Crawler(string source, string target, int mode)
        {
            Canceled = false;

            RootSource = source;
            RootTarget = target;

            CheckRootEndings();

            Mode = mode;
            FileCount = 0;

            AllFiles = new List<string>();

            ErrorFiles = new List<string>();
            PermissionFiles = new List<string>();

            FileTotals = 0;
            BytesCopied = 0;

            FilesCopied = 0;

        }

        private void CheckRootEndings()
        {
            if(!RootSource.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                RootSource += Path.DirectorySeparatorChar.ToString();
            }
            if (!RootTarget.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                RootTarget += Path.DirectorySeparatorChar.ToString();
            }
        }

        #region Getters and Setters

        public double GetFileSizeTotal()
        {
            double returnValue;
            lock (IOLock)
            {
                returnValue = FileTotals;
            }

            return returnValue;
        }

        public double GetBytesCopied()
        {
            double returnValue;
            lock (IOLock)
            {
                returnValue = BytesCopied;
            }

            return returnValue;
        }

        public int GetFileCount()
        {
            int returnValue;
            lock (IOLock)
            {
                returnValue = FileCount;
            }

            return returnValue;
        }

        public int GetFilesCopied()
        {
            int returnValue;
            lock (IOLock)
            {
                returnValue = FilesCopied;
            }

            return returnValue;
        }

        public void SetCanceled(bool cancel)
        {
            lock (IOLock)
            {
                Canceled = cancel;
            }
        }

        public int GetErrorCount()
        {
            int count = 0;
            lock(ErrorLock)
            {
                count = ErrorFiles.Count();
            }

            return count;
        }

        public int GetPermissionCount()
        {
            int count = 0;
            lock (ErrorLock)
            {
                count = PermissionFiles.Count();
            }

            return count;
        }


        public List<string> GetErrorFiles()
        {
            List<string> toReturn;
            lock (ErrorLock)
            {
                toReturn = ErrorFiles;
            }

            return toReturn;
        }

        public List<string> GetPermissionFiles()
        {
            List<string> toReturn;
            lock (ErrorLock)
            {
                toReturn = PermissionFiles;
            }

            return toReturn;
        }


        #endregion

        public void RunCopy()
        {
            if (RootSource == null || RootSource == "" || RootTarget == null || RootTarget == "")
            {
                return;
            }

            int count = 0;


            while (!Finished  || count != 0)
            {
                if (CheckCanceled())
                {
                    return;
                }
                string path = "";
                lock (ListLock)
                {
                    if (AllFiles.Count > 0)
                    {
                        path = AllFiles[0];
                        AllFiles.RemoveAt(0);
                    }
                }

                if (path != "")
                {
                    try
                    {
                        string newTargetFilePath = path.Replace(RootSource, RootTarget);

                        File.Copy(path, newTargetFilePath);

                        lock (IOLock)
                        {
                            ++FilesCopied;
                            BytesCopied += new FileInfo(path).Length;
                        }
                    }

                    catch (IOException)
                    {
                        lock (ErrorLock)
                        {
                            ErrorFiles.Add(path);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        lock (ErrorLock)
                        {
                            PermissionFiles.Add(path);
                        }
                    }
                    catch (Exception)
                    {
                        lock (ErrorLock)
                        {
                            ErrorFiles.Add(path + " (Unknown Error)");
                        }
                    }
                }
                else
                {
                    System.Threading.Thread.Sleep(100);
                }

                lock (ListLock)
                {
                    count = AllFiles.Count();
                }
            }
        }

        public void StartCrawl(string source)
        {
            lock (IOLock)
            {
                AllFiles = new List<string>();
                ErrorFiles = new List<string>();
                PermissionFiles = new List<string>();

                FileCount = 0;
                FileTotals = 0;
                BytesCopied = 0;
                FilesCopied = 0;
            }
            Finished = false;
            Crawl(source);
            Finished = true;
        }

        public void Crawl(string source)
        {
            if(CheckCanceled())
            {
                return;
            }

            if (RootSource == null || RootSource == "" || RootTarget == null || RootTarget == "")
            {
                return;
            }

            string[] dirs = Directory.GetDirectories(source);


            for (int i = 0; i < dirs.Length; ++i)
            {
                try
                {
                    string newTargetDir = dirs[i].Replace(RootSource, RootTarget);
                    if (!Directory.Exists(newTargetDir))
                    {
                        Directory.CreateDirectory(newTargetDir);
                    }

                    Crawl(dirs[i]);
                    if (CheckCanceled())
                    {
                        return;
                    }
                }
                catch (IOException)
                {
                    lock (ErrorLock)
                    {
                        ErrorFiles.Add(dirs[i]);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    lock (ErrorLock)
                    {
                        PermissionFiles.Add(dirs[i]);
                    }
                }
                catch (Exception)
                {
                    lock (ErrorLock)
                    {
                        ErrorFiles.Add(dirs[i] + " (Unknown Error)");
                    }
                }
            }

            string[] files = Directory.GetFiles(source);

            for (int i = 0; i < files.Length; ++i)
            {
                string newTargetFilePath = files[i].Replace(RootSource, RootTarget);

                try
                {
                    FileInfo temp = new FileInfo(files[i]);

                    switch (Mode)
                    {
                        case 0:
                            if (!File.Exists(newTargetFilePath))
                            {
                                lock (ListLock)
                                {
                                    AllFiles.Add(files[i]);
                                }
                                lock (IOLock)
                                {
                                    FileTotals += new FileInfo(files[i]).Length;
                                    FileCount++;
                                }
                            }
                            else
                            {
                                if (File.GetLastWriteTime(files[i]) > File.GetLastWriteTime(newTargetFilePath))
                                {
                                    lock (ListLock)
                                    {
                                        AllFiles.Add(files[i]);
                                    }
                                    lock (IOLock)
                                    {
                                        FileTotals += new FileInfo(files[i]).Length;
                                        FileCount++;
                                    }
                                }
                            }
                            break;


                        case 1:
                            if (File.Exists(newTargetFilePath))
                            {
                                File.Delete(newTargetFilePath);
                            }
                                lock (ListLock)
                            {
                                AllFiles.Add(files[i]);
                            }
                            lock (IOLock)
                            {
                                FileTotals += new FileInfo(files[i]).Length;
                                FileCount++;
                            }
                            break;


                        case 2:
                            if (!File.Exists(newTargetFilePath))
                            {
                                lock (ListLock)
                                {
                                    AllFiles.Add(files[i]);
                                }
                                lock (IOLock)
                                {
                                    FileTotals += new FileInfo(files[i]).Length;
                                    FileCount++;
                                }
                            }
                            break;
                    }
                }
                catch (IOException)
                {
                    lock (ErrorLock)
                    {
                        ErrorFiles.Add(files[i]);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    lock (ErrorLock)
                    {
                        PermissionFiles.Add(files[i]);
                    }
                }
                catch (Exception)
                {
                    lock (ErrorLock)
                    {
                        ErrorFiles.Add(files[i] + " (Unknown Error)");
                    }
                }

            }

        }

        private bool CheckCanceled()
        {
            bool canceled;

            lock (IOLock)
            {
                canceled = Canceled;
            }
            return canceled;
        }
    }
}

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

        private List<string> ErrorFileNames;

        private List<CopyFile> AllFiles;

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

            AllFiles = new List<CopyFile>();

            ErrorFileNames = new List<string>();

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
                count = ErrorFileNames.Count();
            }

            return count;
        }

        public List<string> GetErrorFiles()
        {
            List<string> toReturn;
            lock (ErrorLock)
            {
                toReturn = ErrorFileNames;
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

            long fileSize = 0;

            while (!Finished  || count != 0)
            {
                if (CheckCanceled())
                {
                    return;
                }
                CopyFile file = new CopyFile();
                lock (ListLock)
                {
                    if (AllFiles.Count > 0)
                    {
                        file = AllFiles[0];
                        AllFiles.RemoveAt(0);
                    }
                }

                if (file != null && file.Name != "")
                {
                    try
                    {
                        string newTargetFilePath = file.Name.Replace(RootSource, RootTarget);
                        

                        File.Copy(file.Name, newTargetFilePath);

                        lock (IOLock)
                        {
                            ++FilesCopied;
                            BytesCopied += file.Size;
                        }
                    }
                    catch (Exception)
                    {
                        lock (ErrorLock)
                        {
                            ErrorFileNames.Add(file.Name);
                            BytesCopied += file.Size;
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
                AllFiles = new List<CopyFile>();
                ErrorFileNames = new List<string>();

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

            if (RootSource == null || RootSource == "" || RootTarget == null || RootTarget == "" || source.Contains("$RECYCLE.BIN"))//Ignore recycle bin, not worth copying
            {
                return;
            }

            string[] dirs = Directory.GetDirectories(source);


            for (int i = 0; i < dirs.Length; ++i)
            {
                try
                {
                    //Create Directories that do not exist
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
                catch (Exception)
                {
                    lock (ErrorLock)
                    {
                        ErrorFileNames.Add(dirs[i]);
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
                                    AllFiles.Add(new CopyFile(files[i], new FileInfo(files[i]).Length));
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
                                        AllFiles.Add(new CopyFile(files[i], new FileInfo(files[i]).Length));
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
                                AllFiles.Add(new CopyFile(files[i], new FileInfo(files[i]).Length));
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
                                    AllFiles.Add(new CopyFile(files[i], new FileInfo(files[i]).Length));
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
                catch (Exception)
                {
                    lock (ErrorLock)
                    {
                        ErrorFileNames.Add(files[i]);
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

using Backer_Upper.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Timers;
using System.Diagnostics;

namespace Backer_Upper.Views
{
    /// <summary>
    /// Interaction logic for MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        Crawler crawler;
        string SourceDir;
        string TargetDir;
        string Path;
        string Folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Backer_Upper_Reports");
        string ReportFile = System.IO.Path.DirectorySeparatorChar + "ErrorReport.txt";
        Stopwatch stopwatch;

        public MainPage()
        {
            Path = Folder + ReportFile;
            stopwatch = new Stopwatch();
            InitializeComponent();
        }

        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            //check all inputs are selected and folders exist

            int mode = 0;

            SourceDir = SourcePicker.PathTextBox.Text;
            TargetDir = TargetPicker.PathTextBox.Text;

            if (!Directory.Exists(SourceDir))
            {
                MessageBox.Show("Could not find source folder, please double check the path you selected and try again.", "Error");
                return;
            }
            if (!Directory.Exists(TargetDir))
            {
                MessageBox.Show("Could not find target folder, please double check the path you selected and try again.", "Error");
                return;
            }

            //get mode from radio buttons
            if (UpdateBtn.IsChecked == true)
            {
                mode = 0;
            }
            else if (ReplaceBtn.IsChecked == true)
            {
                mode = 1;
            }
            else
            {
                mode = 2;
            }

            //instantiate crawler and progressBar
            crawler = new Crawler(SourceDir, TargetDir, mode);

            //start crawler
            Task crawlerTask = Task.Run(() =>
            {
                KickOffCralwer();
            });

            Task copyTask = Task.Run(() =>
            {
                KickOffCopy();
            });
        }

        private void KickOffCralwer()
        {
            crawler.StartCrawl(SourceDir);
        }

        private void KickOffCopy()
        {
            Timer timer = new Timer();
            stopwatch.Start();
            timer.Interval = 250;
            timer.Elapsed += TimerTicked;

            this.Dispatcher.Invoke(() =>    //sets button to cancel
            {
                StartBtn.Content = "Cancel";
                StartBtn.Click += CancelCrawl;
                StartBtn.Click -= StartBtn_Click;
            });

            timer.Start();
            crawler.RunCopy();
            timer.Stop();
            stopwatch.Stop();
            stopwatch.Reset();

            this.Dispatcher.Invoke(() =>    //sets button to start
            {
                StartBtn.Content = "Start";
                StartBtn.Click += StartBtn_Click;
                StartBtn.Click -= CancelCrawl;
                StartBtn.IsEnabled = true;
                ProgressLabel.Text = "Finished";
            });

            int errorCount = crawler.GetErrorCount();
            int permissionCount = crawler.GetPermissionCount();
            int count = errorCount + permissionCount;

            if (count > 0)
            {
                WriteToErrorFile();
                MessageBoxResult res = MessageBox.Show("There was an error copying " + count + " files. Would you like to view a list of the affected files?", "View Files?", MessageBoxButton.YesNo);
                if (res == MessageBoxResult.Yes)
                {
                    Process.Start("notepad.exe", Path);
                }
            }
        }

        private void WriteToErrorFile()
        {
            try
            {
                if(!Directory.Exists(Folder))
                {
                    Directory.CreateDirectory(Folder);
                }
                StreamWriter writer = File.CreateText(Path);
                //set up headers

                //write standard io errors
                List<string> errorFiles = crawler.GetErrorFiles();
                foreach(string s in errorFiles)
                {
                    writer.WriteLine(s);
                }

                //write permission errors
                List<string> permissionFiles = crawler.GetPermissionFiles();
                foreach (string s in permissionFiles)
                {
                    writer.WriteLine(s);
                }

                writer.Close();


            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

        }

        private void CancelCrawl(Object source, RoutedEventArgs e)
        {
            StartBtn.Content = "Stopping";
            StartBtn.IsEnabled = false;
            crawler.SetCanceled(true);
        }

        private void TimerTicked(Object source, System.Timers.ElapsedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>    //sets value of progress bar
            {
                double totalBytes = crawler.GetFileSizeTotal();
                double bytesCopied = crawler.GetBytesCopied();
                double percent = bytesCopied / totalBytes;
                int totalFiles = crawler.GetFileCount();
                int filesCopied = crawler.GetFilesCopied() + crawler.GetErrorCount() + crawler.GetPermissionCount();
                string units = "";

                if(filesCopied > totalFiles)
                {
                    Console.WriteLine("Too many Files");
                }

                //format bytes into appropriate prefix block
                double formattedBytes;

                if(totalBytes <= 1024)
                {
                    units = "bytes";
                    formattedBytes = totalBytes;
                }
                else if(totalBytes <= 1048576 && totalBytes > 1024)
                {
                    units = "KB";
                    formattedBytes = totalBytes / 1024;
                    bytesCopied /= 1024;
                }
                else if(totalBytes <= 1073741824 && totalBytes > 1048576)
                {
                    units = "MB";
                    formattedBytes = totalBytes / 1048576;
                    bytesCopied /= 1048576;
                }
                else
                {
                    units = "GB";
                    formattedBytes = totalBytes / 1073741824;
                    bytesCopied /= 1073741824;
                }


                //get data out of stopwatch
                TimeSpan ts = stopwatch.Elapsed;
                TimeSpan timeToGo;
                string finishTime = "";
                if(percent != 0 &&  !Double.IsNaN(percent))
                {
                    timeToGo = new TimeSpan(Convert.ToInt64(ts.Ticks/percent));
                    finishTime = String.Format("{0:00}:{1:00}:{2:00}", timeToGo.Hours, timeToGo.Minutes, timeToGo.Seconds);
                }
                else
                    finishTime = "(Working on Estimation)";


                RunTimeLbl.Text = "Elapsed Time: " + String.Format("{0:00}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds)+ System.Environment.NewLine
                                    +"Estimated Finish Time: " + finishTime;
                ProgressLabel.Text = filesCopied + " Out Of " + totalFiles + " Copied" + System.Environment.NewLine
                                    + String.Format("{0:0.##}", bytesCopied) + " " + units + "/" + String.Format("{0:0.##}", formattedBytes) + " " + units;
            });
        }
    }
}

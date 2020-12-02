using System;
using System.Collections.Generic;
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

using System.Windows.Forms;

namespace Backer_Upper.Views
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class FilePicker : System.Windows.Controls.UserControl
    {

        //private Microsoft.Win32.OpenFileDialog FileDialog;
        private FolderBrowserDialog FolderBrowser;

        public FilePicker()
        {
            //FileDialog = new Microsoft.Win32.OpenFileDialog();

            FolderBrowser = new FolderBrowserDialog();
            
            InitializeComponent();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowser.ShowNewFolderButton = true;
            FolderBrowser.SelectedPath = "C:/";
            DialogResult dialogResult = FolderBrowser.ShowDialog();

            if(dialogResult == DialogResult.OK)
            {
                PathTextBox.Text = FolderBrowser.SelectedPath;
            }

        }
    }
}

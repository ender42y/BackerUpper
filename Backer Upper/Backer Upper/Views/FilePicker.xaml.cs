using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Backer_Upper.Views;

/// <summary>A folder path box with a Browse button. Also accepts a folder dragged from Explorer.</summary>
public partial class FilePicker : UserControl
{
    public static readonly DependencyProperty SelectedPathProperty = DependencyProperty.Register(
        nameof(SelectedPath), typeof(string), typeof(FilePicker),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public FilePicker()
    {
        InitializeComponent();
    }

    public string SelectedPath
    {
        get => (string)GetValue(SelectedPathProperty);
        set => SetValue(SelectedPathProperty, value);
    }

    /// <summary>Title shown on the folder browser dialog.</summary>
    public string DialogTitle { get; set; } = "Select a folder";

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = DialogTitle };
        if (Directory.Exists(SelectedPath))
            dialog.InitialDirectory = SelectedPath;

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            SelectedPath = dialog.FolderName;
    }

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths && Directory.Exists(paths[0]) ? paths[0] : null;

    private void PathTextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFolder(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void PathTextBox_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DroppedFolder(e) is string folder)
            SelectedPath = folder;
        e.Handled = true;
    }
}

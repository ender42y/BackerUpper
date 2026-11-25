using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Shell;
using BackerUpper.Core;

namespace Backer_Upper;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private CancellationTokenSource? _cancellation;
    private bool _closeWhenStopped;
    private string? _reportPath;

    public MainWindow()
    {
        InitializeComponent();
        SourcePicker.SelectedPath = _settings.SourcePath;
        TargetPicker.SelectedPath = _settings.TargetPath;
        SelectedMode = _settings.Mode;
        StatusText.Text = "Choose a folder to back up and where to put it, then press Start backup.";
    }

    private bool IsRunning => _cancellation is not null;

    private BackupMode SelectedMode
    {
        get => ReplaceBtn.IsChecked == true ? BackupMode.ReplaceAll
             : NewBtn.IsChecked == true ? BackupMode.NewOnly
             : BackupMode.UpdateChanged;
        set
        {
            UpdateBtn.IsChecked = value == BackupMode.UpdateChanged;
            ReplaceBtn.IsChecked = value == BackupMode.ReplaceAll;
            NewBtn.IsChecked = value == BackupMode.NewOnly;
        }
    }

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IsRunning)
        {
            RequestStop();
            return;
        }

        string source = SourcePicker.SelectedPath.Trim();
        string target = TargetPicker.SelectedPath.Trim();
        if (BackupEngine.Validate(source, target) is string problem)
        {
            MessageBox.Show(this, problem, "Can't start backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.SourcePath = source;
        _settings.TargetPath = target;
        _settings.Mode = SelectedMode;
        _settings.Save();

        var engine = new BackupEngine(source, target, SelectedMode);
        _cancellation = new CancellationTokenSource();
        SetRunning(true);

        BackupResult? result = null;
        try
        {
            result = await engine.RunAsync(new Progress<BackupProgress>(ShowProgress), _cancellation.Token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The backup stopped unexpectedly:\n\n" + ex.Message, "Backup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetRunning(false);
        }

        if (_closeWhenStopped)
        {
            Close();
            return;
        }
        if (result is not null)
            ShowResult(engine, result);
    }

    private void RequestStop()
    {
        _cancellation?.Cancel();
        StartBtn.IsEnabled = false;
        StartBtn.Content = "Stopping…";
        StatusText.Text = "Stopping after the current file…";
    }

    private void SetRunning(bool running)
    {
        SourcePicker.IsEnabled = TargetPicker.IsEnabled = ModeGroup.IsEnabled = !running;
        StartBtn.IsEnabled = true;
        StartBtn.Content = running ? "Cancel" : "Start backup";
        if (running)
        {
            _reportPath = null;
            ReportBtn.Visibility = Visibility.Collapsed;
            Progress.Value = 0;
            DetailText.Text = "";
        }
        Progress.IsIndeterminate = false;
        Taskbar.ProgressState = running ? TaskbarItemProgressState.Indeterminate : TaskbarItemProgressState.None;
    }

    private void ShowProgress(BackupProgress p)
    {
        if (!IsRunning || p.Phase == BackupPhase.Finished)
            return;

        if (p.Phase == BackupPhase.Scanning)
        {
            Progress.IsIndeterminate = true;
            StatusText.Text = $"Checking files… {p.FilesScanned:N0} checked, {p.FilesToCopy:N0} to copy ({ByteSize.Format(p.BytesToCopy)})";
            DetailText.Text = ErrorSuffix(p.ErrorCount).TrimStart(' ', '·');
            return;
        }

        Progress.IsIndeterminate = false;
        Progress.Value = p.Fraction;
        Taskbar.ProgressState = TaskbarItemProgressState.Normal;
        Taskbar.ProgressValue = p.Fraction;

        StatusText.Text = $"Copying {p.FilesDone:N0} of {p.FilesToCopy:N0} files — "
                        + $"{ByteSize.Format(p.BytesDone)} of {ByteSize.Format(p.BytesToCopy)}";
        string remaining = p.EstimatedRemaining is TimeSpan left ? $"about {FormatDuration(left)} left" : "estimating time left…";
        DetailText.Text = $"Elapsed {FormatDuration(p.Elapsed)} · {remaining}{ErrorSuffix(p.ErrorCount)}";
    }

    private void ShowResult(BackupEngine engine, BackupResult result)
    {
        Taskbar.ProgressState = TaskbarItemProgressState.None;
        string copied = $"{result.FilesCopied:N0} {Plural(result.FilesCopied, "file")} ({ByteSize.Format(result.BytesCopied)})";

        switch (result.Status)
        {
            case BackupStatus.InsufficientSpace:
                Progress.Value = 0;
                StatusText.Text = "Not enough space in the backup location. Nothing was copied.";
                DetailText.Text = $"Needs {ByteSize.Format(result.BytesRequired)} more, "
                                + $"but only {ByteSize.Format(result.BytesAvailable ?? 0)} is free.";
                MessageBox.Show(this, StatusText.Text + "\n\n" + DetailText.Text, "Not enough space",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                break;

            case BackupStatus.Cancelled:
                StatusText.Text = $"Backup cancelled. Copied {copied} before stopping.";
                DetailText.Text = $"Ran for {FormatDuration(result.Elapsed)}.";
                break;

            default:
                Progress.Value = 1;
                StatusText.Text = result.FilesCopied == 0 && result.Errors.Count == 0
                    ? "Your backup is already up to date."
                    : $"Backup finished. Copied {copied}.";
                DetailText.Text = $"Took {FormatDuration(result.Elapsed)} · "
                                + $"{result.FilesUpToDate:N0} {Plural(result.FilesUpToDate, "file")} already up to date";
                break;
        }

        if (result.Errors.Count > 0)
            OfferErrorReport(engine, result);
    }

    private void OfferErrorReport(BackupEngine engine, BackupResult result)
    {
        try
        {
            _reportPath = ErrorReport.Write(ErrorReport.DefaultFolder, engine, result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{result.Errors.Count:N0} files couldn't be copied, and the error report couldn't be saved:\n\n{ex.Message}",
                "Some files weren't copied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReportBtn.Visibility = Visibility.Visible;
        DetailText.Text += ErrorSuffix(result.Errors.Count);
        var answer = MessageBox.Show(this,
            $"{result.Errors.Count:N0} {Plural(result.Errors.Count, "file or folder", "files or folders")} couldn't be copied. "
            + "Would you like to see the list and the reasons?",
            "Some files weren't copied", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
            OpenReport();
    }

    private void ReportBtn_Click(object sender, RoutedEventArgs e) => OpenReport();

    private void OpenReport()
    {
        if (_reportPath is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(_reportPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open the report. It's saved at:\n{_reportPath}\n\n{ex.Message}", "Error report");
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!IsRunning)
            return;

        // Wait for the engine to stop between files rather than killing it mid-copy.
        e.Cancel = true;
        var answer = MessageBox.Show(this, "A backup is still running. Stop it and close?", "Backer Upper",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            _closeWhenStopped = true;
            RequestStop();
        }
    }

    private static string ErrorSuffix(int errors) =>
        errors > 0 ? $" · {errors:N0} {Plural(errors, "error")}" : "";

    private static string Plural(int count, string singular, string? plural = null) =>
        count == 1 ? singular : plural ?? singular + "s";

    private static string FormatDuration(TimeSpan time) =>
        $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";
}

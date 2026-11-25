# Backer Upper

A simple Windows app for backing up one folder into another — for example, your Documents folder onto an external drive.

Pick the folder to back up, pick where the backup goes, choose what should happen to files that are already in the backup, and press **Start backup**.

## Backup modes

| Mode | What it does |
| --- | --- |
| **Copy new and replace changed** (default) | Copies files missing from the backup and replaces files that changed since they were last backed up. The usual choice for repeat backups. |
| **Copy and replace all** | Copies every file and overwrites whatever is already in the backup. |
| **Copy new only** | Copies files missing from the backup and never touches anything already there. |

Backer Upper never deletes anything from the backup. Files you delete from the source stay in the backup.

## How it works

1. **Check:** it scans the source folder, works out which files need copying, and checks there is enough free space. If there isn't, it stops before writing anything.
2. **Copy:** it copies those files, showing progress, time elapsed and an estimate of time left. You can cancel at any point; it stops after the file it's on.

A few details:

- Files are "changed" when the source copy was modified more recently than the backup copy (allowing 2 seconds of slack for FAT/exFAT drives, which store times coarsely).
- Folder shortcuts (junctions and symbolic links), `$RECYCLE.BIN` and `System Volume Information` are skipped.
- Read-only files in the backup are still updated.
- Network locations (`\\server\share`) and long paths work.
- If any files can't be copied (for example, because another program has them open), the backup carries on and writes a report to `Documents\Backer_Upper_Reports` listing each file and why it failed.
- The last folders and mode you used are remembered in `%AppData%\Backer Upper\settings.json`.

## Requirements

Windows 10 or 11 with the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0), or use a self-contained build (see below), which needs nothing installed.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Open `Backer Upper/Backer Upper.sln` in Visual Studio 2022, or from a terminal:

```bash
cd "Backer Upper"
dotnet build
dotnet test
```

To make a single self-contained `.exe` that runs without .NET installed:

```bash
dotnet publish "Backer Upper/Backer Upper/Backer Upper.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Project layout

| Project | Contents |
| --- | --- |
| `Backer Upper` | The WPF app (Windows only). |
| `BackerUpper.Core` | The backup engine, free-space check and error reports. No UI code; runs on any OS. |
| `BackerUpper.Tests` | xUnit tests for the core library. Run anywhere with `dotnet test`. |

## Version history

### 1.1.0

- Moved to .NET 8 and split the copy logic into a separate, tested library.
- **Fixed:** "Copy new and replace changed" failed to replace changed files and reported them all as errors.
- **Fixed:** the free-space check looked at the drive's name instead of its letter, so it often stopped backups for no reason, and it crashed on drives with no disk inserted.
- **Fixed:** "Copy and replace all" deleted backup files before copying, so a failed copy or a cancel could lose them.
- **Fixed:** an error early in the scan could leave the app stuck on "Cancel" forever.
- **Fixed:** files could be copied to the wrong place when a folder name appeared more than once in a path.
- **Fixed:** folder links could make the scan loop forever; choosing a backup folder inside the source is now blocked.
- Added a progress bar (also shown on the taskbar), time remaining, and a clear finished, cancelled or out-of-space message.
- Error reports are timestamped instead of overwritten, include the reason each file failed, and can be reopened with a button.
- Remembers your last folders and mode; Browse opens at the current folder; folders can be dragged onto the path boxes.
- Asks before closing while a backup is running, and stops cleanly between files.

### 1.0.0

- First release.

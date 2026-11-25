using System.Runtime.InteropServices;

namespace BackerUpper.Core;

public static partial class DiskSpace
{
    /// <summary>
    /// Free bytes available to the current user at <paramref name="path"/>, or null if it can't be determined.
    /// Works for drive letters, mounted folders and network shares (\\server\share).
    /// </summary>
    public static long? GetAvailableBytes(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // GetDiskFreeSpaceEx needs a trailing backslash for UNC paths.
                string directory = Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
                return GetDiskFreeSpaceEx(directory, out ulong available, out _, out _) ? (long)Math.Min(available, long.MaxValue) : null;
            }
            return new DriveInfo(path).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(
        string directoryName, out ulong freeBytesAvailable, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
}

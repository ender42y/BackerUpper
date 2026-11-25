using System.Globalization;

namespace BackerUpper.Core;

public static class ByteSize
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats a byte count for people, e.g. "512 bytes", "1.5 MB", "2.25 GB".</summary>
    public static string Format(long bytes, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        if (bytes < 1024)
            return bytes.ToString(provider) + " " + (bytes == 1 ? "byte" : "bytes");

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("0.##", provider) + " " + Units[unit];
    }
}

using System.Globalization;
using System.Text;

namespace RemoteSupport.Application;

public static class SafeReceivedFile
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string NormalizeDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalized = displayName.Normalize(NormalizationForm.FormKC);
        if (normalized.Length > 240 || normalized is "." or ".." || Path.IsPathRooted(normalized) ||
            normalized.IndexOfAny(['/', '\\', ':', '\0']) >= 0 || normalized.Any(char.IsControl) ||
            !string.Equals(Path.GetFileName(normalized), normalized, StringComparison.Ordinal))
        {
            throw Invalid();
        }
        normalized = normalized.TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(normalized)) throw Invalid();
        string stem = normalized.Split('.', 2)[0];
        if (ReservedNames.Contains(stem)) throw Invalid();
        return normalized;
    }

    public static string ChooseDestination(string destinationRoot, string normalizedName)
    {
        string root = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(root);
        string stem = Path.GetFileNameWithoutExtension(normalizedName);
        string extension = Path.GetExtension(normalizedName);
        for (int suffix = 0; suffix < 10_000; suffix++)
        {
            string name = suffix == 0 ? normalizedName : string.Create(CultureInfo.InvariantCulture, $"{stem} ({suffix}){extension}");
            string candidate = Path.GetFullPath(Path.Combine(root, name));
            EnsureWithinRoot(root, candidate);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new DataFeatureException("FILE_PATH_INVALID", "No safe destination filename is available.");
    }

    public static void EnsureWithinRoot(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw Invalid();
    }

    private static DataFeatureException Invalid() => new("FILE_PATH_INVALID", "The received filename or destination is unsafe.");
}

using System.IO;
using System.Security.Cryptography;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

/// <summary>
/// Resolves icon files used by previews and prepares a portable icon reference
/// for an edit operation. KMZ icons are copied beside the package's main KML
/// under files/icons. Ordinary KML files use an absolute file URI.
/// </summary>
public sealed class KmlIconAssetService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp"
        };

    public bool IsSupportedIconFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               File.Exists(path) &&
               SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public void ValidateIconFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Choose an icon image file first.");

        if (!File.Exists(path))
            throw new FileNotFoundException("The selected icon image could not be found.", path);

        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            throw new InvalidOperationException(
                "The icon must be a PNG, JPG, JPEG, GIF, or BMP image.");
        }
    }

    public string PrepareIconHref(
        KmlDocumentContext context,
        string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateIconFile(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);

        if (!context.IsKmz)
            return new Uri(fullSourcePath).AbsoluteUri;

        if (string.IsNullOrWhiteSpace(context.WorkingDirectory) ||
            !Directory.Exists(context.WorkingDirectory))
        {
            throw new InvalidOperationException(
                "The extracted KMZ workspace is no longer available.");
        }

        var kmlDirectory = Path.GetDirectoryName(context.LoadedKmlPath)
            ?? context.WorkingDirectory;

        var iconDirectory = Path.Combine(kmlDirectory, "files", "icons");
        Directory.CreateDirectory(iconDirectory);

        var destinationFileName = BuildBundledFileName(fullSourcePath);
        var destinationPath = Path.Combine(iconDirectory, destinationFileName);

        if (!File.Exists(destinationPath))
            File.Copy(fullSourcePath, destinationPath, overwrite: false);

        return Path.GetRelativePath(kmlDirectory, destinationPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public string? ResolveLocalIconPath(
        KmlDocumentContext context,
        string? href)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(href))
            return null;

        var value = href.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                return null;

            return File.Exists(uri.LocalPath)
                ? uri.LocalPath
                : null;
        }

        if (Path.IsPathRooted(value))
            return File.Exists(value) ? Path.GetFullPath(value) : null;

        var decoded = Uri.UnescapeDataString(value)
            .Replace('/', Path.DirectorySeparatorChar);

        var kmlDirectory = Path.GetDirectoryName(context.LoadedKmlPath)
            ?? Path.GetDirectoryName(context.SourcePath)
            ?? Environment.CurrentDirectory;

        var candidate = Path.GetFullPath(Path.Combine(kmlDirectory, decoded));
        return File.Exists(candidate) ? candidate : null;
    }

    private static string BuildBundledFileName(string sourcePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(baseName
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray())
            .Trim(' ', '.', '-');

        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "icon";

        if (safeName.Length > 60)
            safeName = safeName[..60];

        using var stream = File.OpenRead(sourcePath);
        var hash = SHA256.HashData(stream);
        var shortHash = Convert.ToHexString(hash)[..12].ToLowerInvariant();

        return $"{safeName}-{shortHash}{extension}";
    }
}

using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using KmlScopedEditor.Models;

namespace KmlScopedEditor.Services;

/// <summary>
/// Opens ordinary KML files and extracts KMZ packages into a temporary
/// workspace. When a KMZ is saved, every embedded resource is repackaged.
/// Long-running work reports progress and checks for cancellation between
/// safe processing steps.
/// </summary>
public sealed class KmlPackageService
{
    private readonly KmlLoader _loader;

    public KmlPackageService(KmlLoader loader)
    {
        _loader = loader;
    }

    public KmlDocumentContext Open(
        string sourcePath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A file path is required.", nameof(sourcePath));

        cancellationToken.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(sourcePath);

        if (extension.Equals(".kml", StringComparison.OrdinalIgnoreCase))
        {
            Report(progress, "Opening KML file...", Path.GetFileName(sourcePath), 10);

            return _loader.Load(
                kmlPath: sourcePath,
                sourcePath: sourcePath,
                isKmz: false,
                embeddedResourceCount: 0,
                progress: progress,
                cancellationToken: cancellationToken);
        }

        if (extension.Equals(".kmz", StringComparison.OrdinalIgnoreCase))
        {
            return OpenKmz(sourcePath, progress, cancellationToken);
        }

        throw new NotSupportedException(
            "Only .kml and .kmz Google Earth files are supported.");
    }

    public void SaveAs(
        KmlDocumentContext context,
        string destinationPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("A destination path is required.", nameof(destinationPath));

        cancellationToken.ThrowIfCancellationRequested();

        if (context.IsKmz)
        {
            SaveKmz(context, destinationPath, progress, cancellationToken);
        }
        else
        {
            SaveKml(
                context.Document,
                destinationPath,
                progress,
                cancellationToken);
        }
    }

    private KmlDocumentContext OpenKmz(
        string sourcePath,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, "Opening KMZ package...", Path.GetFileName(sourcePath), 5);

        var workspace = Path.Combine(
            Path.GetTempPath(),
            "KmlScopedEditor",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workspace);

        try
        {
            ExtractKmzSafely(
                sourcePath,
                workspace,
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, "Locating the main KML document...", null, 27);

            var kmlPath = FindMainKml(workspace)
                ?? throw new InvalidDataException(
                    "The KMZ package does not contain a KML file.");

            var relativePath = Path.GetRelativePath(workspace, kmlPath);

            var embeddedResourceCount = Directory
                .EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
                .Count(path => !Path.GetExtension(path)
                    .Equals(".kml", StringComparison.OrdinalIgnoreCase));

            return _loader.Load(
                kmlPath: kmlPath,
                sourcePath: sourcePath,
                isKmz: true,
                workingDirectory: workspace,
                kmlEntryRelativePath: relativePath,
                embeddedResourceCount: embeddedResourceCount,
                progress: progress,
                cancellationToken: cancellationToken);
        }
        catch
        {
            TryDeleteDirectory(workspace);
            throw;
        }
    }

    private static void ExtractKmzSafely(
        string sourcePath,
        string workspace,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Path.GetFullPath(workspace)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(sourcePath);

        var totalEntries = Math.Max(archive.Entries.Count, 1);
        var processedEntries = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativeName = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(relativeName))
            {
                processedEntries++;
                continue;
            }

            var destinationPath = Path.GetFullPath(
                Path.Combine(workspace, relativeName));

            if (!destinationPath.StartsWith(
                    workspaceRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unsafe path found in KMZ package: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
            }
            else
            {
                var destinationDirectory = Path.GetDirectoryName(destinationPath);

                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                using var sourceStream = entry.Open();
                using var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: false);

                sourceStream.CopyTo(destinationStream);
            }

            processedEntries++;

            if (processedEntries % 10 == 0 ||
                processedEntries == totalEntries)
            {
                var percentage = 5d +
                    (processedEntries / (double)totalEntries * 20d);

                Report(
                    progress,
                    "Extracting KMZ package...",
                    $"{processedEntries:N0} of {totalEntries:N0} entries",
                    percentage);
            }
        }
    }

    private static string? FindMainKml(string workspace)
    {
        var allKmlFiles = Directory
            .EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path)
                .Equals(".kml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rootDocKml = allKmlFiles.FirstOrDefault(path =>
            string.Equals(
                Path.GetRelativePath(workspace, path),
                "doc.kml",
                StringComparison.OrdinalIgnoreCase));

        if (rootDocKml is not null)
            return rootDocKml;

        return allKmlFiles
            .OrderBy(path => Path.GetRelativePath(workspace, path)
                .Count(character =>
                    character == Path.DirectorySeparatorChar ||
                    character == Path.AltDirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void SaveKmz(
        KmlDocumentContext context,
        string destinationPath,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.WorkingDirectory) ||
            !Directory.Exists(context.WorkingDirectory))
        {
            throw new InvalidOperationException(
                "The extracted KMZ workspace is no longer available.");
        }

        EnsureExtension(destinationPath, ".kmz");
        cancellationToken.ThrowIfCancellationRequested();

        Report(progress, "Updating the KML inside the package...", null, 8);

        // Update only the KML inside the extracted package. All images and
        // other resources remain untouched in the workspace.
        context.Document.Save(
            context.LoadedKmlPath,
            SaveOptions.DisableFormatting);

        cancellationToken.ThrowIfCancellationRequested();

        var temporaryArchive = Path.Combine(
            Path.GetTempPath(),
            $"KmlScopedEditor_{Guid.NewGuid():N}.kmz");

        try
        {
            CreateArchiveFromDirectory(
                context.WorkingDirectory,
                temporaryArchive,
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, "Validating the KMZ output...", null, 94);
            ValidateKmz(temporaryArchive, context.KmlEntryRelativePath);

            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, "Finalising the KMZ file...", null, 98);
            ReplaceFile(temporaryArchive, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryArchive))
                File.Delete(temporaryArchive);
        }
    }

    private static void CreateArchiveFromDirectory(
        string sourceDirectory,
        string destinationArchive,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .ToList();

        var totalFiles = Math.Max(files.Count, 1);

        using var archiveStream = new FileStream(
            destinationArchive,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None);

        using var archive = new ZipArchive(
            archiveStream,
            ZipArchiveMode.Create,
            leaveOpen: false);

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = files[index];
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath)
                .Replace(Path.DirectorySeparatorChar, '/');

            var entry = archive.CreateEntry(
                relativePath,
                CompressionLevel.Optimal);

            using var input = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: false);

            using var output = entry.Open();
            input.CopyTo(output);

            var processed = index + 1;

            if (processed % 10 == 0 || processed == files.Count)
            {
                var percentage = 12d +
                    (processed / (double)totalFiles * 78d);

                Report(
                    progress,
                    "Rebuilding the KMZ package...",
                    $"{processed:N0} of {files.Count:N0} files",
                    percentage);
            }
        }
    }

    private static void SaveKml(
        XDocument document,
        string destinationPath,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureExtension(destinationPath, ".kml");
        cancellationToken.ThrowIfCancellationRequested();

        var destinationDirectory = Path.GetDirectoryName(
            Path.GetFullPath(destinationPath));

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = Path.Combine(
            destinationDirectory ?? Path.GetTempPath(),
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Report(progress, "Writing the KML output...", null, 35);
            document.Save(temporaryPath, SaveOptions.DisableFormatting);

            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, "Validating the KML output...", null, 82);
            _ = XDocument.Load(temporaryPath, LoadOptions.None);

            cancellationToken.ThrowIfCancellationRequested();

            Report(progress, "Finalising the KML file...", null, 96);
            ReplaceFile(temporaryPath, destinationPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void ValidateKmz(
        string archivePath,
        string kmlEntryRelativePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var normalisedKmlPath = kmlEntryRelativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        var entry = archive.Entries.FirstOrDefault(item =>
            item.FullName.Equals(
                normalisedKmlPath,
                StringComparison.OrdinalIgnoreCase));

        entry ??= archive.Entries.FirstOrDefault(item =>
            Path.GetExtension(item.FullName)
                .Equals(".kml", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new InvalidDataException(
                "The rebuilt KMZ does not contain a KML document.");
        }

        using var stream = entry.Open();
        _ = XDocument.Load(stream, LoadOptions.None);
    }

    private static void ReplaceFile(string sourcePath, string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(
            Path.GetFullPath(destinationPath));

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    private static void EnsureExtension(string path, string requiredExtension)
    {
        if (!Path.GetExtension(path)
            .Equals(requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The output file must use the {requiredExtension} extension.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup after a failed open.
        }
    }

    private static void Report(
        IProgress<OperationProgress>? progress,
        string message,
        string? detail,
        double? percent)
    {
        progress?.Report(new OperationProgress(message, detail, percent));
    }
}

using System.Text;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Export;

/// <summary>
/// Writes parent report exports as UTF-8 CSV with BOM (issue #37). The BOM is
/// what makes Excel detect UTF-8 instead of mojibake for Vietnamese text; names
/// never contain path separators and an existing file is never overwritten.
/// </summary>
public sealed class LocalCsvExportStore : ICsvExportStore
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly CsvExportOptions _options;

    public LocalCsvExportStore(CsvExportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<string> SaveAsync(
        string fileName,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The export name must be a plain file name.", nameof(fileName));
        }

        var directory = string.IsNullOrWhiteSpace(_options.DirectoryPath)
            ? CsvExportOptions.DefaultDirectory
            : _options.DirectoryPath;

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        var suffix = 2;

        while (File.Exists(path))
        {
            if (suffix > 1_000)
                throw new IOException("Could not create a unique CSV export file.");

            path = Path.Combine(directory, AddSuffix(fileName, suffix++));
        }

        try
        {
            await File.WriteAllTextAsync(path, content, Utf8WithBom, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        return path;
    }

    private static string AddSuffix(string fileName, int suffix)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return $"{stem}-{suffix}{extension}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

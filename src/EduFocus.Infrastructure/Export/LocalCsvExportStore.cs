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
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

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
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName is "." or ".."
            || fileName[^1] is '.' or ' '
            || ReservedDeviceNames.Contains(Path.GetFileNameWithoutExtension(fileName)))
        {
            throw new ArgumentException("The export name must be a plain file name.", nameof(fileName));
        }

        var directory = string.IsNullOrWhiteSpace(_options.DirectoryPath)
            ? CsvExportOptions.DefaultDirectory
            : _options.DirectoryPath;

        Directory.CreateDirectory(directory);

        string? claimedPath = null;
        FileStream? stream = null;
        try
        {
            var suffix = 2;
            var candidate = Path.Combine(directory, fileName);

            while (true)
            {
                try
                {
                    stream = new FileStream(
                        candidate,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 4096,
                        useAsync: true);
                    claimedPath = candidate;
                    break;
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    if (suffix > 1_000)
                        throw new IOException("Could not create a unique CSV export file.");

                    candidate = Path.Combine(directory, AddSuffix(fileName, suffix++));
                }
            }

            await using (stream)
            await using (var writer = new StreamWriter(stream, Utf8WithBom, bufferSize: 4096, leaveOpen: true))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return claimedPath;
        }
        catch
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);

            if (claimedPath is not null)
                TryDelete(claimedPath);

            throw;
        }
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

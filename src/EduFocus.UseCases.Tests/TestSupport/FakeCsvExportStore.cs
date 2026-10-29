using EduFocus.Ports.Outbound.Services;

namespace EduFocus.UseCases.Tests.TestSupport;

internal sealed class FakeCsvExportStore : ICsvExportStore
{
    public string FilePath { get; set; } = Path.Combine(Path.GetTempPath(), "EduFocus", "captured.csv");

    public bool ThrowOnSave { get; set; }

    public int SaveCalls { get; private set; }

    public string? FileName { get; private set; }

    public string? Content { get; private set; }

    public Task<string> SaveAsync(
        string fileName,
        string content,
        CancellationToken cancellationToken = default)
    {
        SaveCalls++;

        if (ThrowOnSave)
            throw new IOException("The export directory is unavailable.");

        FileName = fileName;
        Content = content;
        return Task.FromResult(FilePath);
    }
}

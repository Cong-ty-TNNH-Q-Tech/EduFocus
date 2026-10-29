namespace EduFocus.UseCases.Privacy;

/// <summary>Batch size for the irreversible delete-all (UC11, issue #38).</summary>
public sealed record DataDeletionOptions
{
    public int BatchSize { get; init; } = 500;
}

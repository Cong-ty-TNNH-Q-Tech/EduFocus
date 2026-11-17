namespace EduFocus.Cloud.Api.Options;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    public int MaxEventsPerBatch { get; set; } = 50;

    public int MaxBodyBytes { get; set; } = 64 * 1024;

    public int DailySummaryWindowDays { get; set; } = 7;
}

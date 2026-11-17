namespace EduFocus.Cloud.Api.Options;

public sealed class PushOptions
{
    public const string SectionName = "Push";

    public bool Enabled { get; set; }

    public string? ProjectId { get; set; }

    public string? ServiceAccountPath { get; set; }

    public string? ServiceAccountJson { get; set; }

    public int PollSeconds { get; set; } = 60;

    public int MaxAttempts { get; set; } = 3;

    public int BatchSize { get; set; } = 50;
}

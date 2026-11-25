namespace EduFocus.Cloud.Api.Options;

public sealed class PushOptions
{
    public const string SectionName = "Push";

    public bool Enabled { get; set; }

    public string? VapidPublicKey { get; set; }

    public string? VapidPrivateKey { get; set; }

    public string? VapidSubject { get; set; }

    public string[] AllowedEndpointHosts { get; set; } = [];

    public int PollSeconds { get; set; } = 60;

    public int MaxAttempts { get; set; } = 3;

    public int BatchSize { get; set; } = 50;
}

namespace EduFocus.Cloud.Api.Options;

public sealed class CloudApiOptions
{
    public const string SectionName = "Cloud";

    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
}

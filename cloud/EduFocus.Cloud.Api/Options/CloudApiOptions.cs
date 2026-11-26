namespace EduFocus.Cloud.Api.Options;

public sealed class CloudApiOptions
{
    public const string SectionName = "Cloud";

    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    public bool TrustForwardedHeaders { get; set; }

    public string[] TrustedProxyNetworks { get; set; } = [];

    public string[] TrustedProxies { get; set; } = [];
}

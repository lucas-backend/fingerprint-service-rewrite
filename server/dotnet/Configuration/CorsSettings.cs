namespace FingerprintService.Configuration;

/// <summary>
/// CORS configuration. When <see cref="AllowedOrigins"/> is empty, no CORS
/// middleware is registered, which is the correct default for server-to-server
/// deployments.
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

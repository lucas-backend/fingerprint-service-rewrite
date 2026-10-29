namespace FingerprintService.Configuration;

/// <summary>
/// Server resource limits. Bound from the "Server" configuration section.
/// </summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    /// <summary>
    /// Maximum accepted HTTP request body size in bytes. The R503 payload is
    /// 18,432 bytes; the default leaves headroom without allowing arbitrary
    /// large bodies.
    /// </summary>
    public long MaxRequestBodySizeBytes { get; set; } = 64 * 1024;
}

using System.ComponentModel.DataAnnotations;

namespace FingerprintService.Configuration;

/// <summary>
/// Fingerprint recognition and processing configuration.
/// Bound from the "Fingerprint" configuration section.
/// </summary>
public sealed class FingerprintOptions
{
    public const string SectionName = "Fingerprint";

    /// <summary>
    /// Sensor DPI used by SourceAFIS scale estimation. Must match the R503
    /// sensor characteristics; not a calibrated production value by default.
    /// </summary>
    [Range(1, 10000)]
    public int Dpi { get; set; } = 508;

    /// <summary>
    /// Minimum SourceAFIS similarity score required to consider a probe a
    /// match. IMPORTANT: this must be calibrated against a representative,
    /// real dataset before production use. The default is a development value.
    /// </summary>
    [Range(0, double.MaxValue)]
    public double MatchThreshold { get; set; } = 40;

    public FingerprintDiagnosticsOptions Diagnostics { get; set; } = new();

    /// <summary>
    /// Maximum templates allowed per subject. Enforced at repository level
    /// (evict oldest when limit exceeded). Default 3 covers multi-finger use.
    /// </summary>
    [Range(1, 100)]
    public int MaxTemplatesPerSubject { get; set; } = 3;
}

/// <summary>
/// Diagnostic capture configuration. Disabled by default; normal fingerprint
/// processing must never write to disk.
/// </summary>
public sealed class FingerprintDiagnosticsOptions
{
    /// <summary>
    /// When true, raw captures may be written as .pgm files for debugging.
    /// MUST remain false in production. Files contain biometric data.
    /// </summary>
    public bool SavePgm { get; set; }

    /// <summary>
    /// Directory used for diagnostic captures. Relative paths resolve against
    /// the content root. Must not be served publicly and must not be committed.
    /// </summary>
    public string Directory { get; set; } = "./diagnostic-captures";
}

namespace FingerprintService.Diagnostics;

/// <summary>
/// Optional diagnostic capture storage. Production processing must never write
/// biometric data to disk; implementations are expected to no-op when disabled.
/// </summary>
public interface IFingerprintDiagnosticStore
{
    bool IsEnabled { get; }

    /// <summary>
    /// Persists a grayscale capture for diagnostics. Returns the created file
    /// name, or null when disabled. Failures are logged and never bubble up.
    /// </summary>
    Task<string?> SavePgmAsync(byte[] grayscale, CancellationToken cancellationToken);
}

using FingerprintService.Configuration;
using FingerprintService.Imaging;
using Microsoft.Extensions.Options;

namespace FingerprintService.Diagnostics;

/// <summary>
/// Writes .pgm captures to a configurable directory when
/// Fingerprint:Diagnostics:SavePgm is enabled. Disabled by default.
/// </summary>
public sealed class FileSystemDiagnosticStore : IFingerprintDiagnosticStore
{
    private readonly FingerprintDiagnosticsOptions _options;
    private readonly R503ImageService _imageService;
    private readonly ILogger<FileSystemDiagnosticStore> _logger;

    public FileSystemDiagnosticStore(
        IOptions<FingerprintOptions> options,
        R503ImageService imageService,
        ILogger<FileSystemDiagnosticStore> logger)
    {
        _options = options.Value.Diagnostics;
        _imageService = imageService;
        _logger = logger;
    }

    public bool IsEnabled => _options.SavePgm;

    public async Task<string?> SavePgmAsync(
        byte[] grayscale,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            string directory = Path.GetFullPath(_options.Directory);

            Directory.CreateDirectory(directory);

            string fileName =
                $"capture_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.pgm";

            string filePath = Path.Combine(directory, fileName);

            byte[] pgm = _imageService.CreatePgm(grayscale);

            await File.WriteAllBytesAsync(filePath, pgm, cancellationToken);

            _logger.LogInformation(
                "Diagnostic PGM capture saved to configured directory: {FileName}",
                fileName);

            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to write diagnostic PGM capture; request continues.");

            return null;
        }
    }
}

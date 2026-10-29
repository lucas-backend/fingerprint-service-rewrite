using FingerprintService.Configuration;
using FingerprintService.Imaging;
using Microsoft.Extensions.Options;
using SourceAFIS;

namespace FingerprintService.Recognition;

/// <summary>
/// SourceAFIS-backed recognition engine.
///
/// Pipeline (preserved from the working implementation):
///   R503 packed image -> nibble unpack -> 192x192 grayscale ->
///   FingerprintImage -> FingerprintTemplate -> serialized template.
///
/// The serialized template byte format is unchanged, so templates already
/// stored in PostgreSQL remain readable.
/// </summary>
public sealed class FingerprintRecognitionService : IFingerprintRecognitionService
{
    private readonly FingerprintOptions _options;
    private readonly R503ImageService _imageService;

    public FingerprintRecognitionService(
        IOptions<FingerprintOptions> options,
        R503ImageService imageService)
    {
        _options = options.Value;
        _imageService = imageService;
    }

    public double MatchThreshold => _options.MatchThreshold;

    public byte[] CreateTemplateFromR503(byte[] r503Data)
    {
        byte[] grayscale = _imageService.UnpackImage(r503Data);
        return CreateTemplateFromGrayscale(grayscale);
    }

    public byte[] CreateTemplateFromGrayscale(byte[] grayscale)
    {
        ArgumentNullException.ThrowIfNull(grayscale);

        if (grayscale.Length != R503ImageFormat.GrayscaleSize)
        {
            throw new ArgumentException(
                $"Ukuran grayscale image tidak sesuai. " +
                $"Expected: {R503ImageFormat.GrayscaleSize}, Actual: {grayscale.Length}",
                nameof(grayscale));
        }

        var imageOptions = new FingerprintImageOptions
        {
            Dpi = _options.Dpi
        };

        var image = new FingerprintImage(
            R503ImageFormat.Width,
            R503ImageFormat.Height,
            grayscale,
            imageOptions);

        var template = new FingerprintTemplate(image);

        return template.ToByteArray();
    }

    public void ValidateTemplate(byte[] template)
    {
        ArgumentNullException.ThrowIfNull(template);

        // Deserialization is the validation step: a malformed template throws.
        _ = new FingerprintTemplate(template);
    }

    public double Match(byte[] probeTemplate, byte[] candidateTemplate)
    {
        ArgumentNullException.ThrowIfNull(probeTemplate);
        ArgumentNullException.ThrowIfNull(candidateTemplate);

        var probe = new FingerprintTemplate(probeTemplate);
        var candidate = new FingerprintTemplate(candidateTemplate);

        var matcher = new FingerprintMatcher(probe);

        return matcher.Match(candidate);
    }

    public double Match(FingerprintTemplate probe, byte[] candidateTemplate)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(candidateTemplate);

        var candidate = new FingerprintTemplate(candidateTemplate);
        var matcher = new FingerprintMatcher(probe);
        return matcher.Match(candidate);
    }

    public bool IsMatch(double score) => score >= MatchThreshold;
}

namespace FingerprintService.Recognition;

/// <summary>
/// Abstraction over the fingerprint recognition engine (SourceAFIS). Kept as
/// an interface so application logic can be unit tested without biometric data.
/// </summary>
public interface IFingerprintRecognitionService
{
    /// <summary>
    /// Converts a packed R503 image into a serialized SourceAFIS template.
    /// </summary>
    byte[] CreateTemplateFromR503(byte[] r503Data);

    /// <summary>
    /// Converts an 8-bit grayscale 192x192 image into a serialized template.
    /// </summary>
    byte[] CreateTemplateFromGrayscale(byte[] grayscale);

    /// <summary>
    /// Ensures a serialized template can be read back by the recognition engine.
    /// Throws when the template is malformed.
    /// </summary>
    void ValidateTemplate(byte[] template);

    /// <summary>
    /// Computes the SourceAFIS similarity score between two serialized templates.
    /// </summary>
    double Match(byte[] probeTemplate, byte[] candidateTemplate);

    /// <summary>
    /// Computes score using an already-deserialized probe (avoids re-parsing probe per candidate).
    /// </summary>
    double Match(SourceAFIS.FingerprintTemplate probe, byte[] candidateTemplate);

    /// <summary>
    /// Configured minimum similarity score that counts as a match.
    /// </summary>
    double MatchThreshold { get; }

    /// <summary>
    /// Applies the configured threshold to a similarity score.
    /// </summary>
    bool IsMatch(double score);
}

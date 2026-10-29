using System.Diagnostics;
using FingerprintService.Data;
using FingerprintService.Errors;
using FingerprintService.Imaging;
using FingerprintService.Metrics;
using FingerprintService.Recognition;

namespace FingerprintService.Application;

/// <summary>
/// Identification use case: validate the probe, build a template, score it
/// against all enrolled candidates and apply the configured threshold.
/// </summary>
public sealed class IdentificationService
{
    private readonly IFingerprintRepository _repository;
    private readonly IFingerprintRecognitionService _recognition;
    private readonly R503ImageService _imageService;
    private readonly ILogger<IdentificationService> _logger;
    private readonly FingerprintMetrics? _metrics;

    public IdentificationService(
        IFingerprintRepository repository,
        IFingerprintRecognitionService recognition,
        R503ImageService imageService,
        ILogger<IdentificationService> logger,
        FingerprintMetrics? metrics = null)
    {
        _repository = repository;
        _recognition = recognition;
        _imageService = imageService;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<IdentificationOutcome> IdentifyAsync(
        byte[] r503Data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(r503Data);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Fingerprint identification started");

        if (!_imageService.IsValidPackedSize(r503Data.Length))
        {
            throw new ValidationException(
                ApiErrorCodes.InvalidFingerprintImage,
                "Invalid fingerprint image.");
        }

        byte[] probeTemplate;

        try
        {
            probeTemplate = _recognition.CreateTemplateFromR503(r503Data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Probe template generation failed: {Reason}", ex.GetType().Name);

            throw new ValidationException(
                ApiErrorCodes.InvalidFingerprintImage,
                "Invalid fingerprint image.");
        }

        _logger.LogInformation("Probe template generated. SizeBytes={SizeBytes}", probeTemplate.Length);

        IReadOnlyList<CandidateTemplate> candidates =
            await _repository.GetCandidatesAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            stopwatch.Stop();
            double elapsed0 = stopwatch.Elapsed.TotalMilliseconds;

            _logger.LogInformation(
                "Identification completed. CandidateCount=0 Matched=false DurationMs={DurationMs}",
                elapsed0);

            _metrics?.RecordIdentification(0, 0, elapsed0, false);

            return IdentificationOutcome.NoCandidates();
        }

        // Keep top-3 for observability without leaking templates.
        var topScores = new List<double>(3);
        double bestScore = double.MinValue;
        CandidateTemplate? best = null;

        // Use cached probe path when available; fall back to byte-array match for fakes/compat.
        bool useCachedProbe = false;
        SourceAFIS.FingerprintTemplate? probe = null;
        try
        {
            probe = new SourceAFIS.FingerprintTemplate(probeTemplate);
            useCachedProbe = true;
        }
        catch
        {
            useCachedProbe = false;
        }

        foreach (var candidate in candidates)
        {
            double score = useCachedProbe && probe is not null
                ? _recognition.Match(probe, candidate.Template)
                : _recognition.Match(probeTemplate, candidate.Template);
            // Maintain sorted top-3 descending
            int idx = topScores.BinarySearch(score);
            if (idx < 0) idx = ~idx;
            topScores.Insert(idx, score);
            if (topScores.Count > 3) topScores.RemoveAt(0);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        bool matched = _recognition.IsMatch(bestScore);

        stopwatch.Stop();

        string top3 = string.Join(", ", topScores.OrderByDescending(x => x).Select(s => Math.Round(s, 2).ToString()));
        double elapsed = stopwatch.Elapsed.TotalMilliseconds;

        _logger.LogInformation(
            "Candidate matching completed. CandidateCount={CandidateCount} " +
            "Matched={Matched} Score={Score} Top3=[{Top3}] Threshold={Threshold} DurationMs={DurationMs}",
            candidates.Count,
            matched,
            Math.Round(bestScore, 2),
            top3,
            _recognition.MatchThreshold,
            elapsed);

        _metrics?.RecordIdentification(candidates.Count, bestScore, elapsed, matched);

        if (!matched || best is null)
        {
            return IdentificationOutcome.NotMatched(bestScore, candidates.Count);
        }

        _logger.LogInformation(
            "Identification completed. Matched=true ExternalUserId={ExternalUserId} " +
            "FingerprintId={FingerprintId}",
            best.ExternalUserId,
            best.FingerprintId);

        return IdentificationOutcome.FromMatch(bestScore, candidates.Count, best);
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FingerprintService.Metrics;

/// <summary>
/// In-process metrics for fingerprint identification. Exposed via
/// System.Diagnostics.Metrics (Meter) — scrape with OpenTelemetry or dotnet-counters.
/// No external dependency required.
/// </summary>
public sealed class FingerprintMetrics
{
    public const string MeterName = "FingerprintService";

    private readonly Histogram<double> _durationMs;
    private readonly Histogram<int> _candidateCount;
    private readonly Histogram<double> _score;
    private readonly Counter<long> _identificationsTotal;

    public FingerprintMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _durationMs = meter.CreateHistogram<double>(
            "identification_duration_ms",
            unit: "ms",
            description: "End-to-end identification duration including DB fetch and matching.");

        _candidateCount = meter.CreateHistogram<int>(
            "identification_candidate_count",
            description: "Number of enrolled candidates scored per identify request.");

        _score = meter.CreateHistogram<double>(
            "identification_score",
            description: "Best match score per identify request (SourceAFIS).");

        _identificationsTotal = meter.CreateCounter<long>(
            "identifications_total",
            description: "Total identify requests by result.");
    }

    public void RecordIdentification(int candidateCount, double bestScore, double durationMs, bool matched)
    {
        _candidateCount.Record(candidateCount);
        _score.Record(Math.Round(bestScore, 2));
        _durationMs.Record(durationMs);
        _identificationsTotal.Add(1,
            new TagList { { "matched", matched.ToString().ToLowerInvariant() } });
    }
}

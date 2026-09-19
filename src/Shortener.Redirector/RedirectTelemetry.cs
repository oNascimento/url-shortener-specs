using System.Diagnostics.Metrics;
using Shortener.Application;
using Shortener.ServiceDefaults;

namespace Shortener.Redirector;

public static class RedirectTelemetry
{
    private static readonly Meter Meter = new(ServiceSetup.MeterName);
    private static readonly Counter<long> Eligible = Meter.CreateCounter<long>("shortener.redirect.eligible");
    private static readonly Counter<long> Resolutions = Meter.CreateCounter<long>("shortener.redirect.resolutions");
    private static readonly Counter<long> CaptureFailures = Meter.CreateCounter<long>("shortener.redirect.capture.failures");
    private static readonly Counter<long> Publications = Meter.CreateCounter<long>("shortener.redirect.publications");
    private static readonly Histogram<double> HttpDuration = Meter.CreateHistogram<double>("shortener.redirect.duration", "s");
    private static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>("shortener.redirect.publish.duration", "s");
    private static readonly KeyValuePair<string, object?> Service = new("service", "shortener-redirector");

    public static void EligibleGet() => Emit(() => Eligible.Add(1, Service));
    public static void CaptureFailed(string errorClass) => Emit(() =>
        CaptureFailures.Add(1, Service, new("error_class", errorClass)));

    public static void Resolved(int status, double seconds) => Emit(() =>
    {
        var result = status switch
        {
            302 => "resolved", 404 => "not_found", 410 => "unavailable",
            405 => "method_rejected", 503 => "read_failed", _ => "cancelled"
        };
        Resolutions.Add(1, Service, new("result", result));
        HttpDuration.Record(seconds, Service, new("result", result));
    });

    public static void Published(PublishOutcome outcome, double seconds) => Emit(() =>
    {
        var result = outcome switch
        {
            PublishOutcome.Confirmed => "confirmed", PublishOutcome.Rejected => "rejected", _ => "unknown"
        };
        Publications.Add(1, Service, new("result", result));
        PublishDuration.Record(seconds, Service, new("result", result));
    });

    private static void Emit(Action record)
    {
        try { record(); }
        catch (Exception) { /* A failing telemetry listener cannot change the public response. */ }
    }
}

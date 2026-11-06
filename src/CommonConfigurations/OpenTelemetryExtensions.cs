using NServiceBus.Configuration.AdvancedExtensibility;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CommonConfigurations;

static class OpenTelemetryExtensions
{
    public static void EnableOpenTelemetryMetrics(this EndpointConfiguration endpointConfiguration)
    {
        var meterProviderBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(CreateResourceBuilder(endpointConfiguration))
            .AddMeter("NServiceBus.Core.Pipeline.Incoming")
            .AddMeter("LoanBroker")
            .AddOtlpExporter(CollectorExporterName, cfg =>
            {
                var url = Environment.GetEnvironmentVariable(OtlpMetricsUrlEnvVar) ?? OtlpMetricsDefaultUrl;
                cfg.Endpoint = new Uri(url);
                cfg.Protocol = OtlpExportProtocol.HttpProtobuf;
            });

        if (IsRunningInAspire)
        {
            meterProviderBuilder.AddOtlpExporter(AspireExporterName, _ => { });
        }

        meterProviderBuilder.Build();
    }

    public static void EnableOpenTelemetryTracing(this EndpointConfiguration endpointConfiguration)
    {
        var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(CreateResourceBuilder(endpointConfiguration))
            .AddSource("NServiceBus.Core")
            .AddOtlpExporter(CollectorExporterName, cfg =>
            {
                var url = Environment.GetEnvironmentVariable(OtlpTracesUrlEnvVar) ?? OtlpTracesDefaultUrl;
                cfg.Endpoint = new Uri(url);
                cfg.Protocol = OtlpExportProtocol.HttpProtobuf;
            });

        if (IsRunningInAspire)
        {
            tracerProviderBuilder.AddOtlpExporter(AspireExporterName, _ => { });
        }

        tracerProviderBuilder.Build();
    }

    static ResourceBuilder CreateResourceBuilder(EndpointConfiguration endpointConfiguration)
    {
        var attributes = new Dictionary<string, object>
        {
            ["service.name"] = endpointConfiguration.GetSettings().EndpointName(),
            ["service.instance.id"] = Guid.NewGuid().ToString(),
        };

        return ResourceBuilder.CreateDefault().AddAttributes(attributes);
    }

    // The Aspire AppHost sets OTEL_EXPORTER_OTLP_ENDPOINT (plus protocol and headers) to point at the Aspire
    // dashboard. The Aspire exporter is configured entirely from those variables. It needs its own name,
    // otherwise it shares options with the collector exporter and also sends to the collector.
    static bool IsRunningInAspire =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    const string CollectorExporterName = "collector";
    const string AspireExporterName = "aspire";
    const string OtlpMetricsDefaultUrl = "http://localhost:5318/v1/metrics";
    const string OtlpTracesDefaultUrl = "http://localhost:5318/v1/traces";
    const string OtlpMetricsUrlEnvVar = "OTLP_METRICS_URL";
    const string OtlpTracesUrlEnvVar = "OTLP_TRACING_URL";
}

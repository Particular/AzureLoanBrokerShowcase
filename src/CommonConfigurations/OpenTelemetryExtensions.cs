﻿using NServiceBus.Configuration.AdvancedExtensibility;
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
        var endpointName = endpointConfiguration.GetSettings().EndpointName();

        var attributes = new Dictionary<string, object>
        {
            ["service.name"] = endpointName,
            ["service.instance.id"] = Guid.NewGuid().ToString(),
        };

        var resourceBuilder = ResourceBuilder.CreateDefault().AddAttributes(attributes);

        var meterProviderBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter("NServiceBus.Core.Pipeline.Incoming")
            .AddMeter("LoanBroker")
            .AddOtlpExporter(cfg =>
            {
                var url = Environment.GetEnvironmentVariable(OtlpMetricsUrlEnvVar) ?? OtlpMetricsDefaultUrl;
                cfg.Endpoint = new Uri(url);
                cfg.Protocol = OtlpExportProtocol.HttpProtobuf;
            });

        // When orchestrated by Aspire, also export to the dashboard's OTLP endpoint so traces and
        // metrics show up in the Aspire dashboard.
        if (HasAspireOtlpEndpoint())
        {
            meterProviderBuilder.AddOtlpExporter();
        }

        meterProviderBuilder.Build();
    }

    public static void EnableOpenTelemetryTracing(this EndpointConfiguration endpointConfiguration)
    {
        var endpointName = endpointConfiguration.GetSettings().EndpointName();

        var attributes = new Dictionary<string, object>
        {
            ["service.name"] = endpointName,
            ["service.instance.id"] = Guid.NewGuid().ToString(),
        };

        var resourceBuilder = ResourceBuilder.CreateDefault().AddAttributes(attributes);

        var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource("NServiceBus.Core")
            .AddOtlpExporter(cfg =>
            {
                var url = Environment.GetEnvironmentVariable(OtlpTracesUrlEnvVar) ?? OtlpTracesDefaultUrl;
                cfg.Endpoint = new Uri(url);
                cfg.Protocol = OtlpExportProtocol.HttpProtobuf;
            });

        // Also feed the Aspire dashboard when running under the AppHost (see metrics for details).
        if (HasAspireOtlpEndpoint())
        {
            tracerProviderBuilder.AddOtlpExporter();
        }

        tracerProviderBuilder.Build();
    }

    // Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT (plus protocol/headers) into project resources it
    // orchestrates. Its absence means we are not running under Aspire (e.g. plain Docker Compose),
    // so the dashboard exporter is skipped and only the collector pipeline is used.
    static bool HasAspireOtlpEndpoint() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    const string OtlpMetricsDefaultUrl = "http://localhost:5318/v1/metrics";
    const string OtlpTracesDefaultUrl = "http://localhost:5318/v1/traces";
    const string OtlpMetricsUrlEnvVar = "OTLP_METRICS_URL";
    const string OtlpTracesUrlEnvVar = "OTLP_TRACING_URL";
}
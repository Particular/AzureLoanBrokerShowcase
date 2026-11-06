using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Messaging: Azure Service Bus. The emulator's connection limits are too low for the full showcase,
// so a real namespace is required. The connection string is read from ConnectionStrings:transport
// (user secrets, environment, ...), or the Aspire dashboard prompts for it on first run.
var transport = builder.AddConnectionString("transport");

// Persistence: SQL Server container. Aspire creates the NServiceBus database on startup.
var sqlServer = builder.AddSqlServer("sqlserver")
    .WithDataVolume("loanbroker-sqlserver-data");

var nservicebusDb = sqlServer.AddDatabase("nservicebus", databaseName: "NServiceBus");

// The credit bureau is an Azure Functions app. It is built from its Dockerfile so that
// Azure Functions Core Tools don't need to be installed locally.
var creditBureau = builder.AddDockerfile("creditbureau", "../..", "src/CreditBureau/Dockerfile")
    .WithHttpEndpoint(port: 7071, targetPort: 80, name: "http");

// Observability stack used by the Grafana dashboards and Jaeger traces. Configuration files are shared
// with docker-compose.yml. Fixed host ports keep the README links working.
var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.102.1")
    .WithBindMount("../otel/otel-collector-config.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
    .WithHttpEndpoint(port: 5318, targetPort: 5318, name: "http")
    .WithEndpoint(port: 1234, targetPort: 1234, name: "prometheus", scheme: "http");

var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v2.53.2")
    .WithBindMount("../prometheus", "/etc/prometheus")
    .WithVolume("loanbroker-prometheus-data", "/prometheus")
    .WithArgs("--web.enable-lifecycle", "--config.file=/etc/prometheus/prometheus.yml")
    .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http")
    .WaitFor(otelCollector);

builder.AddContainer("grafana", "grafana/grafana-oss", "latest")
    .WithBindMount("../grafana/provisioning", "/etc/grafana/provisioning")
    .WithBindMount("../grafana/dashboards", "/var/lib/grafana/dashboards")
    .WithVolume("loanbroker-grafana-data", "/var/lib/grafana")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
    .WaitFor(prometheus);

builder.AddContainer("jaeger", "jaegertracing/all-in-one", "latest")
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
    .WithHttpEndpoint(port: 16686, targetPort: 16686, name: "http");

// Particular Service Platform: ServiceControl (error, audit, monitoring), RavenDB and ServicePulse.
// The error instance and ServicePulse are added explicitly to customize them; AddDefaultComponents
// reuses them and adds the remaining components.
var platform = builder
    .AddParticularPlatform("particular")
    .WithTransportAzureServiceBus(transport);

var platformPersistence = platform.AddPersistenceRavenDb("particular-persistence");

var serviceControl = platform.AddServiceControlErrorInstance("particular-error", platformPersistence)
    .WithEnvironment("SERVICECONTROL_ALLOWMESSAGEEDITING", "true");

var monitoring = platform.AddServiceControlMonitoringInstance("particular-monitoring");

platform.AddServicePulse("particular-servicepulse", serviceControl, monitoring)
    .WithEndpoint("http", endpoint => endpoint.Port = 9999);

platform.AddDefaultComponents();

// NServiceBus endpoints
var otelHttp = otelCollector.GetEndpoint("http");

IResourceBuilder<ProjectResource> AddEndpoint<TProject>(string name) where TProject : IProjectMetadata, new() =>
    builder.AddProject<TProject>(name)
        // Injects ConnectionStrings__transport and the Particular license
        .WithParticularPlatform(platform)
        // Injects ConnectionStrings__nservicebus
        .WithReference(nservicebusDb)
        .WithEnvironment("OTLP_METRICS_URL", ReferenceExpression.Create($"{otelHttp}/v1/metrics"))
        .WithEnvironment("OTLP_TRACING_URL", ReferenceExpression.Create($"{otelHttp}/v1/traces"))
        .WaitFor(nservicebusDb)
        .WaitFor(otelCollector)
        .WaitFor(platform);

var loanBroker = AddEndpoint<Projects.LoanBroker>("loan-broker")
    .WithEnvironment("CREDIT_BUREAU_URL", ReferenceExpression.Create($"{creditBureau.GetEndpoint("http")}/api/score"))
    .WaitFor(creditBureau);

AddEndpoint<Projects.Bank1Adapter>("bank1");
AddEndpoint<Projects.Bank2Adapter>("bank2");
AddEndpoint<Projects.Bank3Adapter>("bank3");
AddEndpoint<Projects.EmailSender>("email-sender");

AddEndpoint<Projects.Client>("client")
    .WithArgs("--demo")
    .WaitFor(loanBroker);

builder.Build().Run();

using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(enabled: true);

var serviceBusConnectionString = builder.AddParameter("azureServiceBusConnectionString", secret: true);
var transport = builder.AddConnectionString("transport", ReferenceExpression.Create($"{serviceBusConnectionString}"));

const string sqlConnectionString =
    "Server=sqlserver;Database=NServiceBus;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;";
const string creditBureauUrl = "http://creditbureau:80/api/score";
const string otlpMetricsUrl = "http://otel-collector:5318/v1/metrics";
const string otlpTracingUrl = "http://otel-collector:5318/v1/traces";

var sqlServer = builder.AddSqlServer("sqlserver")
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_SA_PASSWORD", "YourStrong@Passw0rd")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithEnvironment("MSSQL_COLLATION", "SQL_Latin1_General_CP1_CI_AS")
    .WithContainerRuntimeArgs("--platform", "linux/amd64")
    .WithContainerRuntimeArgs("--ulimit", "stack=8192:8192")
    .WithVolume("sqlserver-data", "/var/opt/mssql")
    .WithBindMount("../src/sqlserver/", "/tmp/scripts/")
    .AddDatabase("sqlserver-db","NServiceBus")
//    .WithArgs("bash", "-c", "/tmp/scripts/init-db.sh")
    ;

var creditBureau = builder.AddDockerfile(
        "creditbureau",
        "../..",
        dockerfilePath: "./src/CreditBureau/Dockerfile")
    .WithEndpoint(targetPort: 80, port: 7071, scheme: "http", name: "http", isExternal: true)
    .WaitFor(sqlServer);

var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.102.1")
    .WithBindMount("../otel/otel-collector-config.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
    .WithEndpoint(port: 4317, targetPort: 4317, name: "grpc", scheme: "tcp", isExternal: true)
    .WithEndpoint(targetPort: 5318, port: 5318, name: "http", scheme: "http", isExternal: true)
    .WithEndpoint(targetPort: 1234, port: 1234, name: "prometheus", scheme: "http", isExternal: true);

var prometheus = builder.AddContainer("prometheus", "docker.io/prom/prometheus", "v2.53.2")
    .WithBindMount("../prometheus", "/etc/prometheus")
    .WithVolume("prometheus-data", "/prometheus")
    .WithArgs("--web.enable-lifecycle", "--config.file=/etc/prometheus/prometheus.yml")
    .WithEndpoint(targetPort: 9090, port: 9090, scheme: "http", name: "http", isExternal: true)
    .WaitFor(otelCollector);

var grafana = builder.AddContainer("grafana", "docker.io/grafana/grafana-oss", "latest")
    .WithBindMount("../grafana/provisioning", "/etc/grafana/provisioning")
    .WithBindMount("../grafana/dashboards", "/var/lib/grafana/dashboards")
    .WithVolume("grafana-data", "/var/lib/grafana")
    .WithEndpoint(targetPort: 3000, port: 3000, scheme: "http", name: "http", isExternal: true)
    .WaitFor(prometheus);

var jaeger = builder.AddContainer("jaeger", "docker.io/jaegertracing/all-in-one", "latest")
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
    .WithVolume("jaeger-data", "/tmp")
    .WithEndpoint(targetPort: 16686, port: 16686, scheme: "http", name: "http", isExternal: true)
    .WaitFor(otelCollector);

var platform = builder
    .AddParticularPlatform("particular")
    .WithTransportAzureServiceBus(transport)
    .AddDefaultComponents();

var sharedServiceEnvironment = new (string Name, string Value)[]
{
    ("SQL_CONNECTION_STRING", sqlConnectionString),
    ("CREDIT_BUREAU_URL", creditBureauUrl),
    ("OTLP_METRICS_URL", otlpMetricsUrl),
    ("OTLP_TRACING_URL", otlpTracingUrl)
};

var loanBroker = builder.AddProject<Projects.LoanBroker>("loan-broker")
    .WithParticularPlatform(platform)
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(platform);

foreach (var (name, value) in sharedServiceEnvironment)
{
    loanBroker.WithEnvironment(name, value);
}

loanBroker.WaitFor(creditBureau);

var bank1 = builder.AddProject<Projects.Bank1Adapter>("bank1")
    .WithParticularPlatform(platform)
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(platform);

var bank2 = builder.AddProject<Projects.Bank2Adapter>("bank2")
    .WithParticularPlatform(platform)
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(platform);

var bank3 = builder.AddProject<Projects.Bank3Adapter>("bank3")
    .WithParticularPlatform(platform)
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(platform);

var emailSender = builder.AddProject<Projects.EmailSender>("email-sender")
    .WithParticularPlatform(platform)
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(platform);

var client = builder.AddProject<Projects.Client>("client")
    .WithParticularPlatform(platform)
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WithArgs("--demo")
    .WaitFor(platform)
    .WaitFor(loanBroker);

foreach (var endpoint in new[] { bank1, bank2, bank3, emailSender, client })
{
    foreach (var (name, value) in sharedServiceEnvironment)
    {
        endpoint.WithEnvironment(name, value);
    }
}

builder.Build().Run();

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(enabled: true);

var serviceBusConnectionString = builder.AddParameter("azureServiceBusConnectionString", secret: true);
var particularSoftwareLicense = builder.AddParameter("particularSoftwareLicense", secret: true);

const string sqlConnectionString =
    "Server=sqlserver;Database=NServiceBus;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;";
const string creditBureauUrl = "http://creditbureau:80/api/score";
const string otlpMetricsUrl = "http://otel-collector:5318/v1/metrics";
const string otlpTracingUrl = "http://otel-collector:5318/v1/traces";

var sqlServer = builder.AddContainer("sqlserver", "mcr.microsoft.com/mssql/server", "2025-latest")
    .WithEnvironment("ACCEPT_EULA", "Y")
    .WithEnvironment("MSSQL_SA_PASSWORD", "YourStrong@Passw0rd")
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithEnvironment("MSSQL_COLLATION", "SQL_Latin1_General_CP1_CI_AS")
    .WithBindMount("../src/sqlserver/init-db.sh", "/var/opt/mssql/init-db.sh")
    .WithVolume("sqlserver-data", "/var/opt/mssql")
    .WithArgs("bash", "-c", "/var/opt/mssql/init-db.sh")
    .WithEndpoint(port: 1433, targetPort: 1433, name: "sql", scheme: "tcp", isExternal: true);

var creditBureau = builder.AddDockerfile(
        "creditbureau",
        "..",
        dockerfilePath: "./src/CreditBureau/Dockerfile")
    .WithEndpoint(targetPort: 80, port: 7071, scheme: "http", name: "http", isExternal: true)
    .WaitFor(sqlServer);

var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.102.1")
    .WithBindMount("../src/otel/otel-collector-config.yaml", "/etc/otelcol-contrib/config.yaml", isReadOnly: true)
    .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
    .WithEndpoint(port: 4317, targetPort: 4317, name: "grpc", scheme: "tcp", isExternal: true)
    .WithEndpoint(targetPort: 5318, port: 5318, name: "http", scheme: "http", isExternal: true)
    .WithEndpoint(targetPort: 1234, port: 1234, name: "prometheus", scheme: "http", isExternal: true);

var prometheus = builder.AddContainer("prometheus", "docker.io/prom/prometheus", "v2.53.2")
    .WithBindMount("../src/prometheus", "/etc/prometheus")
    .WithVolume("prometheus-data", "/prometheus")
    .WithArgs("--web.enable-lifecycle", "--config.file=/etc/prometheus/prometheus.yml")
    .WithEndpoint(targetPort: 9090, port: 9090, scheme: "http", name: "http", isExternal: true)
    .WaitFor(otelCollector);

var grafana = builder.AddContainer("grafana", "docker.io/grafana/grafana-oss", "latest")
    .WithBindMount("../src/grafana/provisioning", "/etc/grafana/provisioning")
    .WithBindMount("../src/grafana/dashboards", "/var/lib/grafana/dashboards")
    .WithVolume("grafana-data", "/var/lib/grafana")
    .WithEndpoint(targetPort: 3000, port: 3000, scheme: "http", name: "http", isExternal: true)
    .WaitFor(prometheus);

var jaeger = builder.AddContainer("jaeger", "docker.io/jaegertracing/all-in-one", "latest")
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true")
    .WithVolume("jaeger-data", "/tmp")
    .WithEndpoint(targetPort: 16686, port: 16686, scheme: "http", name: "http", isExternal: true)
    .WaitFor(otelCollector);

var serviceControlDb = builder.AddContainer("servicecontrol-db", "docker.io/particular/servicecontrol-ravendb", "latest")
    .WithEnvironment("RAVEN_ARGS", "--Setup.Mode=None")
    .WithEnvironment("RAVEN_Security_UnsecuredAccessAllowed", "PublicNetwork")
    .WithVolume("servicecontrol-db", "/var/lib/ravendb/data")
    .WithVolume("servicecontrol-db-config", "/var/lib/ravendb/config")
    .WithEndpoint(targetPort: 8080, port: 8080, scheme: "http", name: "http", isExternal: true);

var serviceControl = builder.AddContainer("servicecontrol", "docker.io/particular/servicecontrol", "latest")
    .WithEnvironment("TRANSPORTTYPE", "NetStandardAzureServiceBus")
    .WithEnvironment("PARTICULARSOFTWARE_LICENSE", particularSoftwareLicense)
    .WithEnvironment("RAVENDB_CONNECTIONSTRING", "http://servicecontrol-db:8080")
    .WithEnvironment("REMOTEINSTANCES", "[{\"api_uri\":\"http://servicecontrol-audit:44444/api\"}]")
    .WithEnvironment("SERVICECONTROL_CONNECTIONSTRING", serviceBusConnectionString)
    .WithEnvironment("SERVICECONTROL_ALLOWMESSAGEEDITING", "true")
    .WithArgs("--setup-and-run")
    .WithEndpoint(targetPort: 33333, port: 33333, scheme: "http", name: "http", isExternal: true)
    .WaitFor(serviceControlDb);

var serviceControlAudit = builder.AddContainer("servicecontrol-audit", "docker.io/particular/servicecontrol-audit", "latest")
    .WithEnvironment("TRANSPORTTYPE", "NetStandardAzureServiceBus")
    .WithEnvironment("PARTICULARSOFTWARE_LICENSE", particularSoftwareLicense)
    .WithEnvironment("RAVENDB_CONNECTIONSTRING", "http://servicecontrol-db:8080")
    .WithEnvironment("SERVICECONTROL_AUDIT_CONNECTIONSTRING", serviceBusConnectionString)
    .WithEnvironment("SERVICECONTROL_AUDIT_SERVICECONTROLQUEUEADDRESS", "Particular.ServiceControl")
    .WithArgs("--setup-and-run")
    .WithEndpoint(targetPort: 44444, port: 44444, scheme: "http", name: "http", isExternal: true)
    .WaitFor(serviceControlDb);

var serviceControlMonitoring = builder
    .AddContainer("servicecontrol-monitoring", "docker.io/particular/servicecontrol-monitoring", "latest")
    .WithEnvironment("TRANSPORTTYPE", "NetStandardAzureServiceBus")
    .WithEnvironment("PARTICULARSOFTWARE_LICENSE", particularSoftwareLicense)
    .WithEnvironment("MONITORING_CONNECTIONSTRING", serviceBusConnectionString)
    .WithArgs("--setup-and-run")
    .WithEndpoint(targetPort: 33633, port: 33633, scheme: "http", name: "http", isExternal: true)
    .WaitFor(serviceControlDb);

var servicePulse = builder.AddContainer("servicepulse", "docker.io/particular/servicepulse", "latest")
    .WithEnvironment("SERVICECONTROL_URL", "http://servicecontrol:33333")
    .WithEnvironment("MONITORING_URL", "http://servicecontrol-monitoring:33633")
    .WithEndpoint(targetPort: 9090, port: 9999, scheme: "http", name: "http", isExternal: true)
    .WaitFor(serviceControl)
    .WaitFor(serviceControlMonitoring);

var sharedServiceEnvironment = new (string Name, string Value)[]
{
    ("SQL_CONNECTION_STRING", sqlConnectionString),
    ("CREDIT_BUREAU_URL", creditBureauUrl),
    ("OTLP_METRICS_URL", otlpMetricsUrl),
    ("OTLP_TRACING_URL", otlpTracingUrl)
};

var loanBroker = builder.AddProject<Projects.LoanBroker>("loan-broker")
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString);

foreach (var (name, value) in sharedServiceEnvironment)
{
    loanBroker.WithEnvironment(name, value);
}

loanBroker.WaitFor(creditBureau).WaitFor(servicePulse);

var bank1 = builder.AddProject<Projects.Bank1Adapter>("bank1")
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(servicePulse);

var bank2 = builder.AddProject<Projects.Bank2Adapter>("bank2")
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(servicePulse);

var bank3 = builder.AddProject<Projects.Bank3Adapter>("bank3")
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(servicePulse);

var emailSender = builder.AddProject<Projects.EmailSender>("email-sender")
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WaitFor(servicePulse);

var client = builder.AddProject<Projects.Client>("client")
    .WithEnvironment("AZURE_SERVICE_BUS_CONNECTION_STRING", serviceBusConnectionString)
    .WithArgs("--demo")
    .WaitFor(servicePulse)
    .WaitFor(loanBroker);

foreach (var endpoint in new[] { bank1, bank2, bank3, emailSender, client })
{
    foreach (var (name, value) in sharedServiceEnvironment)
    {
        endpoint.WithEnvironment(name, value);
    }
}

builder.Build().Run();

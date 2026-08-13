using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(enabled: true);

var serviceBusConnectionString = builder.AddParameter("azureServiceBusConnectionString", secret: true);
var transport = builder.AddConnectionString("transport", ReferenceExpression.Create($"{serviceBusConnectionString}"));

var sqlPassword = builder.AddParameter("sql-password", "YourStrong@Passw0rd");
var sqlServer = builder.AddSqlServer("sqlserver", password: sqlPassword)
    .WithEnvironment("MSSQL_PID", "Developer")
    .WithEnvironment("MSSQL_COLLATION", "SQL_Latin1_General_CP1_CI_AS")
    .WithVolume("sqlserver-data", "/var/opt/mssql");

var nsbDatabase = sqlServer.AddDatabase("nsb-database", "NServiceBus");

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

var otelHttp = otelCollector.GetEndpoint("http");
var creditBureauHttp = creditBureau.GetEndpoint("http");

IResourceBuilder<ProjectResource> ConfigureEndpoint(IResourceBuilder<ProjectResource> endpoint) =>
    endpoint
        .WithParticularPlatform(platform)
        .WithEnvironment(context =>
        {
            context.EnvironmentVariables["SQL_CONNECTION_STRING"] = nsbDatabase.Resource.ConnectionStringExpression;
            context.EnvironmentVariables["CREDIT_BUREAU_URL"] = ReferenceExpression.Create($"{creditBureauHttp}/api/score");
            context.EnvironmentVariables["OTLP_METRICS_URL"] = ReferenceExpression.Create($"{otelHttp}/v1/metrics");
            context.EnvironmentVariables["OTLP_TRACING_URL"] = ReferenceExpression.Create($"{otelHttp}/v1/traces");
        })
        .WaitFor(sqlServer)
        .WaitFor(otelCollector)
        .WaitFor(platform);

var loanBroker = ConfigureEndpoint(builder.AddProject<Projects.LoanBroker>("loan-broker"))
    .WaitFor(creditBureau);

ConfigureEndpoint(builder.AddProject<Projects.Bank1Adapter>("bank1"));
ConfigureEndpoint(builder.AddProject<Projects.Bank2Adapter>("bank2"));
ConfigureEndpoint(builder.AddProject<Projects.Bank3Adapter>("bank3"));
ConfigureEndpoint(builder.AddProject<Projects.EmailSender>("email-sender"));

ConfigureEndpoint(builder.AddProject<Projects.Client>("client"))
    .WithArgs("--demo")
    .WaitFor(loanBroker);

builder.Build().Run();

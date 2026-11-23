# Azure LoanBroker Showcase

The Azure LoanBroker showcase is a comprehensive loan broker implementation following the [structure presented](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ComposedMessagingExample.html) by [Gregor Hohpe](https://www.enterpriseintegrationpatterns.com/gregor.html) in his [Enterprise Integration Pattern](https://www.enterpriseintegrationpatterns.com/) book.

> [!Note]
> The showcase runs locally using Docker; however, due to connection limitations with the Azure Service Bus Emulator, an Azure account is needed for the default setup. The [how to run the example](#how-to-run-the-example) section details how to configure the solution to connect to Azure services. A separate Docker Compose file is available for running the solution using the Azure Service Bus Emulator, but this is a cut down version of the showcase that does not demonstrate the full functionality of NServiceBus.

This is the logical architecture:

![Logical architecture](https://github.com/user-attachments/assets/5f2ef8b0-c7d4-4cef-ab7b-a8561cbd3282)

And this is how that is represented using Azure services:

![Azure Architectural Diagram](img/architecture-view.png)

## What's in the box

The example is composed of:

- A client application, sending loan requests.
- A credit bureau providing the customers' credit score.
- A loan broker service that receives loan requests, enriches them with credit scores and orchestrates communication with downstream banks.
- Three bank adapters, acting like Anti-Corruption layers (ACL), simulate communication with downstream banks offering loans.
- An email sender simulating email communication with customers.

The example also ships the following monitoring services:

- The Particular platform to monitor endpoints, capture and visualize audit messages, and manage failed messages.
- A Prometheus instance to collect, store, and query raw metrics data.
- A Grafana instance with three different metrics dashboards using Prometheus as the data source.
- A Jaeger instance to visualize OpenTelemetry traces.
- OpenTelemetry collector to collect and export metrics and traces to various destinations.

## Requirements

- .NET 10 SDK
- Docker
- Docker Compose

## How to run the example

The simplest way to run the example is using Docker for both the endpoints and the infrastructure.
The client application, the loan broker service, the e-mail sender, and the bank adapters can be deployed as Docker containers alongside the Particular platform to monitor the system, Azure Service Bus for messaging, SQL Server for persistence, and the additional containers needed for enabling OpenTelemetry observability.

Before running the complete example in Docker, create a local `.env` file from `.env.example` in the root folder and set `AZURE_SERVICE_BUS_CONNECTION_STRING` with your Azure Service Bus connection string.

> [!NOTE]
> The `.env` file is local only and should not be committed.

The initial run will pull several containers required to run the demo. See [Ports](#ports) for the host ports these containers expose — make sure they are free before starting.

To run the complete example in Docker, publish the endpoint container images and start the Compose environment from the root folder:

```shell
dotnet publish src/AzureLoanBrokerShowcase.slnx -c Release --os linux /t:PublishContainer -m:1
docker compose up -d
```

> [!TIP]
> Once the project is running, check out the [Things to try](#things-to-try) section.

The `dotnet publish` command builds projects and publishes Linux container images to the local Docker registry using the .NET SDK built-in container support. The Docker Compose command starts those endpoint images and configures all the additional infrastructural containers.

To stop the running solution and remove all deployed containers, execute the following command from a command prompt:

```shell
docker compose down
```

To run the solution without rebuilding container images, execute the following command:

```shell
docker compose up -d
```

#### Troubleshooting on Windows

- If `dotnet publish` fails, delete the `%TEMP%\Containers\ContentStore` folder and run the command again.
  ```shell
  dotnet publish src/AzureLoanBrokerShowcase.slnx -c Release --os linux /t:PublishContainer -m:1
  docker compose up -d
  ```

- If `docker compose up` fails with `ports are not available` and `An attempt was made to access a socket in a way forbidden by its access permissions` error, it could be that Windows has reserved the port, even though no application is using it. List the reserved ranges with:

  ```shell
  netsh interface ipv4 show excludedportrange protocol=tcp
  ```

  To use a different host port, create a `docker-compose.override.yml` file in the root folder. Docker Compose applies it automatically. For example, to expose Prometheus on port 19090 instead of 9090:

  ```yaml
  services:
    prometheus:
      ports: !override
        - '19090:9090'
  ```

  Do not commit the override file.

### Running with Azure Service Bus Emulator

To run the cut down version of the showcase with the Azure Service Bus Emulator, execute the following command from the root folder:

```shell
dotnet publish src/AzureLoanBrokerShowcase.slnx -c Release --os linux /t:PublishContainer -m:1
docker compose -f docker-compose-ASB-emulator.yml up -d
```

To stop the emulator-based solution and remove all deployed containers, execute the following command:

```shell
docker compose -f docker-compose-ASB-emulator.yml down
```

> [!NOTE]
> The emulator-based setup is a cut down version of the showcase and excludes Bank2 due to the Azure Service Bus Emulator connection limits. Microsoft has confirmed there are currently no plans to increase these limits: <https://github.com/Azure/azure-service-bus-emulator-installer/issues/58#issuecomment-2984760245>

### Running endpoints from the IDE

If you prefer to start the endpoints from your IDE to debug the code, the infrastructure (SQL Server, credit bureau, Particular platform, and observability containers) runs in Docker while the endpoints run on your machine.

**One-time setup**

You need to provide your Azure Service Bus connection string in two places:

- Put it in the `.env` file in the root folder (create it from `.env.example`). Docker Compose uses it for the infrastructure containers, because ServiceControl connects to the same Azure Service Bus namespace.
- Set it as a user-level environment variable named `AZURE_SERVICE_BUS_CONNECTION_STRING`, then restart your IDE so it picks up the change. The endpoints you start from the IDE read it from there. Do not put it in `launchSettings.json`, because that file is committed to source control.

  ```powershell
  [Environment]::SetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING", "<your connection string>", "User")
  ```

You also need to add the connection details for the SQL Server and credit bureau containers to the launch profiles. The `.env` and `env/*.env` files are only used by the Docker containers, and their host names (for example, `sqlserver` and `creditbureau`) do not resolve from your machine. In the `Properties/launchSettings.json` of each endpoint project (Client, LoanBroker, Bank1Adapter, Bank2Adapter, Bank3Adapter, and EmailSender), add `SQL_CONNECTION_STRING` under `environmentVariables`. In the LoanBroker project, also add `CREDIT_BUREAU_URL`:

```json
"environmentVariables": {
  "DOTNET_ENVIRONMENT": "Development",
  "SQL_CONNECTION_STRING": "Server=localhost,1433;Database=NServiceBus;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;",
  "CREDIT_BUREAU_URL": "http://localhost:7071/api/score"
}
```

The password is the demo `sa` password that the SQL Server container is created with in `docker-compose.yml`. Only LoanBroker reads `CREDIT_BUREAU_URL`.

**Run the endpoints**

1. Start the infrastructure from a command prompt in the root directory and wait for the containers to become healthy:

   ```shell
   docker compose --profile infrastructure up -d
   ```

   This starts the following containers, but not the endpoint containers (Client, LoanBroker, Bank1/2/3, EmailSender):

   | Container | Purpose | Host port |
   |-----------|---------|-----------|
   | `sqlserver` | SQL Server for NServiceBus persistence. Creates the `NServiceBus` database. | 1433 |
   | `creditbureau` | Credit bureau service (Azure Functions) called by LoanBroker | 7071 |
   | `servicecontrol` | Particular platform: ServiceControl primary instance | 33333 |
   | `servicecontrol-db` | RavenDB database for ServiceControl | 8080 |
   | `servicecontrol-audit` | Particular platform: ServiceControl Audit instance | 44444 |
   | `servicecontrol-monitoring` | Particular platform: ServiceControl Monitoring instance | 33633 |
   | `servicepulse` | Particular platform: ServicePulse web UI | 9999 |
   | `otel-collector` | OpenTelemetry Collector that receives the endpoints' metrics and traces | 4317, 5318, 1234 |
   | `prometheus` | Metrics storage | 19090 |
   | `grafana` | Dashboards | 3000 |
   | `jaeger` | Distributed tracing UI | 16686 |

2. Start the Client, LoanBroker, Bank1Adapter, Bank2Adapter, Bank3Adapter, and EmailSender projects (for example, by configuring multiple startup projects in Visual Studio).

3. In the Client console window, press `F` to send a single loan request, or `L` to send one every second (`S` stops the repeat sends). Unlike the Docker setup, the Client does not send requests automatically when started from the IDE.

> [!IMPORTANT]
> Do not run the endpoint containers (`docker compose up -d`) at the same time as the endpoints in the IDE. Both would compete for messages from the same queues. Run `docker compose down` first if the full Docker setup is running.

The variables used by the endpoints and their values when running from the IDE are:

| Variable | Used by | IDE value |
|----------|---------|-----------|
| `AZURE_SERVICE_BUS_CONNECTION_STRING` | All endpoints | Your connection string (user environment variable, see one-time setup) |
| `SQL_CONNECTION_STRING` | All endpoints | `Server=localhost,1433;Database=NServiceBus;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;` (the SQL Server container) |
| `CREDIT_BUREAU_URL` | LoanBroker | `http://localhost:7071/api/score` (the credit bureau container) |
| `OTLP_METRICS_URL`, `OTLP_TRACING_URL` | All endpoints | Not required. They default to the OpenTelemetry collector on `localhost:5318`. |

The endpoints create their tables in the `NServiceBus` database on first start. The database itself is created by the SQL Server container.

To use a SQL Server instance installed on your machine instead of the container, create an empty `NServiceBus` database and set `SQL_CONNECTION_STRING` to, for example, `Server=localhost;Database=NServiceBus;Integrated Security=True;TrustServerCertificate=True;`. Remove the `SQL_CONNECTION_STRING` entries from the `launchSettings.json` files, because they take precedence over user environment variables.

Once the endpoints are running, open ServicePulse at <http://localhost:9999> to monitor them.

### Ports

The endpoint containers (Client, LoanBroker, Bank1/2/3, EmailSender) do not expose host ports — they communicate over Azure Service Bus. The infrastructure containers expose the following ports on the host. Make sure these are free before starting the demo.

#### Default setup (`docker-compose.yml`)

| Port  | Service                                       |
|-------|-----------------------------------------------|
| 1433  | SQL Server (NServiceBus persistence)          |
| 3000  | Grafana                                       |
| 4317  | OpenTelemetry Collector — OTLP gRPC           |
| 5318  | OpenTelemetry Collector — OTLP HTTP           |
| 1234  | OpenTelemetry Collector — Prometheus endpoint |
| 7071  | Credit Bureau (Azure Functions worker)        |
| 8080  | ServiceControl RavenDB Studio                 |
| 9090  | Prometheus                                    |
| 9999  | ServicePulse UI                               |
| 16686 | Jaeger UI                                     |
| 33333 | ServiceControl API                            |
| 33633 | ServiceControl Monitoring API                 |
| 44444 | ServiceControl Audit API                      |

#### Azure Service Bus Emulator setup (`docker-compose-ASB-emulator.yml`)

The emulator-based setup omits the Particular platform containers (ServiceControl, ServiceControl Audit, ServiceControl Monitoring, ServicePulse) and adds the emulator itself.

| Port  | Service                                       |
|-------|-----------------------------------------------|
| 1433  | SQL Server                                    |
| 3000  | Grafana                                       |
| 4317  | OpenTelemetry Collector — OTLP gRPC           |
| 5318  | OpenTelemetry Collector — OTLP HTTP           |
| 1234  | OpenTelemetry Collector — Prometheus endpoint |
| 5300  | Azure Service Bus Emulator (management)       |
| 5672  | Azure Service Bus Emulator (AMQP)             |
| 7071  | Credit Bureau                                 |
| 9090  | Prometheus                                    |
| 16686 | Jaeger UI                                     |

## Things to try

Once the project is running, here are some things to try. (Links are to `localhost` and will only work when the project is running.)

1. Explore some [traces in the Jaeger UI](http://localhost:16686/search?service=LoanBroker).
    - The green circles are traces where the entire flow completed successfully.
    - The red circles are traces that contain an exception at some point. (Bank3 fails 1/3 of the time.) Click into the failed steps and find the exception message and stack trace in the logs.
2. Check out a selection of [business metrics in Grafana](http://localhost:3000/d/edmhjobnxatc0c/loan-broker-demo?orgId=1&refresh=5s&from=now-15m&to=now&timezone=browser). (User `admin` and password `admin`.)
    - Some metrics are available for individual message types, even though the messages are processed from the same message queue.
    - Many more metrics are available by navigating to [Dashboards](http://localhost:3000/dashboards) and selecting a different dashboard.
3. Explore the [ServicePulse endpoint monitoring dashboard](http://localhost:9999/#/monitoring?historyPeriod=1), then navigate to [LoanBroker](http://localhost:9999/#/monitoring/endpoint/LoanBroker?historyPeriod=1) to see how these metrics are available for individual message types as well.
4. Investigate the EmailSender failures (the code is rigged to fail 5% of the time) in the [ServicePulse Failed Messages view](http://localhost:9999/#/failed-messages/failed-message-groups).
    - Navigate into the failed message group, then to an individual message.
    - Click on the tabs to see how the stack trace, message headers, and message body help a developer to troubleshoot and fix [systemic errors](https://particular.net/blog/but-all-my-errors-are-severe).
    - Return to the [failed message groups view](http://localhost:9999/#/failed-messages/failed-message-groups) and request a retry for the entire batch of failed messages.
    - The message handler will still fail 5% of the time. Click into the message group and see if there are any messages showing Retry Failures.

## Monitoring

The example comes with the [Particular platform](https://docs.particular.net/platform/), automatically available as Docker containers.

Monitoring information is available in [ServicePulse](http://localhost:9999).

## Telemetry

NServiceBus supports OpenTelemetry. Starting with NServiceBus 9.1, the following metrics are available:

- `nservicebus.messaging.successes` - Total number of messages processed successfully by the endpoint
- `nservicebus.messaging.fetches` - Total number of messages fetched from the queue by the endpoint
- `nservicebus.messaging.failures` - Total number of messages processed unsuccessfully by the endpoint
- `nservicebus.messaging.handler_time` - The time the user handling code takes to handle a message
- `nservicebus.messaging.processing_time` - The time the endpoint takes to process a message
- `nservicebus.messaging.critical_time` - The time between when a message is sent and when it is fully processed
- `nservicebus.recoverability.immediate` - Total number of immediate retries requested
- `nservicebus.recoverability.delayed` - Total number of delayed retries requested
- `nservicebus.recoverability.error` - Total number of messages sent to the error queue

For more information, refer to the [NServiceBus OpenTelemetry documentation](https://docs.particular.net/nservicebus/operations/opentelemetry).

All endpoints are configured to send OpenTelemetry traces to Jaeger. To visualize traces, open the [Jaeger dashboard](http://localhost:16686).

Similarly, endpoints send metrics to Prometheus. To visualize metrics, open the [Grafana dashboards](http://localhost:3000/dashboards). The default Grafana credentials are:

- Username: `admin`
- Password: `admin`

> [!NOTE]
> Setting a new password can be skipped. When containers are redeployed, the credentials are reset to their default values.

The example deploys three pre-configured Grafana dashboards:

- The [LoanBroker](http://localhost:3000/d/edmhjobnxatc0b/loanbroker?orgId=1&refresh=5s) dashboard shows various metrics about the business endpoints behavior, such as the differences between the services critical, processing, and handling time.
- The [Loan Broker Demo](http://localhost:3000/d/edmhjobnxatc0c/loan-broker-demo?orgId=1&refresh=5s) dashboard highlights a curated set of business metrics broken down by individual message type, useful for walking through the showcase.
- The [NServiceBus](http://localhost:3000/d/MHqYOIqnz/nservicebus?orgId=1&refresh=5s) dashboard shows the metrics related to message fetches, processing, and failures, grouped by endpoints or message type.

> [!NOTE]
> After running the solution multiple times, it might happen that Grafana suddenly shows random data instead of the expected metrics. To reset dashboards, tear down all containers, delete the Grafana and Prometheus Docker volumes, and redeploy the containers:
>
> ```shell
> docker compose down
> docker volume rm loanbroker_grafana-data loanbroker_prometheus-data
> docker compose up -d
> ```

# Aspire AppHost creation plan (Docker Compose parity)

## Goal

- [x] Add a new .NET Aspire AppHost that models the existing **default** `docker-compose.yml` topology (no ASB emulator flow).
- [x] Keep current behavior for local orchestration and publishing so container artifacts can be generated from AppHost for Docker Compose deployments.
- [x] Preserve the existing OpenTelemetry collector pipeline (Prometheus + Jaeger) while also enabling Aspire’s own telemetry/dashboard support.

## 1) Create AppHost and baseline wiring

- [x] Add a new project, e.g. `src/AppHost/AppHost.csproj`, and include it in `src/AzureLoanBrokerShowcase.slnx`.
- [x] Add required AppHost packages:
  - `Aspire.Hosting.AppHost`
  - `Aspire.Hosting.Docker` (for Docker Compose environment publishing)
- [x] In `AppHost/Program.cs`, create the builder with `DistributedApplication.CreateBuilder(args)`.
- [x] Add a Docker Compose compute environment in the app model (`AddDockerComposeEnvironment(...)`) so `aspire publish` can emit compose artifacts.

## 2) Model infrastructure from `docker-compose.yml`

- [x] Model the following as container resources in AppHost (same image intent as Compose):
  - `sqlserver`
  - `creditbureau`
  - `otel-collector`
  - `prometheus`
  - `grafana`
  - `jaeger`
  - `servicecontrol`
  - `servicecontrol-db`
  - `servicecontrol-audit`
  - `servicecontrol-monitoring`
  - `servicepulse`
- [x] Mirror key container settings from Compose:
  - ports
  - volumes/bind mounts
  - env vars and required secrets/placeholders
  - startup ordering/dependencies (where relevant)
- [x] Explicitly **exclude** ASB emulator services and related health-check sidecar from AppHost.

## 3) Model application services and references

- [x] Add project resources for:
  - `loan-broker`, `bank1`, `bank2`, `bank3`, `email-sender`, `client`
- [x] Keep image identity aligned with current publish flow (`loanbroker-azure/*`) so generated artifacts remain compatible with existing conventions.
- [x] Model `creditbureau` either:
  - as Dockerfile-based resource matching current `src/CreditBureau/Dockerfile`, or
  - as a project resource only if container publish parity is confirmed for the Azure Functions project.
- [x] Wire environment variables currently provided by `env/azure.env` + `env/metrics.env`:
  - `AZURE_SERVICE_BUS_CONNECTION_STRING`
  - `SQL_CONNECTION_STRING`
  - `CREDIT_BUREAU_URL`
  - `OTLP_METRICS_URL`
  - `OTLP_TRACING_URL`
- [x] Use AppHost parameters/secrets for externalized values (especially Service Bus connection string), so publish output contains resolvable placeholders instead of hard-coded secrets.

## 4) Telemetry coexistence plan (existing collector + Aspire telemetry)

- [x] Keep existing `otel-collector` container and its mounted config (`src/otel/otel-collector-config.yaml`).
- [x] Keep endpoint OTLP env vars pointing to collector (`http://otel-collector:5318/...`) so current NServiceBus/OpenTelemetry emission path is unchanged.
- [x] Enable Aspire dashboard support in the Docker Compose environment (do not replace existing collector/Prometheus/Grafana/Jaeger stack).
- [x] Confirm generated Compose output includes both:
  - existing observability containers, and
  - Aspire dashboard/telemetry resources (if enabled for that environment).

## 5) Publishing and deployment flow

- [x] Standardize on Aspire CLI publish flow for AppHost artifacts:
  - `aspire publish --apphost src/AppHost/AppHost.csproj`
- [x] Ensure published output includes Docker Compose artifacts (`docker-compose.yml`, `.env` placeholders, related generated files).
- [x] Keep ability to build/push images for project/container resources via Aspire Docker integration (including remote image naming/tag strategy when needed).
- [x] Ensure published artifact structure can replace today’s manual `dotnet publish ... /t:PublishContainer` + `docker compose up` workflow for containerized runs.

## 6) Documentation and operational updates

- [x] Update `README.md` with AppHost run/publish instructions (default scenario only; emulator path remains separate or explicitly out of scope).
- [x] Document required parameters/secrets and how to pass them during `aspire publish`.
- [x] Document expected host ports and any AppHost-specific differences from current Compose UX.

## 7) Suggested implementation order

- [x] Scaffold AppHost + Docker integration package.
- [x] Add infrastructure containers first (SQL/collector/monitoring/Particular platform).
- [x] Add application project resources and env wiring.
- [x] Add publish customization to preserve container naming and compose parity.
- [x] Update docs and perform parity validation against existing `docker-compose.yml`.

## References used

- [x] Aspire AppHost overview: <https://aspire.dev/get-started/app-host/>
- [x] Aspire deployment pipeline model: <https://aspire.dev/deployment/deploy-with-aspire/>
- [x] Aspire CLI publish: <https://aspire.dev/reference/cli/commands/aspire-publish/>
- [x] Aspire Docker integration (Compose generation/customization): <https://aspire.dev/integrations/compute/docker/>
- [x] Aspire Community Toolkit overview: <https://github.com/CommunityToolkit/Aspire>

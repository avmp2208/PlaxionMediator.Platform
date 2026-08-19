### Docker Compose Deployment

The root `docker-compose.yml` provides a full Postgres-backed stack for local development and validation.

#### Services
- **postgres**: PostgreSQL 16 database.
- **platform-migrate**: A one-shot container that runs database migrations.
- **platform-api**: The API service and Dashboard (port 8080).
- **platform-ingestion**: The OTLP ingestion service (port 8081).

#### Quick Start
1. Copy `.env.example` to `.env` and adjust the `POSTGRES_PASSWORD`.
2. Start the stack:
   ```bash
   docker compose up -d
   ```
3. The API will be available at `http://localhost:8080`.

#### Database Validation (PostgreSQL)
To verify the PostgreSQL storage implementation manually:
1. Start only the Postgres service:
   ```bash
   docker compose up -d postgres
   ```
2. Set the connection string environment variable:
   ```bash
   # PowerShell
   $env:ConnectionStrings__Platform = "Host=localhost;Database=plaxion;Username=plaxion;Password=plaxion_secret"
   ```
3. Run the conformance tests:
   ```bash
   dotnet test --filter FullyQualifiedName~PostgreSqlConformanceTests
   ```

*Note: During initial containerization, automated validation was skipped due to environment limitations, but `PostgreSqlConformanceTests.cs` has been added to the test suite for future use.*

#### Containerized OTLP Acceptance Flow
To verify end-to-end data flow using a sample OTel application:
1. Ensure the Plaxion stack is running: `docker compose up -d`.
2. Run a .NET application with the OTel SDK configured to export to `http://localhost:8081` (OTLP/HTTP).
   ```csharp
   // Example .NET configuration
   builder.Services.AddOpenTelemetry()
       .WithTracing(t => t
           .AddAspNetCoreInstrumentation()
           .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:8081/v1/traces")))
       .WithMetrics(m => m
           .AddAspNetCoreInstrumentation()
           .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:8081/v1/metrics")));
   ```
3. Generate traffic in the sample app.
4. Verify data appears in the Dashboard at `http://localhost:8080`:
   - Postgres stores the raw samples.
   - `Platform.Ingestion` receives OTLP and writes to Postgres.
   - `Platform.Aggregation` (part of Ingestion role) computes rollups.
   - `Platform.Api` serves the rollups to the Dashboard.

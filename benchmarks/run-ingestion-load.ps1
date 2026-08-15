<#
.SYNOPSIS
    Manual MVP performance baseline script for Doc B. NOT part of `dotnet test`
    - this is a throwaway/reusable local benchmark tool only, meant to be run
    by hand against a locally running Platform.Ingestion + Platform.Api.

.DESCRIPTION
    Sends N requests to a running Platform.Ingestion instance's bridging
    JSON POST /v1/ingest endpoint (exercises the same storage/service-catalog
    write path as POST /v1/metrics), measuring wall-clock throughput and
    per-request latency (p50/p95). Then issues one query call against
    Platform.Api's GET /v1/tenants/{tenantId}/services to record its latency.

    See docs/performance-baseline.md for how to run this and the last
    measured results.

.PARAMETER IngestionBaseUrl
    Base URL of the running Platform.Ingestion instance.

.PARAMETER ApiBaseUrl
    Base URL of the running Platform.Api instance.

.PARAMETER RequestCount
    Number of requests to send (Doc B MVP target: 200-500).
#>
param(
    [string]$IngestionBaseUrl = "http://localhost:5299",
    [string]$ApiBaseUrl = "http://localhost:5298",
    [int]$RequestCount = 300
)

Add-Type -AssemblyName System.Net.Http

# Uses the deterministic Development-only dev API key that both
# Platform.Ingestion and Platform.Api seed for DevTenant (see Program.cs in
# each project). This script is meant to be run against local Development
# instances only.
$devApiKey = "pk_dev0000000000000000000000000000000000000000000000000000dev0"

$httpClient = New-Object System.Net.Http.HttpClient
$httpClient.DefaultRequestHeaders.Add("X-Api-Key", $devApiKey)
$httpClient.Timeout = [TimeSpan]::FromSeconds(30)

$latenciesMs = New-Object System.Collections.Generic.List[double]
$errors = 0
$sw = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 0; $i -lt $RequestCount; $i++) {
    $body = @{
        resource = @{
            serviceName = "bench-service"
            environment = "bench"
        }
        metrics = @(
            @{
                metricName   = "bench.requests"
                value        = [double]$i
                timeUnixNano = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() * 1000000
            }
        )
    } | ConvertTo-Json -Depth 5

    $content = New-Object System.Net.Http.StringContent($body, [System.Text.Encoding]::UTF8, "application/json")
    $reqSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = $httpClient.PostAsync("$IngestionBaseUrl/v1/ingest", $content).GetAwaiter().GetResult()
        $reqSw.Stop()
        if (-not $response.IsSuccessStatusCode) {
            $errors++
        }
        $latenciesMs.Add($reqSw.Elapsed.TotalMilliseconds)
    }
    catch {
        $reqSw.Stop()
        $errors++
        $latenciesMs.Add($reqSw.Elapsed.TotalMilliseconds)
    }
}

$sw.Stop()
$sorted = $latenciesMs | Sort-Object
$p50 = $sorted[[int]([math]::Floor($sorted.Count * 0.50))]
$p95 = $sorted[[int]([math]::Floor($sorted.Count * 0.95) - 1)]
$throughput = $RequestCount / $sw.Elapsed.TotalSeconds

Write-Host "=== Platform.Ingestion POST /v1/ingest load ==="
Write-Host "Requests:        $RequestCount"
Write-Host "Errors:          $errors"
Write-Host "Total time (s):  $($sw.Elapsed.TotalSeconds)"
Write-Host "Throughput:      $([math]::Round($throughput, 1)) req/s"
Write-Host "p50 latency (ms): $([math]::Round($p50, 2))"
Write-Host "p95 latency (ms): $([math]::Round($p95, 2))"

# One representative query call against Platform.Api, timed separately.
# Uses the same dev-seeded DevTenant.Id that Platform.Api/Platform.Ingestion
# both seed a matching API key for in Development.
try {
    $apiClient = New-Object System.Net.Http.HttpClient
    $apiClient.DefaultRequestHeaders.Add("X-Api-Key", $devApiKey)
    $devTenantId = "00000000-0000-0000-0000-000000000001"
    $qsw = [System.Diagnostics.Stopwatch]::StartNew()
    $servicesResp = $apiClient.GetAsync("$ApiBaseUrl/v1/tenants/$devTenantId/services").GetAwaiter().GetResult()
    $qsw.Stop()
    Write-Host ""
    Write-Host "=== Platform.Api GET /v1/tenants/{id}/services ==="
    Write-Host "Status:          $($servicesResp.StatusCode)"
    Write-Host "Latency (ms):    $([math]::Round($qsw.Elapsed.TotalMilliseconds, 2))"
}
catch {
    Write-Host "Api query call failed: $_"
}

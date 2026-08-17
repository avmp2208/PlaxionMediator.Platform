namespace Platform.Api;

public sealed record ServiceResponse(Guid Id, string Name, string Environment);

public sealed record MetricRollupResponse(
    DateTimeOffset BucketStart,
    DateTimeOffset BucketEnd,
    string MetricName,
    int Count,
    double Min,
    double Max,
    double Avg,
    double P50,
    double P95,
    double P99,
    string? RequestType);

public sealed record RequestTypeRankingResponse(
    string RequestType,
    long ExecutionCount,
    double P95,
    double P99,
    double FailurePercent);

public sealed record PagedRequestTypeRankingResponse(
    IReadOnlyList<RequestTypeRankingResponse> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

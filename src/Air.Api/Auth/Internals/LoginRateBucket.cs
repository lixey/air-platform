namespace Platform.Api.Auth.Internals;

internal sealed record LoginRateBucket(int Failures, DateTimeOffset WindowStart);

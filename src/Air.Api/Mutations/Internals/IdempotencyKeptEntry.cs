namespace Platform.Api.Mutations.Internals;

internal sealed record IdempotencyKeptEntry(
  string ServiceKey,
  string PayloadHash,
  long RecordId,
  DateTimeOffset CreatedAt,
  object? CachedResult = null);

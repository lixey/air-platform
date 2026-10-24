namespace Platform.Data.Querying;

/// <summary>
/// Thrown during query planning when a client request violates query map capabilities or limits.
/// </summary>
public sealed class QueryRequestException(
  QueryError error,
  string message,
  string? field = null,
  string? jsonPath = null) : Exception(message)
{
  public QueryError Error { get; } = error;
  public string? Field { get; } = field;
  public string? JsonPath { get; } = jsonPath;
  public string Code => Error.ToCode();
}

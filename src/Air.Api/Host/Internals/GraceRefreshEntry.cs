namespace Platform.Api.Host.Internals;

internal sealed record GraceRefreshEntry(
  string PrevHash,
  string AccessToken,
  DateTimeOffset AccessExpiresAt,
  string RefreshToken,
  DateTimeOffset RefreshExpiresAt,
  DateTimeOffset GraceExpiresAt);

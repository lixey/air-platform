namespace Platform.Api.Auth.Internals;

internal sealed record GraceRefreshEntry(
  string PrevHash,
  string AccessToken,
  DateTimeOffset AccessExpiresAt,
  string RefreshToken,
  DateTimeOffset RefreshExpiresAt,
  DateTimeOffset GraceExpiresAt);

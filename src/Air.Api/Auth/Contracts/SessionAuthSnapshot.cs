namespace Platform.Api.Auth.Contracts;

/// <summary>
/// Immutable cached snapshot of an authentication session.
/// </summary>
public sealed record SessionAuthSnapshot(
  long Id,
  long UserId,
  long? TenantId,
  DateTimeOffset? RevokedAt,
  DateTimeOffset ExpiresAt
);

using Microsoft.EntityFrameworkCore;

namespace Air.Data.Entities;

/// <summary>
/// One login, on one device, until it expires or is revoked.
/// </summary>
[Comment("Base authentication session model")]
public abstract class BaseAppAuthSession : BaseEntity
{
  [Comment("Owning user identifier")]
  public long UserId { get; set; }

  [Comment("Active tenant identifier")]
  public long? TenantId { get; set; }

  [Comment("Sessions descended from one login share a family; reuse revokes the family")]
  public Guid FamilyId { get; set; }

  [Sensitive, Comment("SHA-256 of the current refresh token")]
  public string RefreshTokenHash { get; set; } = string.Empty;

  [Sensitive, Comment("SHA-256 of the refresh token retired by the last rotation")]
  public string? PreviousRefreshTokenHash { get; set; }

  [Comment("When the last rotation happened")]
  public DateTimeOffset? RotatedAt { get; set; }

  [Comment("Last time a token of this session was used")]
  public DateTimeOffset? LastUsedAt { get; set; }

  [Comment("Absolute expiry of the session")]
  public DateTimeOffset ExpiresAt { get; set; }

  [Comment("When the session was revoked, if it was")]
  public DateTimeOffset? RevokedAt { get; set; }

  [Comment("Why the session was revoked: self, all, password-change, expired, reuse")]
  public string? RevokedReason { get; set; }

  [Comment("Client description as sent by the user agent")]
  public string? Client { get; set; }

  [Comment("Client network address at login")]
  public string? Address { get; set; }

  public bool IsLive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}

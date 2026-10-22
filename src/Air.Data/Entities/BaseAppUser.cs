using System.Collections.Frozen;

namespace Air.Data.Entities;

[Comment("Base user entity model with security and session control")]
public abstract class BaseAppUser : BaseEntity
{
  internal static readonly FrozenSet<string> LoginCounters = new[]
  {
    nameof(LastLoggedIn), nameof(AccessFailedCount), nameof(LockoutCount), nameof(LockoutEnd)
  }.ToFrozenSet(StringComparer.Ordinal);

  [MaxLength(128), Comment("User unique login name, unique across the installation")]
  public string Login { get; set; } = string.Empty;

  [Sensitive, Comment("Versioned password hash")]
  public string PasswordHash { get; set; } = string.Empty;

  [MaxLength(256), Comment("User display name")]
  public string? Name { get; set; }

  [MaxLength(256), Comment("User email address, unique across the installation when present")]
  public string? Email { get; set; }

  [Comment("Token version; advancing it revokes every session")]
  public int TokenVersion { get; set; } = 1;

  [Comment("Consecutive failed login attempts since the last success")]
  public int AccessFailedCount { get; set; }

  [Comment("How many lockouts in a row; picks the progressive lockout step")]
  public int LockoutCount { get; set; }

  [Comment("Lockout end timestamp")]
  public DateTimeOffset? LockoutEnd { get; set; }

  [Comment("Last successful login timestamp")]
  public DateTimeOffset? LastLoggedIn { get; set; }

  [Comment("Active status flag")]
  public bool IsActive { get; set; } = true;

  [Comment("The root user: never deactivated, deleted or left without '*'")]
  public bool IsRoot { get; set; }

  [Comment("The user must change the password before doing anything else")]
  public bool MustChangePassword { get; set; }

  [Comment("Foreign key referencing preferred AppLanguage")]
  public long? LanguageId { get; set; }

  [MaxLength(TimeZones.MaxLength), Comment("IANA time zone id; null takes the organisation's")]
  public string? TimeZone { get; set; }

  [Comment("Navigation property to preferred AppLanguage"), ForeignKey(nameof(LanguageId))]
  public virtual AppLanguage? Language { get; set; }
}

namespace Platform.Data.Entities;

/// <summary>
/// A tenant: a row the code declares, read only in every screen, in a tree of any depth.
/// </summary>
public abstract class BaseAppTenant : BaseDictionaryEntity, IReadOnlyEntity
{
  [Comment("Parent tenant; null for a root")]
  public long? ParentId { get; set; }

  [MaxLength(TimeZones.MaxLength), Comment("IANA time zone id; null takes the parent's")]
  public string? TimeZone { get; set; }
}

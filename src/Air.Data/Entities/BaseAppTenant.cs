namespace Platform.Data.Entities;

/// <summary>
/// A tenant in a hierarchy of any depth, editable via the application's tenant form service.
/// </summary>
public abstract class BaseAppTenant : BaseDictionaryEntity
{
  [Comment("Parent tenant; null for a root")]
  public long? ParentId { get; set; }

  [MaxLength(TimeZones.MaxLength), Comment("IANA time zone id; null takes the parent's")]
  public string? TimeZone { get; set; }
}

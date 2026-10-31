using System.Collections.Frozen;

namespace Platform.Data.Internals;

internal sealed class EntityAuditMetadata
{
  private static readonly ConcurrentDictionary<Type, EntityAuditMetadata> Cache = new();

  public bool IsEntityAudited { get; }
  public FrozenSet<string> ExcludedProperties { get; }
  public FrozenSet<string> KeepsVersionProperties { get; }

  private EntityAuditMetadata(Type clrType)
  {
    IsEntityAudited = typeof(IEntity).IsAssignableFrom(clrType) &&
                      !typeof(AppPlatformChange).IsAssignableFrom(clrType) &&
                      !typeof(BaseAppAuthSession).IsAssignableFrom(clrType) &&
                      !Attribute.IsDefined(clrType, typeof(NoAuditAttribute), inherit: true);

    var excluded = new HashSet<string>(StringComparer.Ordinal)
    {
      nameof(IHasTimestamps.UpdatedAt)
    };
    var keepsVersion = new HashSet<string>(StringComparer.Ordinal);

    foreach (var prop in clrType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
    {
      var isSensitive = Attribute.IsDefined(prop, typeof(SensitiveAttribute), inherit: true);
      var isNoAudit = Attribute.IsDefined(prop, typeof(NoAuditAttribute), inherit: true);
      var isKeepsVersion = Attribute.IsDefined(prop, typeof(KeepsVersionAttribute), inherit: true);

      if (isSensitive || isNoAudit)
      {
        excluded.Add(prop.Name);
      }

      if (isKeepsVersion)
      {
        keepsVersion.Add(prop.Name);
      }
    }

    ExcludedProperties = excluded.ToFrozenSet(StringComparer.Ordinal);
    KeepsVersionProperties = keepsVersion.ToFrozenSet(StringComparer.Ordinal);
  }

  public static EntityAuditMetadata For(Type clrType) =>
    Cache.GetOrAdd(clrType, static t => new EntityAuditMetadata(t));
}

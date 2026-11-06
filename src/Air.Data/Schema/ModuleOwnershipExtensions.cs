using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Data.Schema;

/// <summary>
/// Resolves and validates per-module table ownership across <see cref="PlatformDbContext"/> inheritance chains
/// without requiring boilerplate annotations on every entity.
/// </summary>
public static class ModuleOwnershipExtensions
{
  internal const string ModuleAnnotation = "Platform:SchemaModule";
  private const string OwnerAnnotation = "Platform:TableOwner";
  private static readonly ConcurrentDictionary<Type, string?> HandleCache = new();
  private static readonly ConcurrentDictionary<Assembly, string?> AssemblyHandleCache = new();

  /// <summary>
  /// Explicitly overrides the owning module handle for <typeparamref name="TEntity"/>'s table.
  /// </summary>
  public static EntityTypeBuilder<TEntity> HasModuleOwner<TEntity>(this EntityTypeBuilder<TEntity> entity, string module)
    where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(entity);
    ArgumentException.ThrowIfNullOrWhiteSpace(module);
    var normalized = module.Trim();
    var previous = FindExplicitOwner(entity.Metadata);
    if (previous is not null && !string.Equals(previous, normalized, StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
        $"Entity '{entity.Metadata.Name}' has conflicting explicit table owners '{previous}' and '{normalized}'.");
    }

    entity.HasAnnotation(OwnerAnnotation, normalized);
    return entity;
  }

  /// <summary>
  /// Returns the module handle declared via <see cref="ModuleSchemaAttribute"/> directly on <paramref name="contextType"/>
  /// or on its declaring assembly.
  /// </summary>
  public static string? GetDeclaredModuleHandle(Type contextType)
  {
    ArgumentNullException.ThrowIfNull(contextType);
    return HandleCache.GetOrAdd(
      contextType,
      static t => t.GetCustomAttribute<ModuleSchemaAttribute>(inherit: false)?.Handle
        ?? t.Assembly.GetCustomAttribute<ModuleSchemaAttribute>()?.Handle);
  }

  internal static IReadOnlyList<(Type ContextType, string Handle, Assembly Assembly)> ResolveLevels(Type contextType)
  {
    var stack = new List<(Type ContextType, string Handle, Assembly Assembly)>();
    for (var current = contextType;
         current is not null && current != typeof(PlatformDbContext) && current != typeof(DbContext) && current != typeof(object);
         current = current.BaseType)
    {
      if (GetDeclaredModuleHandle(current) is { } handle && !string.IsNullOrWhiteSpace(handle))
      {
        stack.Add((current, handle, current.Assembly));
      }
    }

    stack.Reverse();
    return stack;
  }

  internal static Dictionary<(string? Schema, string Name), string> InferTableOwners(
    IReadOnlyModel model,
    Type contextType)
  {
    var levels = ResolveLevels(contextType);
    if (levels.Count == 0)
    {
      return [];
    }

    var levelDepth = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var i = 0; i < levels.Count; i++)
    {
      if (!levelDepth.TryAdd(levels[i].Handle, i))
      {
        throw new InvalidOperationException(
          $"Duplicate ModuleSchema handle '{levels[i].Handle}' in inheritance hierarchy of '{contextType.Name}'.");
      }
    }

    var entityOwners = new Dictionary<IReadOnlyEntityType, string>();
    foreach (var entity in model.GetEntityTypes())
    {
      if (entity.GetTableName() is null)
      {
        continue;
      }

      entityOwners[entity] = ResolveEntityOwner(entity, levels, levelDepth);
    }

    var tableOwners = new Dictionary<(string? Schema, string Name), string>();
    foreach (var group in model.GetEntityTypes()
               .Where(e => e.GetTableName() is not null)
               .GroupBy(e => (Schema: e.GetSchema(), Name: e.GetTableName()!)))
    {
      var distinctOwners = group
        .Select(e => entityOwners[e])
        .Distinct(StringComparer.Ordinal)
        .ToArray();

      if (distinctOwners.Length != 1)
      {
        throw new InvalidOperationException(
          $"Table '{group.Key.Schema ?? "public"}.{group.Key.Name}' in '{contextType.Name}' must have exactly one module owner, but found [{string.Join(", ", distinctOwners)}].");
      }

      tableOwners[group.Key] = distinctOwners[0];
    }

    // Validate cross-module FK direction: dependent module depth must be >= principal module depth
    foreach (var entity in model.GetEntityTypes())
    {
      if (entity.GetTableName() is not { } depTable)
      {
        continue;
      }

      var depOwner = tableOwners[(entity.GetSchema(), depTable)];
      if (!levelDepth.TryGetValue(depOwner, out var depDepth))
      {
        continue;
      }

      foreach (var fk in entity.GetForeignKeys())
      {
        if (fk.PrincipalEntityType.GetTableName() is not { } prinTable)
        {
          continue;
        }

        var prinOwner = tableOwners[(fk.PrincipalEntityType.GetSchema(), prinTable)];
        if (levelDepth.TryGetValue(prinOwner, out var prinDepth) && prinDepth > depDepth)
        {
          throw new InvalidOperationException(
            $"Invalid cross-module foreign key from '{depTable}' (module '{depOwner}') to '{prinTable}' (derived module '{prinOwner}'). Base modules cannot reference tables of derived modules.");
        }
      }
    }

    return tableOwners;
  }

  private static string ResolveEntityOwner(
    IReadOnlyEntityType entity,
    IReadOnlyList<(Type ContextType, string Handle, Assembly Assembly)> levels,
    Dictionary<string, int> levelDepth)
  {
    var explicitOwner = FindExplicitOwner(entity);
    if (explicitOwner is not null)
    {
      return explicitOwner;
    }

    if (entity.IsOwned() && entity.FindOwnership()?.PrincipalEntityType is { } principal)
    {
      return ResolveEntityOwner(principal, levels, levelDepth);
    }

    if (entity.HasSharedClrType)
    {
      var fkOwners = entity.GetForeignKeys()
        .Select(fk => ResolveEntityOwner(fk.PrincipalEntityType, levels, levelDepth))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

      if (fkOwners.Length > 0)
      {
        return fkOwners
          .OrderByDescending(o => levelDepth.GetValueOrDefault(o, -1))
          .First();
      }
    }

    var rootType = entity.GetRootType();
    var clrAssembly = rootType.ClrType.Assembly;

    if (clrAssembly == typeof(PlatformDbContext).Assembly)
    {
      return levels[0].Handle;
    }

    foreach (var level in levels)
    {
      if (level.Assembly == clrAssembly)
      {
        return level.Handle;
      }
    }

    if (GetAssemblyModuleHandle(clrAssembly) is { } externalModuleHandle)
    {
      return externalModuleHandle;
    }

    throw new InvalidOperationException(
      $"Entity '{entity.DisplayName()}' (table '{entity.GetTableName()}') in assembly '{clrAssembly.GetName().Name}' has no inferred module owner in '{levels[^1].ContextType.Name}'. Apply [ModuleSchema] to the module's DbContext or assembly, or configure .HasModuleOwner(...).");
  }

  internal static void ValidateExternalEntitiesHaveModuleOwner(IReadOnlyModel model, Type contextType)
  {
    foreach (var entity in model.GetEntityTypes())
    {
      if (entity.GetTableName() is null || entity.IsOwned() || entity.HasSharedClrType)
      {
        continue;
      }

      if (FindExplicitOwner(entity) is not null)
      {
        continue;
      }

      var clrAssembly = entity.GetRootType().ClrType.Assembly;
      if (clrAssembly == typeof(PlatformDbContext).Assembly)
      {
        continue;
      }

      if (GetAssemblyModuleHandle(clrAssembly) is null)
      {
        throw new InvalidOperationException(
          $"Entity '{entity.DisplayName()}' (table '{entity.GetTableName()}') in assembly '{clrAssembly.GetName().Name}' has no inferred module owner in '{contextType.Name}'. Apply [ModuleSchema] to the module's DbContext or assembly, or configure .HasModuleOwner(...).");
      }
    }
  }

  private static string? GetAssemblyModuleHandle(Assembly assembly) =>
    AssemblyHandleCache.GetOrAdd(assembly, static asm =>
    {
      if (asm.GetCustomAttribute<ModuleSchemaAttribute>()?.Handle is { } asmHandle &&
          !string.IsNullOrWhiteSpace(asmHandle))
      {
        return asmHandle;
      }

      Type[] types;
      try
      {
        types = asm.GetTypes();
      }
      catch (ReflectionTypeLoadException ex)
      {
        types = ex.Types.Where(t => t is not null).ToArray()!;
      }

      var handles = types
        .Where(t => typeof(PlatformDbContext).IsAssignableFrom(t))
        .Select(t => t.GetCustomAttribute<ModuleSchemaAttribute>(inherit: false)?.Handle)
        .Where(h => !string.IsNullOrWhiteSpace(h))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
      return handles.Length == 1 ? handles[0] : null;
    });

  private static string? FindExplicitOwner(IReadOnlyEntityType? entity)
  {
    while (entity is not null)
    {
      if (entity.FindAnnotation(OwnerAnnotation)?.Value is string owner && !string.IsNullOrWhiteSpace(owner))
      {
        return owner;
      }

      entity = entity.BaseType;
    }

    return null;
  }
}

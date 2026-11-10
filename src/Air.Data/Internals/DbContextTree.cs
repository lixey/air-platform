namespace Platform.Data.Internals;

internal sealed class DbContextTree
{
  private readonly Dictionary<Type, (Func<IServiceProvider, PlatformDbContext> Factory, bool Owned)> _factories = [];
  private readonly List<Type> _registeredTypes = [];

  public void Register(Type contextType, Func<IServiceProvider, PlatformDbContext> factory, bool owned = true)
  {
    _factories[contextType] = (factory, owned);
    if (!_registeredTypes.Contains(contextType))
    {
      _registeredTypes.Add(contextType);
    }
  }

  public bool CanCreate(Type contextType) => _factories.ContainsKey(contextType);

  public (PlatformDbContext Instance, bool Owned) Create(Type concreteType, IServiceProvider services)
  {
    if (!_factories.TryGetValue(concreteType, out var entry))
    {
      throw new InvalidOperationException($"PlatformDbContext type '{concreteType.Name}' is not registered in DbContextTree.");
    }

    return (entry.Factory(services), entry.Owned);
  }

  public Type SelectConcreteType(Type requestedType, Type? boundContextType)
  {
    var candidates = _registeredTypes.Where(requestedType.IsAssignableFrom).ToList();
    if (candidates.Count == 0)
    {
      throw new InvalidOperationException($"No PlatformDbContext assignable to '{requestedType.Name}' is registered.");
    }

    if (boundContextType != null && requestedType.IsAssignableFrom(boundContextType))
    {
      var boundCandidates = _registeredTypes.Where(boundContextType.IsAssignableFrom).ToList();
      var boundLeaves = GetLeaves(boundCandidates);
      if (boundLeaves.Count == 1)
      {
        return boundLeaves[0];
      }

      if (_factories.ContainsKey(boundContextType))
      {
        return boundContextType;
      }

      if (boundLeaves.Count > 0)
      {
        return boundLeaves[0];
      }
    }

    var leaves = GetLeaves(candidates);
    if (leaves.Count == 1)
    {
      return leaves[0];
    }

    if (_factories.ContainsKey(requestedType))
    {
      return requestedType;
    }

    var commonAncestor = candidates
      .Where(c => leaves.All(c.IsAssignableFrom))
      .OrderByDescending(GetInheritanceDepth)
      .FirstOrDefault();

    return commonAncestor ?? leaves[0];
  }

  private static List<Type> GetLeaves(List<Type> candidates) =>
    candidates
      .Where(c => !candidates.Any(other => other != c && c.IsAssignableFrom(other)))
      .ToList();

  private static int GetInheritanceDepth(Type type)
  {
    var depth = 0;
    for (var cur = type; cur != null; cur = cur.BaseType)
    {
      depth++;
    }

    return depth;
  }
}

using Platform.Data.Internals;

namespace Platform.Data;

/// <summary>
/// Scoped resolver that binds a request to its target module's leaf <see cref="PlatformDbContext"/>
/// so base and leaf services share a single context instance while allowing parallel leaf modules in one host.
/// </summary>
public sealed class DbScope : IDisposable, IAsyncDisposable
{
  private readonly DbContextTree _tree;
  private readonly Dictionary<Type, PlatformDbContext> _instances = [];
  private readonly List<PlatformDbContext> _ownedInstances = [];
  private bool _disposed;

  internal DbScope(DbContextTree tree)
  {
    _tree = tree;
  }

  public Type? BoundContextType { get; private set; }

  public void Bind(Type? contextType)
  {
    if (contextType == null)
    {
      return;
    }

    if (!typeof(PlatformDbContext).IsAssignableFrom(contextType))
    {
      throw new ArgumentException($"'{contextType.Name}' must derive from {nameof(PlatformDbContext)}.", nameof(contextType));
    }

    foreach (var existing in _instances.Values)
    {
      if (!contextType.IsInstanceOfType(existing))
      {
        throw new InvalidOperationException(
          $"Cannot bind {nameof(DbScope)} to '{contextType.Name}' after incompatible context '{existing.GetType().Name}' was already resolved in this scope.");
      }
    }

    if (BoundContextType == null || BoundContextType.IsAssignableFrom(contextType))
    {
      BoundContextType = contextType;
    }
    else if (!contextType.IsAssignableFrom(BoundContextType))
    {
      throw new InvalidOperationException(
        $"Cannot rebind {nameof(DbScope)} from '{BoundContextType.Name}' to parallel context '{contextType.Name}'.");
    }
  }

  public TContext Resolve<TContext>(IServiceProvider services) where TContext : PlatformDbContext =>
    (TContext)Resolve(typeof(TContext), services);

  public PlatformDbContext Resolve(Type requestedType, IServiceProvider services)
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    ArgumentNullException.ThrowIfNull(requestedType);
    ArgumentNullException.ThrowIfNull(services);

    foreach (var existing in _instances.Values)
    {
      if (requestedType.IsInstanceOfType(existing) &&
          (BoundContextType == null || BoundContextType.IsInstanceOfType(existing)))
      {
        return existing;
      }
    }

    var concreteType = _tree.SelectConcreteType(requestedType, BoundContextType);
    if (_instances.TryGetValue(concreteType, out var cached))
    {
      return cached;
    }

    var (instance, owned) = _tree.Create(concreteType, services);
    _instances[concreteType] = instance;
    if (owned)
    {
      _ownedInstances.Add(instance);
    }

    return instance;
  }

  public static List<Type> GetLeafContextTypes(IEnumerable<Type> contextTypes) =>
    AtomicMutationCoordinator.GetLeafContextTypes(contextTypes);

  public static Task RunAtomicAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Func<CancellationToken, Task> work,
    CancellationToken ct = default) =>
    AtomicMutationCoordinator.RunAsync(contexts, work, ct);

  public static async Task RunAtomicAsync(
    IServiceProvider services,
    IEnumerable<Type> contextTypes,
    Func<IReadOnlyDictionary<Type, PlatformDbContext>, CancellationToken, Task> work,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(contextTypes);
    ArgumentNullException.ThrowIfNull(work);

    var leafTypes = GetLeafContextTypes(contextTypes);
    if (leafTypes.Count == 0)
    {
      await work(new Dictionary<Type, PlatformDbContext>(), ct);
      return;
    }

    var childScopes = new List<AsyncServiceScope>();
    var contextsByType = new Dictionary<Type, PlatformDbContext>(leafTypes.Count);
    var orderedContexts = new List<PlatformDbContext>(leafTypes.Count);
    var hasDbScope = services.GetService<DbScope>() != null;

    try
    {
      foreach (var leafType in leafTypes)
      {
        PlatformDbContext resolvedDb;
        if (hasDbScope)
        {
          var childScope = services.CreateAsyncScope();
          childScopes.Add(childScope);
          childScope.ServiceProvider.GetRequiredService<DbScope>().Bind(leafType);
          resolvedDb = (PlatformDbContext)childScope.ServiceProvider.GetRequiredService(leafType);
        }
        else
        {
          resolvedDb = services.GetService(leafType) as PlatformDbContext
            ?? (services.GetService<PlatformDbContext>() is { } fallback && leafType.IsInstanceOfType(fallback) ? fallback : null)
            ?? throw new InvalidOperationException($"DbContext '{leafType.Name}' is not registered.");
        }

        contextsByType[leafType] = resolvedDb;
        orderedContexts.Add(resolvedDb);
      }

      await RunAtomicAsync(orderedContexts, token => work(contextsByType, token), ct);
    }
    finally
    {
      for (var i = childScopes.Count - 1; i >= 0; i--)
      {
        await childScopes[i].DisposeAsync();
      }
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    for (var i = _ownedInstances.Count - 1; i >= 0; i--)
    {
      _ownedInstances[i].Dispose();
    }

    _ownedInstances.Clear();
    _instances.Clear();
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    for (var i = _ownedInstances.Count - 1; i >= 0; i--)
    {
      await _ownedInstances[i].DisposeAsync();
    }

    _ownedInstances.Clear();
    _instances.Clear();
  }
}

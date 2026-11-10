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

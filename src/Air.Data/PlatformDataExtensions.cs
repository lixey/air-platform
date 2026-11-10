using Platform.Data.Internals;

namespace Platform.Data;

public static class PlatformDataExtensions
{
  public static IServiceCollection RegisterPlatformDbContextHierarchy<TContext>(this IServiceCollection services)
    where TContext : PlatformDbContext
  {
    ArgumentNullException.ThrowIfNull(services);
    if (typeof(TContext) == typeof(PlatformDbContext))
    {
      return services;
    }

    var tree = GetOrAddTree(services);
    services.TryAddScoped(sp => new DbScope(sp.GetRequiredService<DbContextTree>()));

    if (tree.CanCreate(typeof(TContext)))
    {
      return services;
    }

    var existingDescriptor = services.LastOrDefault(d =>
      d.ServiceType == typeof(TContext) &&
      d.ImplementationFactory?.Target is not DbScopeForwarder);

    var (factory, owned) = BuildConcreteFactory<TContext>(existingDescriptor);
    tree.Register(typeof(TContext), factory, owned);

    var leafForwarder = new DbScopeForwarder(typeof(TContext));
    services.Replace(ServiceDescriptor.Scoped(leafForwarder.ResolveTyped<TContext>));

    for (var baseType = typeof(TContext).BaseType;
         baseType != null && typeof(PlatformDbContext).IsAssignableFrom(baseType);
         baseType = baseType.BaseType)
    {
      var baseForwarder = new DbScopeForwarder(baseType);
      services.Replace(ServiceDescriptor.Scoped(baseType, baseForwarder.Resolve));
    }

    return services;
  }

  private static DbContextTree GetOrAddTree(IServiceCollection services)
  {
    if (services.FirstOrDefault(d => d.ServiceType == typeof(DbContextTree))?.ImplementationInstance is DbContextTree existing)
    {
      return existing;
    }

    var tree = new DbContextTree();
    services.AddSingleton(tree);
    return tree;
  }

  private static (Func<IServiceProvider, PlatformDbContext> Factory, bool Owned) BuildConcreteFactory<TContext>(
    ServiceDescriptor? descriptor)
    where TContext : PlatformDbContext
  {
    if (descriptor?.ImplementationInstance is PlatformDbContext singletonInstance)
    {
      return (_ => singletonInstance, false);
    }

    if (descriptor?.ImplementationFactory is { } customFactory)
    {
      return (sp => (PlatformDbContext)customFactory(sp), true);
    }

    var implType = descriptor?.ImplementationType ?? typeof(TContext);
    return (sp =>
    {
      var typedOptions = sp.GetService<DbContextOptions<TContext>>();
      return typedOptions != null && implType == typeof(TContext)
        ? (PlatformDbContext)ActivatorUtilities.CreateInstance(sp, implType, typedOptions)
        : (PlatformDbContext)ActivatorUtilities.CreateInstance(sp, implType);
    }, true);
  }
}

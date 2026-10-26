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

    var derivedDescriptor = services.FirstOrDefault(d =>
      d.ServiceType != typeof(TContext) &&
      typeof(TContext).IsAssignableFrom(d.ServiceType));

    if (derivedDescriptor != null)
    {
      var derivedType = derivedDescriptor.ServiceType;
      services.Replace(ServiceDescriptor.Scoped<TContext>(sp => (TContext)sp.GetRequiredService(derivedType)));
      return services;
    }

    for (var baseType = typeof(TContext).BaseType;
         baseType != null && baseType != typeof(PlatformDbContext) && typeof(PlatformDbContext).IsAssignableFrom(baseType);
         baseType = baseType.BaseType)
    {
      var conflicting = services.FirstOrDefault(d =>
        d.ServiceType != typeof(TContext) &&
        baseType.IsAssignableFrom(d.ServiceType) &&
        !d.ServiceType.IsAssignableFrom(typeof(TContext)) &&
        !typeof(TContext).IsAssignableFrom(d.ServiceType));

      if (conflicting != null)
      {
        throw new InvalidOperationException(
          $"Conflicting DbContext hierarchy: both '{typeof(TContext).Name}' and '{conflicting.ServiceType.Name}' derive from '{baseType.Name}'.");
      }

      services.Replace(ServiceDescriptor.Scoped(baseType, sp => sp.GetRequiredService<TContext>()));
    }

    if (typeof(TContext).BaseType != typeof(PlatformDbContext))
    {
      services.Replace(ServiceDescriptor.Scoped<PlatformDbContext>(sp => sp.GetRequiredService<TContext>()));
    }
    else
    {
      services.TryAddScoped<PlatformDbContext>(sp => sp.GetRequiredService<TContext>());
    }

    return services;
  }
}

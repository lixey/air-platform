namespace Platform.Data.Internals;

internal sealed class DbScopeForwarder(Type targetType)
{
  public PlatformDbContext Resolve(IServiceProvider services) =>
    services.GetRequiredService<DbScope>().Resolve(targetType, services);

  public TContext ResolveTyped<TContext>(IServiceProvider services) where TContext : PlatformDbContext =>
    services.GetRequiredService<DbScope>().Resolve<TContext>(services);
}

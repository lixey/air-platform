namespace Platform.Api.Host;

public sealed class PlatformOptions
{
  private readonly List<IPlatformModule> _modules = [];
  private readonly List<Action<WebApplicationBuilder>> _builderActions = [];
  private readonly List<Action<WebApplication>> _appActions = [];

  public IReadOnlyList<IPlatformModule> Modules => _modules;
  public IReadOnlyList<Action<WebApplicationBuilder>> BuilderActions => _builderActions;
  public IReadOnlyList<Action<WebApplication>> AppActions => _appActions;

  public PlatformOptions AddModule<TModule>() where TModule : IPlatformModule, new()
  {
    _modules.Add(new TModule());
    return this;
  }

  public PlatformOptions AddModule(IPlatformModule module)
  {
    ArgumentNullException.ThrowIfNull(module);
    _modules.Add(module);
    return this;
  }

  public PlatformOptions ConfigureBuilder(Action<WebApplicationBuilder> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    _builderActions.Add(configure);
    return this;
  }

  public PlatformOptions ConfigureApp(Action<WebApplication> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    _appActions.Add(configure);
    return this;
  }

  public PlatformOptions AddJob<TJob>(TimeSpan interval, TimeSpan? initialDelay = null)
    where TJob : class, IPlatformJob
  {
    ConfigureBuilder(builder => builder.AddPlatformJob<TJob>(interval, initialDelay));
    return this;
  }
}

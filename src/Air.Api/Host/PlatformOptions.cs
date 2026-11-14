namespace Platform.Api.Host;

public sealed class PlatformOptions
{
  private readonly List<IPlatformModule> _modules = [];
  private readonly List<Assembly> _contractAssemblies = [];
  private readonly List<PlatformCulture> _cultures = [];
  private readonly List<Action<WebApplicationBuilder>> _builderActions = [];
  private readonly List<Action<WebApplication>> _appActions = [];

  public IReadOnlyList<IPlatformModule> Modules => _modules;
  public IReadOnlyList<Assembly> ContractAssemblies => _contractAssemblies;
  public IReadOnlyList<PlatformCulture> Cultures => _cultures;
  public IReadOnlyList<Action<WebApplicationBuilder>> BuilderActions => _builderActions;
  public IReadOnlyList<Action<WebApplication>> AppActions => _appActions;
  public string DefaultCurrency { get; set; } = "UAH";
  public string DefaultCulture { get; set; } = "uk-UA";
  public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);
  public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
  public bool UseForwardedHeaders { get; set; }

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

  public PlatformOptions AddContractAssembly(Assembly assembly)
  {
    ArgumentNullException.ThrowIfNull(assembly);
    if (!_contractAssemblies.Contains(assembly))
    {
      _contractAssemblies.Add(assembly);
    }

    return this;
  }

  public PlatformOptions AddCulture(long id, string handle, string code, string displayName)
  {
    CodeRows.EnsurePascalCaseHandle(handle, nameof(handle));
    var cultureInfo = CultureInfo.GetCultureInfo(code);
    _cultures.Add(new PlatformCulture(id, handle, cultureInfo.Name, cultureInfo.TwoLetterISOLanguageName, displayName, cultureInfo));
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

  public PlatformOptions AddLocalFiles(string? path = null) =>
    ConfigureBuilder(builder => builder.AddPlatformLocalFiles(path));

  public PlatformOptions AddJob<TJob>(TimeSpan interval, TimeSpan? initialDelay = null)
    where TJob : class, IPlatformJob
  {
    ConfigureBuilder(builder => builder.AddPlatformJob<TJob>(interval, initialDelay));
    return this;
  }
}

using Air.App.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Air.App.Modules;

public interface IPlatformModule
{
  string Handle { get; }
  string Name { get; }
  ModuleMenu Menu { get; }
  IReadOnlyList<Feature> Features { get; }
  void Configure(WebApplicationBuilder builder);
  void ConfigureServices(IServiceCollection services) { }
}

public abstract class PlatformModule : IPlatformModule
{
  public virtual string Handle => GetType().Name.Replace("Module", "");
  public virtual string Name => Handle;
  public virtual ModuleMenu Menu => ModuleMenu.Empty;
  public virtual IReadOnlyList<Feature> Features => [];

  public virtual void Configure(WebApplicationBuilder builder)
  {
    ConfigureServices(builder);
    ConfigureServices(builder.Services);
  }

  protected virtual void ConfigureServices(WebApplicationBuilder builder)
  {
  }

  public virtual void ConfigureServices(IServiceCollection services)
  {
  }
}

public abstract class PlatformModule<TDefinition> : PlatformModule
  where TDefinition : ModuleDefinition, new()
{
  private readonly TDefinition _definition = new();

  public override string Handle => _definition.GetHandle();
  public override string Name => _definition.GetName();
  public override IReadOnlyList<Feature> Features => _definition.GetFeatures();
}

public sealed class ModuleCatalog
{
  public static ModuleCatalog Instance { get; set; } = new();

  private readonly Dictionary<string, Feature> _featuresByPath = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, Type> _servicesByPath = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<IPlatformModule> _modules = [];

  public ModuleCatalog()
  {
    Instance = this;
  }

  public IReadOnlyList<IPlatformModule> Modules => _modules;
  public IEnumerable<Feature> AllFeatures => _featuresByPath.Values.Distinct();

  public void Register(IPlatformModule module)
  {
    _modules.Add(module);

    foreach (var feat in module.Features)
    {
      RegisterFeature(feat);
    }

    foreach (var node in Flatten(module.Menu.Nodes))
    {
      if (node.Feature is not null)
      {
        RegisterFeature(node.Feature);
      }
    }

    var serviceTypes = module.GetType().Assembly.GetTypes()
      .Where(t => t is { IsClass: true, IsAbstract: false } &&
                  (t.Name.EndsWith("GridService") || t.Name.EndsWith("FormService") || t.Name.EndsWith("Service")));
    foreach (var st in serviceTypes)
    {
      var path = ServicePathResolver.Resolve(st);
      _servicesByPath[path] = st;
      _servicesByPath[path.Replace('/', '-')] = st;
      _servicesByPath[path.Replace("-", "")] = st;
      _servicesByPath[path.Replace("-", "").Replace('/', '-')] = st;
      _servicesByPath[st.Name] = st;

      var clean = st.Name;
      if (clean.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
      {
        clean = clean[..^"Service".Length];
      }
      _servicesByPath[clean] = st;

      if (path.EndsWith("-grid", StringComparison.OrdinalIgnoreCase))
      {
        var noGrid = path[..^"-grid".Length];
        _servicesByPath[noGrid] = st;
        _servicesByPath[noGrid.Replace('/', '-')] = st;
        _servicesByPath[noGrid.Replace("-", "")] = st;
      }
      else if (path.EndsWith("-form", StringComparison.OrdinalIgnoreCase))
      {
        var noForm = path[..^"-form".Length];
        _servicesByPath[noForm + "form"] = st;
        _servicesByPath[(noForm + "form").Replace('/', '-')] = st;
        _servicesByPath[(noForm + "form").Replace("-", "")] = st;
      }
    }
  }

  private void RegisterFeature(Feature feat)
  {
    _featuresByPath[feat.Path] = feat;
    _featuresByPath[feat.Path.Replace("-", "")] = feat;
    _featuresByPath[feat.Name] = feat;
    _featuresByPath[feat.CleanName] = feat;
    if (feat.ServiceType != null)
    {
      var servicePath = ServicePathResolver.Resolve(feat.ServiceType);
      _featuresByPath[servicePath] = feat;
      _servicesByPath[feat.Path] = feat.ServiceType;
      _servicesByPath[feat.Path.Replace("-", "")] = feat.ServiceType;
      _servicesByPath[feat.Name] = feat.ServiceType;
    }
  }

  public Feature? FindFeature(string path)
  {
    if (string.IsNullOrWhiteSpace(path)) return null;
    return _featuresByPath.GetValueOrDefault(path)
      ?? _featuresByPath.GetValueOrDefault(path.Replace('/', '-'))
      ?? _featuresByPath.GetValueOrDefault(path.Replace('-', '/'))
      ?? _featuresByPath.GetValueOrDefault(path.Replace("demo/", ""))
      ?? _featuresByPath.GetValueOrDefault(path.Replace("-", ""));
  }

  public Type? FindServiceType(string path)
  {
    if (string.IsNullOrWhiteSpace(path)) return null;
    return _servicesByPath.GetValueOrDefault(path)
      ?? _servicesByPath.GetValueOrDefault(path.Replace('/', '-'))
      ?? _servicesByPath.GetValueOrDefault(path.Replace('-', '/'))
      ?? _servicesByPath.GetValueOrDefault(path.Replace("demo/", ""))
      ?? _servicesByPath.GetValueOrDefault(path.Replace("-", ""));
  }

  public object? CreateServiceInstance(Type serviceType, params object?[] availableServices)
  {
    var ctor = serviceType.GetConstructors().FirstOrDefault();
    if (ctor is null) return null;

    var serviceProvider = availableServices.OfType<IServiceProvider>().FirstOrDefault()
      ?? availableServices.OfType<PlatformAppService>().FirstOrDefault()?.Services;
    var appService = availableServices.OfType<PlatformAppService>().FirstOrDefault()
      ?? serviceProvider?.GetService(typeof(PlatformAppService)) as PlatformAppService;

    var parameters = ctor.GetParameters();
    var args = new object?[parameters.Length];
    for (var i = 0; i < parameters.Length; i++)
    {
      var param = parameters[i];
      var match = availableServices.FirstOrDefault(s => s is not null && param.ParameterType.IsInstanceOfType(s))
        ?? serviceProvider?.GetService(param.ParameterType);
      if (match is not null)
      {
        args[i] = match;
      }
      else if (param.HasDefaultValue)
      {
        args[i] = param.DefaultValue;
      }
      else if (param.ParameterType.IsValueType)
      {
        args[i] = Activator.CreateInstance(param.ParameterType);
      }
    }

    var instance = ctor.Invoke(args);
    if (appService is not null)
    {
      if (instance is Grid.IGridService grid) grid.AttachPlatformApp(appService);
      else if (instance is Form.IFormService form) form.AttachPlatformApp(appService);
    }
    return instance;
  }

  public static IEnumerable<ModuleMenuNode> Flatten(IEnumerable<ModuleMenuNode> nodes)
  {
    foreach (var node in nodes)
    {
      yield return node;
      foreach (var child in Flatten(node.Children))
      {
        yield return child;
      }
    }
  }
}

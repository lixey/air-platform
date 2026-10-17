using Air.App.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Air.App.Modules;

public interface IPlatformModule
{
  string Handle { get; }
  string Name { get; }
  ModuleMenu Menu { get; }
  void ConfigureServices(IServiceCollection services);
}

public abstract class PlatformModule : IPlatformModule
{
  public virtual string Handle => GetType().Name.Replace("Module", "");
  public virtual string Name => Handle;
  public virtual ModuleMenu Menu => ModuleMenu.Empty;

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
  public IReadOnlyList<Feature> Features => _definition.GetFeatures();
}

public sealed class ModuleCatalog
{
  private readonly Dictionary<string, Feature> _featuresByPath = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<IPlatformModule> _modules = [];

  public IReadOnlyList<IPlatformModule> Modules => _modules;
  public IEnumerable<Feature> AllFeatures => _featuresByPath.Values.Distinct();

  public void Register(IPlatformModule module)
  {
    _modules.Add(module);

    var featuresProp = module.GetType().GetProperty("Features");
    if (featuresProp?.GetValue(module) is IReadOnlyList<Feature> features)
    {
      foreach (var feat in features)
      {
        _featuresByPath[feat.Path] = feat;
        _featuresByPath[feat.Name] = feat;
      }
    }

    foreach (var node in Flatten(module.Menu.Nodes))
    {
      if (node.Feature is not null)
      {
        _featuresByPath[node.Feature.Path] = node.Feature;
        _featuresByPath[node.Feature.Name] = node.Feature;
      }
    }
  }

  public Feature? FindFeature(string path) =>
    _featuresByPath.GetValueOrDefault(path);

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

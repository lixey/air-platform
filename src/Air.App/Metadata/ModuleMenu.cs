using Air.App.Modules;

namespace Air.App.Metadata;

public sealed class ModuleMenuNode
{
  public Feature? Feature { get; init; }
  public string? Handle { get; init; }
  public string? Title { get; init; }
  public string? Icon { get; init; }
  public bool IsHidden { get; init; }
  public bool IsSeparator { get; init; }
  public List<ModuleMenuNode> Children { get; init; } = [];
}

public sealed class ModuleMenu
{
  public static readonly ModuleMenu Empty = new([]);

  public ModuleMenu(List<ModuleMenuNode> nodes) => Nodes = nodes;

  public IReadOnlyList<ModuleMenuNode> Nodes { get; }

  public static ModuleMenu Build(Action<ModuleMenuBuilder> configure)
  {
    var builder = new ModuleMenuBuilder();
    configure(builder);
    return builder.Build();
  }

  public static ModuleMenuBuilder Route(Feature feature, string? title = null, string? icon = null) =>
    new ModuleMenuBuilder().Route(feature, title, icon);
  public static ModuleMenuBuilder Hidden(Feature feature) => new ModuleMenuBuilder().Hidden(feature);
  public static ModuleMenuBuilder Group(string handle, string title, Action<ModuleMenuBuilder> configure) =>
    new ModuleMenuBuilder().Group(handle, title, configure);
}

public sealed class ModuleMenuBuilder
{
  private readonly List<ModuleMenuNode> _nodes = [];

  public ModuleMenuBuilder Route(Feature feature, string? title = null, string? icon = null)
  {
    _nodes.Add(new ModuleMenuNode
    {
      Feature = feature,
      Handle = feature.CleanName,
      Title = title ?? feature.Name,
      Icon = icon
    });
    return this;
  }

  public ModuleMenuBuilder Item(string module, string name, string title, string? icon = null, Type? serviceType = null)
  {
    var feature = new Feature(module, name, serviceType);
    _nodes.Add(new ModuleMenuNode { Feature = feature, Handle = feature.CleanName, Title = title, Icon = icon });
    return this;
  }

  public ModuleMenuBuilder Hidden(Feature feature)
  {
    _nodes.Add(new ModuleMenuNode { Feature = feature, Handle = feature.CleanName, Title = feature.Name, IsHidden = true });
    return this;
  }

  public ModuleMenuBuilder Separator()
  {
    _nodes.Add(new ModuleMenuNode { IsSeparator = true });
    return this;
  }

  public ModuleMenuBuilder Group(string handle, string title, Action<ModuleMenuBuilder> configure)
  {
    var builder = new ModuleMenuBuilder();
    configure(builder);
    _nodes.Add(new ModuleMenuNode { Handle = handle, Title = title, Children = builder._nodes });
    return this;
  }

  public ModuleMenu Build() => new(_nodes);
}

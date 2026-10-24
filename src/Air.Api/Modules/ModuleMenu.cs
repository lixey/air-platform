namespace Platform.Api.Modules;

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

namespace Platform.Api.Modules;

public sealed class ModuleMenuBuilder
{
  private readonly List<ModuleMenuNode> _nodes = [];

  public ModuleMenuBuilder Route(Feature feature, string? title = null, string? icon = null, string? tooltip = null)
  {
    ArgumentNullException.ThrowIfNull(feature);
    _nodes.Add(new ModuleMenuNode
    {
      Feature = feature,
      Handle = feature.Handle,
      Title = title ?? feature.Name,
      Tooltip = tooltip ?? feature.Title,
      Icon = icon
    });
    return this;
  }

  public ModuleMenuBuilder Hidden(Feature feature)
  {
    ArgumentNullException.ThrowIfNull(feature);
    _nodes.Add(new ModuleMenuNode { Feature = feature, Handle = feature.Handle, Title = feature.Name, IsHidden = true });
    return this;
  }

  public ModuleMenuBuilder Separator()
  {
    _nodes.Add(new ModuleMenuNode { IsSeparator = true });
    return this;
  }

  public ModuleMenuBuilder Group(string handle, string title, Action<ModuleMenuBuilder> configure)
  {
    CodeRows.EnsurePascalCaseHandle(handle, "ModuleMenuGroup");
    ArgumentNullException.ThrowIfNull(configure);
    var builder = new ModuleMenuBuilder();
    configure(builder);
    _nodes.Add(new ModuleMenuNode { Handle = handle, Title = title, Children = builder._nodes });
    return this;
  }

  public ModuleMenu Build() => new(_nodes);
}

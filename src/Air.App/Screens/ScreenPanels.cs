namespace Platform.App.Screens;

public sealed class ScreenPanels
{
  private PanelLayoutBuilder _layout;

  internal ScreenPanels(string servicePath)
  {
    ServicePath = servicePath;
    _layout = PanelLayout.Row(servicePath);
  }

  internal string ServicePath { get; }

  public PanelLayoutBuilder Row() => _layout = PanelLayout.Row(ServicePath);

  public PanelLayoutBuilder Column() => _layout = PanelLayout.Column(ServicePath);

  public PanelLayoutBuilder Gap(int pixels) => _layout.Gap(pixels);

  public PanelLayoutBuilder HideSecondaryUnder(int containerSizePx) => _layout.HideSecondaryUnder(containerSizePx);

  public PanelLayoutBuilder Exclusive() => _layout.Exclusive();

  public PanelLayoutBuilder Panel(string key, Action<PanelBuilder> configure) => _layout.Panel(key, configure);

  public PanelLayoutBuilder Panel(string key, PanelModel model) => _layout.Panel(key, model);

  public PanelLayoutBuilder Screen<TService>(string key, Action<PanelBuilder>? configure = null)
    where TService : class, IPlatformService =>
    _layout.Screen<TService>(key, configure);

  internal PanelLayoutDto Build(ILocalizer localizer)
  {
    var dto = _layout.Build();
    LocalizeNode(dto.Root, localizer);
    return dto;
  }

  private static void LocalizeNode(PanelNodeDto node, ILocalizer localizer)
  {
    if (!string.IsNullOrEmpty(node.Label))
    {
      node.Label = localizer[node.Label];
    }

    if (node.Children is not { Count: > 0 })
    {
      return;
    }

    foreach (var child in node.Children)
    {
      LocalizeNode(child, localizer);
    }
  }
}

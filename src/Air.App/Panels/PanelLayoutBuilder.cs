namespace Platform.App.Panels;

public sealed class PanelLayoutBuilder
{
  private readonly PanelLayoutDto _layout;
  private readonly PanelBuilder _root;

  internal PanelLayoutBuilder(string key, string direction)
  {
    _root = new PanelBuilder(string.Empty) { Node = { Direction = direction } };
    _layout = new PanelLayoutDto { Key = key, Root = _root.Node };
  }

  public PanelLayoutBuilder Gap(int pixels)
  {
    _layout.Gap = pixels;
    return this;
  }

  public PanelLayoutBuilder HideSecondaryUnder(int containerSizePx)
  {
    _layout.HideSecondaryUnder = containerSizePx;
    return this;
  }

  public PanelLayoutBuilder Exclusive()
  {
    _root.Exclusive();
    return this;
  }

  public PanelLayoutBuilder Panel(string key, Action<PanelBuilder> configure)
  {
    _root.Panel(key, configure);
    return this;
  }

  public PanelLayoutBuilder Panel(string key, PanelModel model) => Panel(key, p => p.Model(model));

  public PanelLayoutDto Build() => _layout;
}

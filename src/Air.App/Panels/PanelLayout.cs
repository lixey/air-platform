namespace Air.App.Panels;

public static class PanelLayout
{
  public static PanelLayoutBuilder Row(string key) => new(key, PanelDirection.Row);
  public static PanelLayoutBuilder Column(string key) => new(key, PanelDirection.Column);
}

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

public sealed class PanelBuilder
{
  internal PanelBuilder(string key) => Node = new PanelNodeDto { Key = key };

  internal PanelNodeDto Node { get; }

  public PanelBuilder Model(PanelModel model)
  {
    ArgumentNullException.ThrowIfNull(model);
    Node.Model = model;
    return this;
  }

  public PanelBuilder Label(string label)
  {
    Node.Label = label;
    return this;
  }

  public PanelBuilder Size(string size)
  {
    Node.Size = size;
    return this;
  }

  public PanelBuilder Size(int pixels) => Size(PanelSize.Px(pixels));
  public PanelBuilder Fill() => Size(PanelSize.Fill);
  public PanelBuilder Auto() => Size(PanelSize.Auto);

  public PanelBuilder Min(string size)
  {
    Node.MinSize = size;
    return this;
  }

  public PanelBuilder Min(int pixels) => Min(PanelSize.Px(pixels));

  public PanelBuilder Max(string size)
  {
    Node.MaxSize = size;
    return this;
  }

  public PanelBuilder Max(int pixels) => Max(PanelSize.Px(pixels));

  public PanelBuilder Resizable(bool isResizable = true)
  {
    Node.IsResizable = isResizable;
    return this;
  }

  public PanelBuilder Rigid(bool isRigid = true)
  {
    Node.IsRigid = isRigid;
    return this;
  }

  public PanelBuilder Transparent(bool isTransparent = true)
  {
    Node.IsTransparent = isTransparent;
    return this;
  }

  public PanelBuilder Collapsible(bool isCollapsible = true)
  {
    Node.IsCollapsible = isCollapsible;
    return this;
  }

  public PanelBuilder Collapsed(bool isCollapsed = true)
  {
    Node.IsCollapsed = isCollapsed;
    return this;
  }

  public PanelBuilder Pinned(bool isPinned = true)
  {
    Node.IsPinned = isPinned;
    return this;
  }

  public PanelBuilder Exclusive(bool isExclusive = true)
  {
    Node.IsExclusive = isExclusive;
    return this;
  }

  public PanelBuilder Row(string key, Action<PanelBuilder> configure)
  {
    var child = new PanelBuilder(key) { Node = { Direction = PanelDirection.Row } };
    configure(child);
    AddChild(child.Node);
    return this;
  }

  public PanelBuilder Column(string key, Action<PanelBuilder> configure)
  {
    var child = new PanelBuilder(key) { Node = { Direction = PanelDirection.Column } };
    configure(child);
    AddChild(child.Node);
    return this;
  }

  public PanelBuilder Panel(string key, Action<PanelBuilder> configure)
  {
    var child = new PanelBuilder(key);
    configure(child);
    AddChild(child.Node);
    return this;
  }

  public PanelBuilder Panel(string key, PanelModel model) => Panel(key, p => p.Model(model));

  private void AddChild(PanelNodeDto child)
  {
    Node.Children ??= [];
    Node.Children.Add(child);
  }
}

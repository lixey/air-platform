namespace Platform.App.Panels;

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

  public PanelBuilder PageUnder(int? pixels)
  {
    Node.PageUnder = pixels;
    return this;
  }

  public PanelBuilder PageUnderHeight(int? pixels)
  {
    Node.PageUnderHeight = pixels;
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

namespace Platform.App.Form;

public sealed class FormNav
{
  internal List<Action<NavModel, NavModel?>> Customizers { get; } = [];

  public List<NavElementModel> Start { get; } = [];
  public List<NavElementModel> Elements { get; } = [];
  public List<NavElementModel> End { get; } = [];

  public bool ShowSaveAndClose { get; set; } = true;
  public bool ShowSave { get; set; } = true;
  public bool ShowClose { get; set; } = true;
  public bool ShowHistory { get; set; } = true;

  public FormNav AddStart(NavElementModel model)
  {
    Start.Add(model);
    return this;
  }

  public FormNav Add(NavElementModel model)
  {
    Elements.Add(model);
    return this;
  }

  public FormNav AddEnd(NavElementModel model)
  {
    End.Add(model);
    return this;
  }

  public FormNav WithoutSaveAndClose()
  {
    ShowSaveAndClose = false;
    return this;
  }

  public FormNav WithoutSave()
  {
    ShowSave = false;
    return this;
  }

  public FormNav WithoutClose()
  {
    ShowClose = false;
    return this;
  }

  public FormNav WithoutHistory()
  {
    ShowHistory = false;
    return this;
  }

  public FormNav Customize(Action<NavModel> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    Customizers.Add((nav, _) => configure(nav));
    return this;
  }

  public FormNav Customize(Action<NavModel, NavModel?> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    Customizers.Add(configure);
    return this;
  }
}

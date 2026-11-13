namespace Platform.App.Grid;

public sealed class GridButtonBuilder
{
  public string? LabelText { get; private set; }
  public string? TitleText { get; private set; }
  public string? IconHandle { get; private set; }
  public string? ColorHandle { get; private set; }
  public string? VariantHandle { get; private set; }
  public bool IsInMenu { get; private set; }
  public bool IsSingle { get; private set; }

  public GridButtonBuilder Label(string label)
  {
    LabelText = label;
    return this;
  }

  public GridButtonBuilder Title(string title)
  {
    TitleText = title;
    return this;
  }

  public GridButtonBuilder Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));

  public GridButtonBuilder Icon(string icon)
  {
    IconHandle = icon;
    return this;
  }

  public GridButtonBuilder Color(string color)
  {
    ColorHandle = color;
    return this;
  }

  public GridButtonBuilder Variant(string variant)
  {
    VariantHandle = variant;
    return this;
  }

  public GridButtonBuilder Soft()
  {
    VariantHandle = ButtonVariant.Soft;
    return this;
  }

  public GridButtonBuilder InMenu()
  {
    IsInMenu = true;
    return this;
  }

  public GridButtonBuilder Single(bool single = true)
  {
    IsSingle = single;
    return this;
  }
}

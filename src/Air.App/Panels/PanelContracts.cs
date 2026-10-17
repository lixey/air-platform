namespace Air.App.Panels;

[UiContract]
public static class PanelDirection
{
  public const string Row = "row";
  public const string Column = "column";
}

public static class PanelSize
{
  public const string Fill = "1fr";
  public const string Auto = "auto";

  public static string Px(int value) => $"{value}px";
  public static string Percent(int value) => $"{value}%";
  public static string Fr(int value) => $"{value}fr";
}

[UiContract]
public sealed class PanelNodeDto
{
  public string Key { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public PanelModel? Model { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  [ClientOptional]
  public string Size { get; set; } = PanelSize.Fill;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? MinSize { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? MaxSize { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsResizable { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsRigid { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsCollapsible { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsCollapsed { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsTransparent { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsPinned { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsExclusive { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? HideUnder { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? PageUnder { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? PageUnderHeight { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(PanelDirection))]
  public string? Direction { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<PanelNodeDto>? Children { get; set; }
}

[UiContract]
public sealed class PanelLayoutDto
{
  public string Key { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Gap { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? HideSecondaryUnder { get; set; }

  public PanelNodeDto Root { get; set; } = new();
}

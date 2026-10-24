namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class GridNumberEditDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public double? Step { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public double? Min { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public double? Max { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool HasStepper { get; set; }
}

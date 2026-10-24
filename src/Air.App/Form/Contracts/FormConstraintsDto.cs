namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class FormConstraintsDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? AllowsNull { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? RejectsBlank { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(FormLengthUnit))]
  public string? LengthUnit { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinLength { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxLength { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Precision { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Scale { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [WireNumber]
  public JsonElement? Min { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [WireNumber]
  public JsonElement? Max { get; set; }
}

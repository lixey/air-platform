namespace Platform.Data.Contracts;

/// <summary>
/// A reference to another entity: Id, Display Name, and optional semantic Color.
/// </summary>
[UiContract]
public class Reference
{
  public long Id { get; set; }
  public string Name { get; set; } = string.Empty;

  [OneOf(typeof(UiColor))]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Color { get; set; }
}

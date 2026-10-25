namespace Platform.Data.Contracts;

/// <summary>
/// A reference to another entity: Id, Display Name, and optional semantic Color.
/// </summary>
public class Reference
{
  public long Id { get; set; }
  public string Name { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Color { get; set; }
}

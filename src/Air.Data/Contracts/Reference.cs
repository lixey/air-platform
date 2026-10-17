namespace Air.Data.Contracts;

/// <summary>
/// A reference to another entity: Id and Display Name.
/// </summary>
public class Reference
{
  public long Id { get; set; }
  public string Name { get; set; } = string.Empty;
}

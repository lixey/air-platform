namespace Platform.App.Grid.Contracts;

[UiContract]
[ClientOptional]
public class GridPickRequest : GridRequest
{
  public long PickedId { get; set; }
}

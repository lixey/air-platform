namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class GridDeleteDto : MutationResultDto
{
  public int Deleted { get; set; }
}

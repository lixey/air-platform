namespace Platform.App.Grid.Contracts;

[UiContract]
public class GridCellDto : MutationResultDto
{
  public object? Row { get; set; }

  public GridCellDto()
  {
    Outcome = MutationOutcome.Committed;
  }
}

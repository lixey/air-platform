namespace Platform.App.Form.Contracts;

public sealed class FormChildDraftGroupDto
{
  public required string ChildKey { get; init; }
  public required string ChildGridService { get; init; }
  public required string ChildFormService { get; init; }
  public required string Title { get; init; }
  public string Mode { get; init; } = "replace";
  public bool HasUpdate { get; init; }
  public IReadOnlyList<FormElementDto> Elements { get; init; } = [];
  public List<FormChildDraftRowDto> Rows { get; init; } = [];
}

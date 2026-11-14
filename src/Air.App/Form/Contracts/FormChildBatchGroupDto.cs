namespace Platform.App.Form.Contracts;

public sealed class FormChildBatchGroupDto
{
  public required string ChildKey { get; init; }
  public string? ChildGridService { get; init; }
  public required string ChildFormService { get; init; }
  public string Mode { get; init; } = "replace";
  public bool HasUpdate { get; init; } = true;
  public List<Dictionary<string, object?>> Rows { get; init; } = [];
}

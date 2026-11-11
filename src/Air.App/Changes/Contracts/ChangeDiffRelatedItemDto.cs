namespace Platform.App.Changes.Contracts;

[UiContract]
public sealed record ChangeDiffRelatedItemDto
{
  public long Id { get; init; }
  public string Title { get; init; } = string.Empty;
  public string Operation { get; init; } = string.Empty;
  public string OperationColor { get; init; } = UiColor.Slate;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OpenTargetDto? Open { get; init; }

  public bool IsCurrent { get; init; }
}

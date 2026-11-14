namespace Platform.App.Form.Contracts;

public sealed class FormChildBatchResultDto
{
  public List<MessageDto> Warnings { get; init; } = [];
  public List<AffectedDto>? Affected { get; init; }
}

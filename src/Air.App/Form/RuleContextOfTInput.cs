namespace Platform.App.Form;

public class RuleContext<TInput>
{
  public required object? Target { get; init; }
  public required FormSaveContext<object, TInput> SaveContext { get; init; }
  public List<MessageDto> Failures { get; } = [];
  internal string? CurrentPath { get; set; }

  public void AddFailure(string message, string? path = null, string? code = null) =>
    Failures.Add(new MessageDto
    {
      Detail = message,
      Path = path ?? CurrentPath,
      Code = code,
      Type = MessageType.Error
    });
}

namespace Platform.App.Form;

public class RuleContext<TEntity>
{
  public required FormSaveContext<TEntity> SaveContext { get; init; }
  public TEntity Entity => SaveContext.Entity;
  public bool IsNew => SaveContext.IsNew;
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

  public void Warn(string message, string? path = null, string? code = null) =>
    SaveContext.Warn(message, code, path: path ?? CurrentPath);
}

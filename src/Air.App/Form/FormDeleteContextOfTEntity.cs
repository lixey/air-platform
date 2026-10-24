namespace Platform.App.Form;

public class FormDeleteContext<TEntity>
{
  public required TEntity Entity { get; init; }
  public List<MessageDto> Errors { get; } = [];

  public void AddError(string detail, string? path = null) =>
    Errors.Add(new MessageDto { Detail = detail, Path = path, Type = MessageType.Error });

  public void AddError(string code, string detail, string? path = null) =>
    Errors.Add(new MessageDto { Code = code, Detail = detail, Path = path, Type = MessageType.Error });

  public void Refuse(string detail, string? path = null) =>
    AddError(detail, path);
}

namespace Platform.App.Form;

public sealed class FormRefusedException(
  string outcome,
  string title,
  IReadOnlyList<MessageDto> messages) : Exception(title)
{
  public string Outcome { get; } = outcome;
  public string Title { get; } = title;
  public IReadOnlyList<MessageDto> Messages { get; } = messages;
}

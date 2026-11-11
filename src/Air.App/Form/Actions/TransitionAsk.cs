namespace Platform.App.Form.Actions;

public static class TransitionAsk
{
  public static RecordAsk Of<TInput>(Action<Elements<TInput>> elements) where TInput : class, new()
  {
    ArgumentNullException.ThrowIfNull(elements);
    return new TransitionAsk<TInput>(elements);
  }
}

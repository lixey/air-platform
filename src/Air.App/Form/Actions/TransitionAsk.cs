namespace Air.App.Form.Actions;

public static class TransitionAsk
{
  public static RecordAsk Of<TInput>(Action<ActionInput<TInput>> elements) where TInput : class, new()
  {
    ArgumentNullException.ThrowIfNull(elements);
    return new TransitionAsk<TInput>(elements);
  }
}

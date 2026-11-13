namespace Platform.App.Screens;

public sealed class StatusFlowOptions(Action<bool> setAllowReadOnly)
{
  public StatusFlowOptions AllowReadOnly(bool allow = true)
  {
    setAllowReadOnly(allow);
    return this;
  }
}

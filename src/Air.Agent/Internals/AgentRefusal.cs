namespace Platform.Agent.Internals;

internal static class AgentRefusal
{
  private const string MarkerKey = "Platform.Agent.DomainRefusal";

  public static InvalidOperationException Create(string message, Exception? inner = null)
  {
    var ex = new InvalidOperationException(message, inner);
    ex.Data[MarkerKey] = true;
    return ex;
  }

  public static bool IsDomainRefusal(InvalidOperationException ex) =>
    ex.Data.Contains(MarkerKey);
}

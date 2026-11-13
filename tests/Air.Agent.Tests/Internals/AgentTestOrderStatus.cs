namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestOrderStatus : IEntity
{
  public long Id { get; set; }
  public string Name { get; set; } = string.Empty;
  public bool IsEditable { get; set; }
  public bool IsFinal { get; set; }
}

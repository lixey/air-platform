namespace Platform.App.Screens;

public sealed class RecordAvailability
{
  public required LambdaExpression Condition { get; init; }
  public required string Reason { get; init; }
}

using System.Linq.Expressions;

namespace Air.App.Screens;

public sealed class RecordAvailability
{
  public required LambdaExpression Condition { get; init; }
  public required string Reason { get; init; }
}

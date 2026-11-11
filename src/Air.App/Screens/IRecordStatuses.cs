namespace Platform.App.Screens;

public interface IRecordStatuses
{
  LambdaExpression StatusMemberExpression { get; }
  IReadOnlyList<OptionDto> Statuses { get; }
  IReadOnlyList<Type> Followers { get; }
  bool FollowsChildFlow(Type childFlowType) => false;
  PropertyInfo? FindParentKeyProperty(Type childEntityType) => null;
  Task FollowMovesAsync(IMutationContext context, IReadOnlySet<long> movedRecordIds, CancellationToken cancellationToken);
}

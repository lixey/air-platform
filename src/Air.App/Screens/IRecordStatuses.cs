namespace Platform.App.Screens;

public interface IRecordStatuses
{
  IReadOnlyList<Type> Followers { get; }
  PropertyInfo? FindParentKeyProperty(Type childEntityType) => null;
  Task FollowMovesAsync(IMutationContext context, IReadOnlySet<long> movedRecordIds, CancellationToken cancellationToken);
}

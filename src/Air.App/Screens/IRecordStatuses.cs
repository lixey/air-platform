namespace Platform.App.Screens;

public interface IRecordStatuses
{
  IReadOnlyList<Type> Followers { get; }
  Task FollowMovesAsync(IMutationContext context, IReadOnlySet<long> movedRecordIds, CancellationToken cancellationToken);
}

namespace Air.App.Screens;

public interface IRecordStatuses
{
  IReadOnlyList<Type> Followers { get; }
  Task FollowMovesAsync(IMutationContext context, IReadOnlySet<long> movedRecordIds, CancellationToken cancellationToken);
}

public interface IRecordStatuses<TEntity> : IRecordStatuses where TEntity : class
{
  Expression<Func<TEntity, long>> StatusMember { get; }
  IReadOnlyList<OptionDto> Statuses { get; }
  OptionDto? Initial { get; }
  TEntity Initialize(TEntity record);
  IReadOnlyList<RecordTransition> Transitions { get; }
  Task MoveAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    string transition,
    object? input,
    CancellationToken cancellationToken
  );
}

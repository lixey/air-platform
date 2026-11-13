namespace Platform.App.Screens;

public interface IRecordStatuses<TEntity> : IRecordStatuses where TEntity : class
{
  Expression<Func<TEntity, long>> StatusMember { get; }
  LambdaExpression IRecordStatuses.StatusMemberExpression => StatusMember;
  OptionDto? Initial { get; }
  TEntity Initialize(TEntity record);
  IReadOnlyList<RecordTransition> Transitions { get; }
  new IReadOnlyList<RecordTransition> AllTransitions => Transitions;
  Task LockRecordsAsync(IMutationContext context, IReadOnlyList<TEntity> records, CancellationToken cancellationToken) =>
    Task.CompletedTask;
  Task MoveAsync(
    IMutationContext context,
    IReadOnlyList<TEntity> records,
    string transition,
    object? input,
    CancellationToken cancellationToken
  );
}

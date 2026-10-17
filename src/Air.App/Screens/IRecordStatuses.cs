using System.Linq.Expressions;
using Air.App.Mutations;

namespace Air.App.Screens;

public interface IRecordStatuses<TEntity> where TEntity : class
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

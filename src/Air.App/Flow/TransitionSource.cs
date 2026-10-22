using System.Runtime.CompilerServices;

namespace Air.App.Flow;

public sealed class TransitionSource<TEntity>(
  IReadOnlyList<long> from,
  Func<StatusFlowService<TEntity>.Transition, StatusFlowService<TEntity>.Transition> register
)
  where TEntity : class, IEntity, new()
{
  public StatusFlowService<TEntity>.Transition To(long status, [CallerMemberName] string name = "")
  {
    var transition = new StatusFlowService<TEntity>.Transition(name, from, status);
    return register(transition);
  }
}

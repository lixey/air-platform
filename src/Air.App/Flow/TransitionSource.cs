using System.Runtime.CompilerServices;

namespace Platform.App.Flow;

public sealed class TransitionSource<TEntity>(
  IReadOnlyList<long> from,
  Func<StatusFlowService<TEntity>.Transition, StatusFlowService<TEntity>.Transition> register,
  bool excludeTarget = false
)
  where TEntity : class, IEntity, new()
{
  public StatusFlowService<TEntity>.Transition To(long status, [CallerMemberName] string name = "")
  {
    var effectiveFrom = excludeTarget
      ? from.Where(id => id != status).ToArray()
      : from;
    var transition = new StatusFlowService<TEntity>.Transition(name, effectiveFrom, status);
    return register(transition);
  }
}

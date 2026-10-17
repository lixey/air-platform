using System.Runtime.CompilerServices;

namespace Air.App.Flow;

public sealed class TransitionSource<TEntity>(
  IReadOnlyList<long> from,
  Func<Transition<TEntity>, Transition<TEntity>> register
)
{
  public Transition<TEntity> To(long status, [CallerMemberName] string name = "")
  {
    var transition = new Transition<TEntity>(name, from, status);
    return register(transition);
  }
}

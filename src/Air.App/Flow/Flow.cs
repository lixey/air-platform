using Platform.App.Internals;

namespace Platform.App.Flow;

public class Flow<TEntity>
{
  internal Expression<Func<TEntity, long>>? StateMember { get; private set; }
  internal List<OptionDto> DeclaredStatuses { get; } = [];
  internal HashSet<long> FinalStatusIds { get; } = [];
  internal long? InitialStatusId { get; private set; }
  internal List<Type> FollowerTypes { get; } = [];
  internal Dictionary<Type, PropertyInfo> FollowerForeignKeys { get; } = [];

  public List<OptionDto> Items => DeclaredStatuses;

  public OptionDto? Find(long id) => DeclaredStatuses.FirstOrDefault(s => s.Id == id);

  public bool Remove(long id)
  {
    FinalStatusIds.Remove(id);
    if (InitialStatusId == id)
    {
      InitialStatusId = null;
    }

    return DeclaredStatuses.RemoveAll(s => s.Id == id) > 0;
  }

  public void Clear()
  {
    DeclaredStatuses.Clear();
    FinalStatusIds.Clear();
    InitialStatusId = null;
  }

  public void State(Expression<Func<TEntity, long>> member)
  {
    StateMember = member;
  }

  public void State<TStatus>(Expression<Func<TEntity, TStatus>> member)
    where TStatus : struct
  {
    ArgumentNullException.ThrowIfNull(member);
    StateMember = typeof(TStatus) == typeof(long)
      ? (Expression<Func<TEntity, long>>)(LambdaExpression)member
      : Expression.Lambda<Func<TEntity, long>>(Expression.Convert(member.Body, typeof(long)), member.Parameters);
  }

  public void Status(long id, string name, string color, string? title = null, bool isFinal = false)
  {
    DeclaredStatuses.Add(new OptionDto
    {
      Id = id,
      Name = name,
      Color = color,
      Title = title
    });
    if (isFinal)
    {
      FinalStatusIds.Add(id);
    }
  }

  public void Status(long id, string name, string color, string uiSource, string dbSource, bool isFinal = false) =>
    Status(id, name, color, Feature.FormatSourceTitle(uiSource, dbSource), isFinal);

  public void FinalStatus(long id, string name, string color, string? title = null) =>
    Status(id, name, color, title, isFinal: true);

  public void FinalStatus(long id, string name, string color, string uiSource, string dbSource) =>
    Status(id, name, color, uiSource, dbSource, isFinal: true);

  public void Initial(long id)
  {
    InitialStatusId = id;
  }

  public void FollowedBy<TFlowService>() where TFlowService : class
  {
    FollowerTypes.Add(typeof(TFlowService));
  }

  public void FollowedBy<TFlowService>(Expression<Func<TEntity, long?>> fk) where TFlowService : class
  {
    ArgumentNullException.ThrowIfNull(fk);
    FollowerTypes.Add(typeof(TFlowService));
    if (ParentScopeValidator.ExtractBoundProperty(fk) is { } prop)
    {
      FollowerForeignKeys[typeof(TFlowService)] = prop;
    }
  }

  public void FollowedBy<TFlowService>(Expression<Func<TEntity, long>> fk) where TFlowService : class
  {
    ArgumentNullException.ThrowIfNull(fk);
    FollowerTypes.Add(typeof(TFlowService));
    if (ParentScopeValidator.ExtractBoundProperty(fk) is { } prop)
    {
      FollowerForeignKeys[typeof(TFlowService)] = prop;
    }
  }
}

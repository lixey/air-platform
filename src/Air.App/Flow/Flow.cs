namespace Air.App.Flow;

public sealed class Flow<TEntity>
{
  internal Expression<Func<TEntity, long>>? StateMember { get; private set; }
  internal List<OptionDto> DeclaredStatuses { get; } = [];
  internal long? InitialStatusId { get; private set; }

  public void State(Expression<Func<TEntity, long>> member)
  {
    StateMember = member;
  }

  public void Status(long id, string name, string color)
  {
    DeclaredStatuses.Add(new OptionDto
    {
      Id = id,
      Name = name,
      Color = color
    });
  }

  public void Initial(long id)
  {
    InitialStatusId = id;
  }
}

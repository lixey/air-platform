namespace Platform.App.Form;

public sealed class FormElements<TEntity> : Elements<TEntity>
  where TEntity : class, IEntity
{
  public ChildGridBuilder<TEntity, TChildEntity> AddChild<TChildGridService, TChildEntity>()
    where TChildEntity : class, IEntity
  {
    var builder = AddTypedChild<TChildGridService, TEntity, TChildEntity>();
    builder.ChildEntityType = typeof(TChildEntity);
    return builder;
  }

  public ChildGridBuilder<TEntity, TChildEntity> AddChild<TChildGridService, TChildEntity>(
    Expression<Func<TChildEntity, long>> foreignKey,
    Action<TChildEntity, TEntity>? preset = null)
    where TChildEntity : class, IEntity
  {
    var builder = AddTypedChild<TChildGridService, TEntity, TChildEntity>();
    builder.ChildEntityType = typeof(TChildEntity);
    builder.FilterProvider = (_, parentId, rawQuery) =>
    {
      Expression<Func<long>> idExpr = () => parentId;
      var param = foreignKey.Parameters[0];
      var body = Expression.Equal(foreignKey.Body, idExpr.Body);
      var predicate = Expression.Lambda<Func<TChildEntity, bool>>(body, param);
      return ((IQueryable<TChildEntity>)rawQuery).Where(predicate);
    };

    var expr = foreignKey.Body is UnaryExpression ue ? ue.Operand : foreignKey.Body;
    var directProp = expr is MemberExpression me && me.Expression == foreignKey.Parameters[0]
      ? me.Member as PropertyInfo
      : null;

    if (directProp is { CanWrite: true } || preset != null)
    {
      builder.PresetProvider = (ctxDb, parentId, childObj) =>
      {
        if (childObj is not TChildEntity child)
        {
          return;
        }

        if (directProp is { CanWrite: true })
        {
          directProp.SetValue(child, parentId);
        }

        if (preset != null)
        {
          var parent = LoadParentEntity(ctxDb, parentId);
          if (parent != null)
          {
            preset(child, parent);
          }
        }
      };
    }

    return builder;
  }

  public ChildGridBuilder<TEntity, TChildEntity> AddChild<TChildGridService, TChildEntity>(
    Expression<Func<TChildEntity, long?>> foreignKey,
    Action<TChildEntity, TEntity>? preset = null)
    where TChildEntity : class, IEntity
  {
    var builder = AddTypedChild<TChildGridService, TEntity, TChildEntity>();
    builder.ChildEntityType = typeof(TChildEntity);
    builder.FilterProvider = (_, parentId, rawQuery) =>
    {
      long? nullableParentId = parentId;
      Expression<Func<long?>> idExpr = () => nullableParentId;
      var param = foreignKey.Parameters[0];
      var body = Expression.Equal(foreignKey.Body, idExpr.Body);
      var predicate = Expression.Lambda<Func<TChildEntity, bool>>(body, param);
      return ((IQueryable<TChildEntity>)rawQuery).Where(predicate);
    };

    var expr = foreignKey.Body is UnaryExpression ue ? ue.Operand : foreignKey.Body;
    var directProp = expr is MemberExpression me && me.Expression == foreignKey.Parameters[0]
      ? me.Member as PropertyInfo
      : null;

    if (directProp is { CanWrite: true } || preset != null)
    {
      builder.PresetProvider = (ctxDb, parentId, childObj) =>
      {
        if (childObj is not TChildEntity child)
        {
          return;
        }

        if (directProp is { CanWrite: true })
        {
          directProp.SetValue(child, (long?)parentId);
        }

        if (preset != null)
        {
          var parent = LoadParentEntity(ctxDb, parentId);
          if (parent != null)
          {
            preset(child, parent);
          }
        }
      };
    }

    return builder;
  }

  public ChildGridBuilder<TEntity, TChildEntity> AddChild<TChildGridService, TChildEntity>(
    Func<TEntity, Expression<Func<TChildEntity, bool>>> filter,
    Action<TChildEntity, TEntity>? preset = null)
    where TChildEntity : class, IEntity =>
    AddChild<TChildGridService, TChildEntity>((_, parent) => filter(parent), preset);

  public ChildGridBuilder<TEntity, TChildEntity> AddChild<TChildGridService, TChildEntity>(
    Func<PlatformDbContext, TEntity, Expression<Func<TChildEntity, bool>>> filter,
    Action<TChildEntity, TEntity>? preset = null)
    where TChildEntity : class, IEntity
  {
    var builder = AddTypedChild<TChildGridService, TEntity, TChildEntity>();
    builder.ChildEntityType = typeof(TChildEntity);
    builder.FilterProvider = (ctxDb, parentId, rawQuery) =>
    {
      var parent = LoadParentEntity(ctxDb, parentId);
      if (parent == null)
      {
        return ((IQueryable<TChildEntity>)rawQuery).Where(_ => false);
      }

      var predicate = filter(ctxDb, parent);
      return ((IQueryable<TChildEntity>)rawQuery).Where(predicate);
    };

    if (preset != null)
    {
      builder.PresetProvider = (ctxDb, parentId, childObj) =>
      {
        if (childObj is not TChildEntity child)
        {
          return;
        }

        var parent = LoadParentEntity(ctxDb, parentId);
        if (parent != null)
        {
          preset(child, parent);
        }
      };
    }

    return builder;
  }

  private static TEntity? LoadParentEntity(PlatformDbContext ctxDb, long parentId) =>
    ctxDb.Set<TEntity>().Local.FirstOrDefault(e => e.Id == parentId)
    ?? ctxDb.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == parentId).GetAwaiter().GetResult();
}

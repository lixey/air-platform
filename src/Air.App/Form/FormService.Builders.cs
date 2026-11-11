using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
  where TEntity : class, IEntity, new()
{
  public sealed class Elements : Elements<TEntity>
  {
    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Expression<Func<TChildEntity, long>> foreignKey,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity
    {
      var builder = AddChild<TChildGridService>();
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

    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Expression<Func<TChildEntity, long?>> foreignKey,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity
    {
      var builder = AddChild<TChildGridService>();
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

    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Func<TEntity, Expression<Func<TChildEntity, bool>>> filter,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity =>
      AddChild<TChildGridService, TChildEntity>((_, parent) => filter(parent), preset);

    public ChildGridBuilder AddChild<TChildGridService, TChildEntity>(
      Func<PlatformDbContext, TEntity, Expression<Func<TChildEntity, bool>>> filter,
      Action<TChildEntity, TEntity>? preset = null)
      where TChildEntity : class, IEntity
    {
      var builder = AddChild<TChildGridService>();
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
      ctxDb.Set<TEntity>().Local.FirstOrDefault(e => e.Id == parentId);
  }

  public sealed class Parent
  {
    public Type? BoundParentServiceType { get; private set; }
    public Type? BoundParentEntityType => ParentScopeValidator.GetFormEntityType(BoundParentServiceType);
    public string? BoundParentService { get; private set; }
    public PropertyInfo? BoundProperty { get; private set; }
    public bool IsOptional { get; private set; }

    public bool MatchesService(string? parentService) =>
      ParentScopeValidator.MatchesService(parentService, BoundParentService, BoundParentServiceType);

    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      BoundProperty = ParentScopeValidator.ExtractBoundProperty(member);
    }

    public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false)
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      BoundProperty = ParentScopeValidator.ExtractBoundProperty(member);
    }
  }

  public sealed class Hooks
  {
    internal List<Func<FormLoadContext<TEntity>, CancellationToken, Task>> AfterLoadActions { get; } = [];
    internal List<Action<FormSaveContext<TEntity>>> ComputeActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity>, CancellationToken, Task>> BeforeSaveActions { get; } = [];
    internal List<Func<FormSaveContext<TEntity>, CancellationToken, Task>> AfterSaveActions { get; } = [];

    public void AfterLoad(Func<FormLoadContext<TEntity>, CancellationToken, Task> handler) => AfterLoadActions.Add(handler);

    public void AfterLoad(Action<FormLoadContext<TEntity>> handler) =>
      AfterLoadActions.Add((c, _) =>
      {
        handler(c);
        return Task.CompletedTask;
      });

    public void Compute(Action<FormSaveContext<TEntity>> handler) => ComputeActions.Add(handler);

    public void BeforeSave(Func<FormSaveContext<TEntity>, CancellationToken, Task> handler) => BeforeSaveActions.Add(handler);

    public void BeforeSave(Action<FormSaveContext<TEntity>> handler) =>
      BeforeSaveActions.Add((c, _) =>
      {
        handler(c);
        return Task.CompletedTask;
      });

    public void AfterSave(Func<FormSaveContext<TEntity>, CancellationToken, Task> handler) => AfterSaveActions.Add(handler);

    public void AfterSave(Action<FormSaveContext<TEntity>> handler) =>
      AfterSaveActions.Add((c, _) =>
      {
        handler(c);
        return Task.CompletedTask;
      });
  }

  public sealed class Rules
  {
    internal List<Func<FormSaveContext<TEntity>, RuleContext<TEntity>, CancellationToken, Task>> Validators { get; } = [];

    public RuleBuilder<TEntity, TProp> For<TProp>(Expression<Func<TEntity, TProp>> member)
    {
      var compiled = member.Compile();
      string? path = null;
      var expr = member.Body is UnaryExpression ue ? ue.Operand : member.Body;
      if (expr is MemberExpression me)
      {
        path = char.ToLowerInvariant(me.Member.Name[0]) + me.Member.Name[1..];
      }

      var builder = new RuleBuilder<TEntity, TProp>(ctx => compiled(ctx.Entity), path);
      Validators.Add((ctx, rCtx, ct) => builder.ValidateAsync(ctx, rCtx, ct));
      return builder;
    }
  }

  public sealed class Actions : FormActions<TEntity>;

  public sealed class Query
  {
    internal IQueryable<TEntity>? Source { get; private set; }
    internal List<Expression<Func<TEntity, bool>>> Filters { get; } = [];

    public Query From(IQueryable<TEntity> source)
    {
      Source = source;
      return this;
    }

    public Query Where(Expression<Func<TEntity, bool>> predicate)
    {
      Filters.Add(predicate);
      return this;
    }
  }
}

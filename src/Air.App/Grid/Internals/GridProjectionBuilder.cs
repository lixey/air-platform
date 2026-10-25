namespace Platform.App.Grid.Internals;

internal static class GridProjectionBuilder
{
  private static readonly ConcurrentDictionary<(Type EntityType, string Signature), LambdaExpression> Cache = new();

  public static Expression<Func<TEntity, TEntity>>? TryBuildSelector<TEntity>(
    IReadOnlyList<GridColumnDto> columns,
    bool hasCustomShapeRow)
    where TEntity : class, IEntity, new()
  {
    if (hasCustomShapeRow)
    {
      return null;
    }

    foreach (var col in columns)
    {
      if (col.ColorEvaluator != null && col.ColorExpression == null && string.IsNullOrEmpty(col.ColorNavPath))
      {
        return null;
      }
    }

    var entityType = typeof(TEntity);
    var root = new GridProjectionNode();

    foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      if (GridProjectionNode.CanBindScalar(prop))
      {
        root.Leaves.Add(prop);
      }
    }

    foreach (var col in columns)
    {
      if (!string.IsNullOrWhiteSpace(col.Path))
      {
        if (col.IsHidden && GridProjectionNode.IsRootFilterOnlyNotMapped(entityType, col.Path))
        {
          continue;
        }

        var members = GridProjectionNode.ResolveMembers(entityType, col.Path);
        if (members.Count == 0)
        {
          return null;
        }

        var isRef = col.ReferenceService != null ||
                    col.Cell is GridCellReferenceModel ||
                    !GridProjectionNode.IsScalar(members[^1].PropertyType);
        if (!root.TryAddPath(members, isRef, col.ReferenceLabelPaths))
        {
          return null;
        }
      }

      if (col.ColorExpression != null && !TryAddExpressionMemberPaths(entityType, col.ColorExpression, root))
      {
        return null;
      }

      if (!string.IsNullOrWhiteSpace(col.ColorNavPath))
      {
        var colorNavMembers = GridProjectionNode.ResolveMembers(entityType, col.ColorNavPath);
        if (colorNavMembers.Count == 0 || !GridProjectionNode.CanConstructNavigation(colorNavMembers[^1]))
        {
          return null;
        }

        var colorNode = root;
        foreach (var nav in colorNavMembers)
        {
          if (!GridProjectionNode.CanConstructNavigation(nav))
          {
            return null;
          }

          colorNode = colorNode.GetOrAddChild(nav);
        }

        var navType = colorNavMembers[^1].PropertyType;
        var handleProp = PropertyAccessorCache.GetProperty(navType, "Handle");
        if (handleProp != null && GridProjectionNode.CanBindScalar(handleProp))
        {
          colorNode.Leaves.Add(handleProp);
        }

        var nameProp = PropertyAccessorCache.GetProperty(navType, "Name");
        if (nameProp != null && GridProjectionNode.CanBindScalar(nameProp))
        {
          colorNode.Leaves.Add(nameProp);
        }
      }

      if (col.Cell is GridCellOpenFormModel { KeyPath: { Length: > 0 } keyPath })
      {
        var keyMembers = GridProjectionNode.ResolveMembers(entityType, keyPath);
        if (keyMembers.Count == 0 || !root.TryAddPath(keyMembers, isReference: false))
        {
          return null;
        }
      }
    }

    var sigBuilder = new StringBuilder(256);
    root.AppendSignature(sigBuilder, string.Empty);
    var signature = sigBuilder.ToString();

    return (Expression<Func<TEntity, TEntity>>)Cache.GetOrAdd((entityType, signature), _ =>
    {
      var param = Expression.Parameter(entityType, "e");
      var bindings = root.Bind(param).ToList();
      return Expression.Lambda<Func<TEntity, TEntity>>(
        Expression.MemberInit(Expression.New(entityType), bindings),
        param);
    });
  }

  private static bool TryAddExpressionMemberPaths(Type entityType, LambdaExpression lambda, GridProjectionNode root)
  {
    if (lambda.Parameters.Count == 0)
    {
      return true;
    }

    var param = lambda.Parameters[0];
    return CollectParameterMemberChains(entityType, lambda.Body, param, root);
  }

  private static bool CollectParameterMemberChains(
    Type entityType,
    Expression? expr,
    ParameterExpression param,
    GridProjectionNode root)
  {
    switch (expr)
    {
      case null:
        return true;

      case MemberExpression me:
        {
          var chain = new List<PropertyInfo>();
          Expression? cur = me;
          while (cur is MemberExpression m && m.Member is PropertyInfo pi)
          {
            chain.Add(pi);
            cur = m.Expression;
          }

          if (cur == param && chain.Count > 0)
          {
            chain.Reverse();
            if (chain.Any(p => !p.CanRead || !p.CanWrite || p.GetCustomAttribute<NotMappedAttribute>() != null))
            {
              return false;
            }

            var isRef = !GridProjectionNode.IsScalar(chain[^1].PropertyType);
            return root.TryAddPath(chain, isRef);
          }

          return CollectParameterMemberChains(entityType, me.Expression, param, root);
        }

      case UnaryExpression ue:
        return CollectParameterMemberChains(entityType, ue.Operand, param, root);

      case BinaryExpression be:
        return CollectParameterMemberChains(entityType, be.Left, param, root) &&
               CollectParameterMemberChains(entityType, be.Right, param, root);

      case ConditionalExpression ce:
        return CollectParameterMemberChains(entityType, ce.Test, param, root) &&
               CollectParameterMemberChains(entityType, ce.IfTrue, param, root) &&
               CollectParameterMemberChains(entityType, ce.IfFalse, param, root);

      case MethodCallExpression mce:
        if (!CollectParameterMemberChains(entityType, mce.Object, param, root))
        {
          return false;
        }

        foreach (var arg in mce.Arguments)
        {
          if (!CollectParameterMemberChains(entityType, arg, param, root))
          {
            return false;
          }
        }

        return true;

      default:
        return true;
    }
  }
}

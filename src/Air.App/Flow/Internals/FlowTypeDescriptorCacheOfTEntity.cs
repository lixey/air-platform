namespace Platform.App.Flow.Internals;

internal static class FlowTypeDescriptorCache<TEntity>
  where TEntity : class, IEntity, new()
{
  private static readonly ConcurrentDictionary<Type, (
    (Func<StatusFlowService<TEntity>, Transition<TEntity>?> Getter, bool IsPublic)[] Transitions,
    Func<StatusFlowService<TEntity>, StatusFlowService<TEntity>.ChildRule?>[] Rules
  )> PropertyAccessors = new();

  private static readonly ConcurrentDictionary<(Type ServiceType, MemberInfo? Member), (Func<TEntity, long> Reader, Action<TEntity, long>? Setter)> StateAccessors = new();
  private static readonly ConcurrentDictionary<(Type ChildType, Type ParentType), PropertyInfo?> ConventionParentKeys = new();
  private static readonly ConcurrentDictionary<Type, bool> CustomLockTypes = new();

  public static bool OverridesLockAsync(Type serviceType) =>
    CustomLockTypes.GetOrAdd(serviceType, static t =>
    {
      var m = t.GetMethod(
        "LockAsync",
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(IMutationContext), typeof(IReadOnlyList<TEntity>), typeof(CancellationToken)]);
      return m != null && m.DeclaringType != typeof(StatusFlowService<TEntity>);
    });

  public static (Func<TEntity, long> Reader, Action<TEntity, long>? Setter) GetStateAccessors(
    Type serviceType,
    Expression<Func<TEntity, long>> stateMember)
  {
    var unwrapped = stateMember.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } ue
      ? ue.Operand
      : stateMember.Body;
    var memberKey = unwrapped is MemberExpression { Expression: ParameterExpression, Member: { } member } ? member : null;
    if (memberKey is null)
    {
      return (stateMember.Compile(preferInterpretation: true), null);
    }

    return StateAccessors.GetOrAdd((serviceType, memberKey), _ => (stateMember.Compile(), BuildStatusSetter(unwrapped)));
  }

  public static (
    (Func<StatusFlowService<TEntity>, Transition<TEntity>?> Getter, bool IsPublic)[] Transitions,
    Func<StatusFlowService<TEntity>, StatusFlowService<TEntity>.ChildRule?>[] Rules
  ) GetPropertyAccessors(Type serviceType) =>
    PropertyAccessors.GetOrAdd(serviceType, static type =>
    {
      var allProps = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
      var serviceParam = Expression.Parameter(typeof(StatusFlowService<TEntity>), "s");
      var typedService = Expression.Convert(serviceParam, type);

      var transitions = allProps
        .Where(static p => p.GetIndexParameters().Length == 0 && p.GetMethod != null && typeof(Transition<TEntity>).IsAssignableFrom(p.PropertyType))
        .Select(p =>
        {
          var readProp = Expression.Convert(Expression.Property(typedService, p), typeof(Transition<TEntity>));
          var getter = Expression.Lambda<Func<StatusFlowService<TEntity>, Transition<TEntity>?>>(readProp, serviceParam).Compile();
          return (getter, p.GetMethod!.IsPublic);
        })
        .ToArray();

      var rules = allProps
        .Where(static p => p.GetIndexParameters().Length == 0 && p.GetMethod != null && typeof(StatusFlowService<TEntity>.ChildRule).IsAssignableFrom(p.PropertyType))
        .Select(p =>
        {
          var readProp = Expression.Convert(Expression.Property(typedService, p), typeof(StatusFlowService<TEntity>.ChildRule));
          return Expression.Lambda<Func<StatusFlowService<TEntity>, StatusFlowService<TEntity>.ChildRule?>>(readProp, serviceParam).Compile();
        })
        .ToArray();

      return (transitions, rules);
    });

  public static PropertyInfo? FindConventionParentKey(Type parentEntityType) =>
    ConventionParentKeys.GetOrAdd((typeof(TEntity), parentEntityType), static key =>
      key.ChildType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(p => (p.PropertyType == typeof(long) || p.PropertyType == typeof(long?)) &&
          p.Name.Length > 2 &&
          p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
          (string.Equals(p.Name, key.ParentType.Name + "Id", StringComparison.OrdinalIgnoreCase) ||
           key.ParentType.Name.EndsWith(p.Name[..^2], StringComparison.OrdinalIgnoreCase))));

  private static Action<TEntity, long>? BuildStatusSetter(Expression unwrappedBody)
  {
    if (unwrappedBody is not MemberExpression { Expression: ParameterExpression, Member: PropertyInfo { CanWrite: true } pi })
    {
      return null;
    }

    var entityParam = Expression.Parameter(typeof(TEntity), "e");
    var valParam = Expression.Parameter(typeof(long), "v");
    var propExpr = Expression.Property(entityParam, pi);
    var targetType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;

    if (targetType == typeof(long) || targetType.IsEnum || targetType.IsPrimitive)
    {
      var converted = pi.PropertyType == typeof(long)
        ? (Expression)valParam
        : Expression.Convert(valParam, pi.PropertyType);
      return Expression.Lambda<Action<TEntity, long>>(Expression.Assign(propExpr, converted), entityParam, valParam).Compile();
    }

    return (entity, val) => pi.SetValue(entity, Convert.ChangeType(val, targetType, CultureInfo.InvariantCulture));
  }
}

namespace Platform.App.Grid.Internals;

internal sealed class GridProjectionNode
{
  public HashSet<PropertyInfo> Leaves { get; } = [];
  public Dictionary<PropertyInfo, GridProjectionNode> Children { get; } = [];

  public GridProjectionNode GetOrAddChild(PropertyInfo navigation)
  {
    if (!Children.TryGetValue(navigation, out var child))
    {
      child = new GridProjectionNode();
      Children[navigation] = child;
      AddEntityId(navigation.PropertyType, child);
    }

    return child;
  }

  public bool TryAddPath(IReadOnlyList<PropertyInfo> members, bool isReference, IReadOnlyList<string>? referenceLabelPaths = null)
  {
    if (members.Count == 0)
    {
      return false;
    }

    var current = this;
    for (var i = 0; i < members.Count - 1; i++)
    {
      var nav = members[i];
      if (!CanConstructNavigation(nav))
      {
        return false;
      }

      current = current.GetOrAddChild(nav);
    }

    var last = members[^1];
    if (isReference && CanConstructNavigation(last))
    {
      var refNode = current.GetOrAddChild(last);
      return TryAddReferenceLeaves(last.PropertyType, refNode, referenceLabelPaths);
    }

    if (CanBindScalar(last))
    {
      current.Leaves.Add(last);
      return true;
    }

    return false;
  }

  public IEnumerable<MemberBinding> Bind(Expression source)
  {
    foreach (var leaf in Leaves.OrderBy(p => p.Name, StringComparer.Ordinal))
    {
      yield return Expression.Bind(leaf, Expression.Property(source, leaf));
    }

    foreach (var (nav, child) in Children.OrderBy(kv => kv.Key.Name, StringComparer.Ordinal))
    {
      var childBindings = child.Bind(Expression.Property(source, nav)).ToList();
      if (childBindings.Count == 0)
      {
        continue;
      }

      var navExpr = Expression.Property(source, nav);
      var init = Expression.MemberInit(Expression.New(nav.PropertyType), childBindings);
      var guarded = Expression.Condition(
        Expression.Equal(navExpr, Expression.Constant(null, nav.PropertyType)),
        Expression.Constant(null, nav.PropertyType),
        init);
      yield return Expression.Bind(nav, guarded);
    }
  }

  public void AppendSignature(StringBuilder sb, string prefix)
  {
    foreach (var leaf in Leaves.OrderBy(p => p.Name, StringComparer.Ordinal))
    {
      sb.Append(prefix).Append(leaf.Name).Append('|');
    }

    foreach (var (nav, child) in Children.OrderBy(kv => kv.Key.Name, StringComparer.Ordinal))
    {
      child.AppendSignature(sb, string.Concat(prefix, nav.Name, "."));
    }
  }

  internal static void AddEntityId(Type type, GridProjectionNode node)
  {
    var idProp = PropertyAccessorCache.GetProperty(type, nameof(IEntity.Id));
    if (idProp != null && CanBindScalar(idProp))
    {
      node.Leaves.Add(idProp);
    }
  }

  internal static bool TryAddReferenceLeaves(Type refType, GridProjectionNode refNode, IReadOnlyList<string>? labelPaths)
  {
    AddEntityId(refType, refNode);

    foreach (var candidate in ReferenceLabelCandidates)
    {
      var prop = PropertyAccessorCache.GetProperty(refType, candidate);
      if (prop != null && CanBindScalar(prop))
      {
        refNode.Leaves.Add(prop);
      }
    }

    if (labelPaths is { Count: > 0 })
    {
      foreach (var lp in labelPaths)
      {
        var subMembers = ResolveMembers(refType, lp);
        if (subMembers.Count == 0 || !refNode.TryAddPath(subMembers, isReference: false))
        {
          return false;
        }
      }
    }

    return true;
  }

  internal static List<PropertyInfo> ResolveMembers(Type rootType, string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return [];
    }

    var segments = path.Split('.');
    var result = new List<PropertyInfo>(segments.Length);
    var currentType = rootType;

    foreach (var seg in segments)
    {
      var prop = PropertyAccessorCache.GetProperty(currentType, seg);
      if (prop == null || !prop.CanRead || !prop.CanWrite || prop.GetCustomAttribute<NotMappedAttribute>() != null)
      {
        return [];
      }

      result.Add(prop);
      currentType = prop.PropertyType;
    }

    return result;
  }

  internal static bool IsRootFilterOnlyNotMapped(Type rootType, string path)
  {
    if (string.IsNullOrWhiteSpace(path) || path.Contains('.'))
    {
      return false;
    }

    var prop = PropertyAccessorCache.GetProperty(rootType, path);
    return prop == null || prop.GetCustomAttribute<NotMappedAttribute>() != null;
  }

  internal static bool CanBindScalar(PropertyInfo prop) =>
    prop.CanRead &&
    prop.CanWrite &&
    prop.GetCustomAttribute<NotMappedAttribute>() == null &&
    IsScalar(prop.PropertyType);

  internal static bool CanConstructNavigation(PropertyInfo prop) =>
    prop.CanRead &&
    prop.CanWrite &&
    prop.GetCustomAttribute<NotMappedAttribute>() == null &&
    !IsScalar(prop.PropertyType) &&
    !typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) &&
    prop.PropertyType.GetConstructor(Type.EmptyTypes) != null;

  internal static bool IsScalar(Type t)
  {
    var u = Nullable.GetUnderlyingType(t) ?? t;
    return u.IsValueType || u == typeof(string) || u == typeof(byte[]);
  }

  private static readonly string[] ReferenceLabelCandidates = ["Name", "Title", "Number", "Code", "Login"];
}

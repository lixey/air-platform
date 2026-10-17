namespace Air.Data.Internal;

internal sealed class QueryPlanner<TEntity>(QueryMap<TEntity> map) where TEntity : class
{
  private static readonly char[] WordBreaks = [' ', '\t', '\r', '\n'];

  private readonly ParameterExpression _entity = Expression.Parameter(typeof(TEntity), "e");
  private int _nodes;

  public QueryPlan<TEntity> Plan(ListRequest request)
  {
    var limits = map.Limits;
    var body = And(And(CompileFilter(request.Filter), CompileFilter(request.ColumnFilter)), CompileSearch(request));
    var predicate = body is null ? null : Expression.Lambda<Func<TEntity, bool>>(body, _entity);

    var page = Math.Max(1, request.Page);
    var pageSize = request.PageSize <= 0 ? limits.DefaultPageSize : Math.Min(request.PageSize, limits.MaxPageSize);
    return new QueryPlan<TEntity>(predicate, ResolveOrdering(request.Sort), page, pageSize);
  }

  private Expression? CompileFilter(FilterNode? root) => root is null ? null : CompileNode(root, 1);

  private Expression? CompileNode(FilterNode node, int depth)
  {
    if (++_nodes > map.Limits.MaxFilterNodes || depth > map.Limits.MaxFilterDepth)
    {
      throw new QueryRequestException(
        QueryError.FilterTooComplex,
        $"A filter holds at most {map.Limits.MaxFilterNodes} nodes, {map.Limits.MaxFilterDepth} levels deep.");
    }

    return node switch
    {
      FilterGroup group => CompileGroup(group, depth),
      FilterRule rule => CompileRule(rule),
      _ => throw new QueryRequestException(QueryError.InvalidValue, "A filter node is a group or a rule.")
    };
  }

  private Expression? CompileGroup(FilterGroup group, int depth)
  {
    if (group.Items.Count == 0)
    {
      return null;
    }

    if (!FilterLogic.IsValid(group.Logic))
    {
      throw new QueryRequestException(QueryError.InvalidValue, $"Filter logic '{group.Logic}' is invalid.");
    }

    var builtItems = new List<Expression>(group.Items.Count);
    foreach (var item in group.Items)
    {
      if (item is null)
      {
        throw new QueryRequestException(QueryError.InvalidValue, "A filter group holds an empty item.");
      }

      var built = CompileNode(item, depth + 1);
      if (built is not null)
      {
        builtItems.Add(built);
      }
    }

    if (builtItems.Count == 0)
    {
      return null;
    }

    var isAnd = !FilterLogic.IsOr(group.Logic);
    return Join(builtItems.ToArray(), isAnd, 0, builtItems.Count);
  }

  private static Expression Join(Expression[] children, bool isAnd, int start, int count)
  {
    if (count == 1)
    {
      return children[start];
    }

    if (count == 2)
    {
      return isAnd
        ? Expression.AndAlso(children[start], children[start + 1])
        : Expression.OrElse(children[start], children[start + 1]);
    }

    var mid = count / 2;
    var left = Join(children, isAnd, start, mid);
    var right = Join(children, isAnd, start + mid, count - mid);
    return isAnd ? Expression.AndAlso(left, right) : Expression.OrElse(left, right);
  }

  private Expression CompileRule(FilterRule rule)
  {
    var field = map.Require(rule.Field, FieldCapabilities.Filter);
    if (!field.Operators.Contains(rule.Operator))
    {
      throw new QueryRequestException(
        QueryError.OperatorNotAllowed, $"'{field.Name}' does not admit '{rule.Operator}'.", field.Name);
    }

    return PredicateFactory.Build(field, field.Bind(_entity), rule, map.Limits);
  }

  private Expression? CompileSearch(ListRequest request)
  {
    var text = request.Search?.Trim();
    if (string.IsNullOrEmpty(text))
    {
      return null;
    }

    if (text.Length > map.Limits.MaxSearchLength)
    {
      throw new QueryRequestException(QueryError.SearchTooLong, $"A search holds at most {map.Limits.MaxSearchLength} characters.");
    }

    var words = text.StartsWith('=') ? [text] : text.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);
    if (words.Length > map.Limits.MaxSearchWords)
    {
      throw new QueryRequestException(QueryError.SearchTooLong, $"A search holds at most {map.Limits.MaxSearchWords} words.");
    }

    var fields = (request.SearchFields is { Count: > 0 } named
        ? named.Select(name => map.Require(name, FieldCapabilities.Search))
        : map.Fields.Where(f => f.Can(FieldCapabilities.Search)))
      .DistinctBy(f => f.Bind(_entity), ExpressionEqualityComparer.Instance)
      .ToList();

    Expression? result = null;
    foreach (var pattern in words.Select(TextPattern.Parse).Where(p => !p.IsEmpty))
    {
      var anywhere = fields.Aggregate((Expression?)null, (found, field) => Or(found, CompileWord(field, pattern)));
      result = And(result, anywhere ?? Expression.Constant(false));
    }

    return result;
  }

  private Expression? CompileWord(QueryField field, TextPattern pattern)
  {
    var member = field.Bind(_entity);
    if (field.Kind == FieldKind.Text)
    {
      return PredicateFactory.TextMatch(field, member, pattern);
    }

    return pattern.IsPlain && RequestValue.TryConvert(pattern.Term, field.ValueType, out var typed)
      ? PredicateFactory.Equality(field, member, typed)
      : null;
  }

  private List<SortKey> ResolveOrdering(List<SortRule>? rules)
  {
    var terms = rules is { Count: > 0 } ? ResolveTerms(rules) : map.DefaultSort.ToList();
    if (!terms.Exists(t => ReferenceEquals(t.Field.Expression, map.Key.Expression)))
    {
      terms.Add(new SortTerm(map.Key, terms.Count == 0 || terms[^1].Descending));
    }

    return terms.SelectMany(ToSortKeys).ToList();
  }

  private List<SortTerm> ResolveTerms(List<SortRule> rules)
  {
    if (rules.Count > map.Limits.MaxSortRules)
    {
      throw new QueryRequestException(QueryError.TooManySortRules, $"An order holds at most {map.Limits.MaxSortRules} levels.");
    }

    var terms = new List<SortTerm>();
    foreach (var rule in rules)
    {
      var field = map.Require(rule?.Field, FieldCapabilities.Sort);

      if (!SortDirection.Asc.Equals(rule!.Direction, StringComparison.OrdinalIgnoreCase) &&
          !SortDirection.Desc.Equals(rule!.Direction, StringComparison.OrdinalIgnoreCase))
      {
        throw new QueryRequestException(QueryError.InvalidValue, $"Sort direction '{rule.Direction}' is invalid.");
      }

      if (!terms.Exists(t => t.Field == field))
      {
        terms.Add(new SortTerm(field, SortDirection.Desc.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase)));
      }
    }

    return terms;
  }

  private IEnumerable<SortKey> ToSortKeys(SortTerm term)
  {
    var member = term.Field.Bind(_entity);
    if (map.NullOrdering != NullOrdering.Native && term.Field.IsNullableInStore)
    {
      var isNull = PredicateFactory.IsNull(term.Field, member);
      yield return new SortKey(Expression.Lambda(isNull, _entity), map.NullOrdering == NullOrdering.NullsFirst);
    }

    yield return new SortKey(Expression.Lambda(member, _entity), term.Descending);
  }

  private static Expression? And(Expression? left, Expression? right) =>
    left is null ? right : right is null ? left : Expression.AndAlso(left, right);

  private static Expression? Or(Expression? left, Expression? right) =>
    left is null ? right : right is null ? left : Expression.OrElse(left, right);
}

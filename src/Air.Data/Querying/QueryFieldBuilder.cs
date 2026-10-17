namespace Air.Data.Querying;

public sealed class QueryFieldBuilder
{
  private FieldCapabilities? _exact;
  private FieldCapabilities _added;
  private FieldCapabilities _removed;

  internal HashSet<string>? AllowedOperators { get; private set; }

  public QueryFieldBuilder Capabilities(FieldCapabilities capabilities)
  {
    _exact = capabilities;
    return this;
  }

  public QueryFieldBuilder FilterOnly() => Capabilities(FieldCapabilities.Filter);
  public QueryFieldBuilder SortOnly() => Capabilities(FieldCapabilities.Sort);

  public QueryFieldBuilder Searchable(bool value = true) => Toggle(FieldCapabilities.Search, value);
  public QueryFieldBuilder Sortable(bool value = true) => Toggle(FieldCapabilities.Sort, value);
  public QueryFieldBuilder Filterable(bool value = true) => Toggle(FieldCapabilities.Filter, value);

  public QueryFieldBuilder Operators(params string[] operators)
  {
    AllowedOperators = new HashSet<string>(operators, StringComparer.OrdinalIgnoreCase);
    return this;
  }

  internal FieldCapabilities Resolve(FieldKind kind) => ((_exact ?? DefaultsFor(kind)) | _added) & ~_removed;

  private QueryFieldBuilder Toggle(FieldCapabilities capability, bool value)
  {
    if (value)
    {
      _added |= capability;
      _removed &= ~capability;
    }
    else
    {
      _removed |= capability;
      _added &= ~capability;
    }

    return this;
  }

  private static FieldCapabilities DefaultsFor(FieldKind kind) =>
    kind == FieldKind.Text ? FieldCapabilities.All : FieldCapabilities.Filter | FieldCapabilities.Sort;
}

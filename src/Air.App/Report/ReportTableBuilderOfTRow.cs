using Platform.App.Report.Internals;

namespace Platform.App.Report;

public sealed class ReportTableBuilder<TRow>
{
  private readonly List<TRow> _sourceRows;
  private readonly IPlatformService? _host;
  private readonly PlatformAppService? _hostApp;
  private readonly ILocalizer? _localizer;
  private readonly List<(ReportColumnDto Column, Func<TRow, object?> Value, Func<TRow, string?>? Color)> _columns = [];

  private string? _key;
  private string? _title;
  private string? _subtitle;
  private string? _tab;
  private bool _searchable;
  private bool _sortable;
  private bool _compact;
  private int? _maxHeight;
  private string? _emptyText;
  private string? _filterField;
  private List<ReportFilterItemDto>? _filterChips;
  private List<RowDto>? _bottomRows;
  private Func<TRow, long>? _idSelector;
  private Type? _openFormType;
  private string? _openFormService;
  private bool _openEnabled;

  internal ReportTableBuilder(IEnumerable<TRow> rows, IPlatformService? host)
  {
    ArgumentNullException.ThrowIfNull(rows);
    _sourceRows = rows as List<TRow> ?? rows.ToList();
    _host = host;
    _hostApp = ResolveHostApp(host);
    if (host?.Feature is { } feat)
    {
      _localizer = (_hostApp?.L ?? Localizer.Instance).For(feat.Module);
    }
  }

  public ReportTableBuilder<TRow> Key(string key)
  {
    _key = key;
    return this;
  }

  public ReportTableBuilder<TRow> Title(string title)
  {
    _title = Localize(title);
    return this;
  }

  public ReportTableBuilder<TRow> Subtitle(string subtitle)
  {
    _subtitle = Localize(subtitle);
    return this;
  }

  public ReportTableBuilder<TRow> Tab(string tab)
  {
    _tab = Localize(tab);
    return this;
  }

  public ReportTableBuilder<TRow> Searchable(bool searchable = true)
  {
    _searchable = searchable;
    return this;
  }

  public ReportTableBuilder<TRow> Sortable(bool sortable = true)
  {
    _sortable = sortable;
    return this;
  }

  public ReportTableBuilder<TRow> Compact(bool compact = true)
  {
    _compact = compact;
    return this;
  }

  public ReportTableBuilder<TRow> MaxHeight(int pixels)
  {
    _maxHeight = pixels;
    return this;
  }

  public ReportTableBuilder<TRow> EmptyText(string emptyText)
  {
    _emptyText = Localize(emptyText);
    return this;
  }

  public ReportTableBuilder<TRow> FilterChips(string filterField, IEnumerable<ReportFilterItemDto> chips)
  {
    _filterField = filterField;
    _filterChips = [.. chips];
    return this;
  }

  public ReportTableBuilder<TRow> BottomRows(params ReadOnlySpan<RowDto> rows)
  {
    _bottomRows = [.. rows];
    return this;
  }

  public ReportTableBuilder<TRow> RowId(Func<TRow, long> idSelector)
  {
    ArgumentNullException.ThrowIfNull(idSelector);
    _idSelector = idSelector;
    return this;
  }

  public ReportTableBuilder<TRow> OpenWith<TForm>(Func<TRow, long> idSelector, bool when = true)
    where TForm : class, IPlatformService
  {
    ArgumentNullException.ThrowIfNull(idSelector);
    if (_host != null)
    {
      var catalog = _hostApp?.Catalog ?? ModuleCatalog.Instance;
      ModuleCatalog.EnsureModuleServiceAccessAllowed(_host.GetType(), typeof(TForm), "ReportTable.OpenWith", catalog);
    }

    _idSelector = idSelector;
    _openFormType = typeof(TForm);
    _openFormService = ServicePathResolver.Resolve<TForm>();
    _openEnabled = when;
    return this;
  }

  public ReportTableBuilder<TRow> Column(
    string key,
    string label,
    Func<TRow, object?> value,
    string kind = ReportColumnKind.Text,
    int? width = null,
    string? currency = null,
    int? decimals = null,
    string? align = null,
    string? sticky = null,
    bool? sortable = null,
    Func<TRow, string?>? color = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    ArgumentNullException.ThrowIfNull(value);

    _columns.Add((
      new ReportColumnDto
      {
        Key = key,
        Label = Localize(label),
        Kind = kind,
        Width = width,
        Currency = currency,
        Decimals = decimals,
        Align = align,
        Sticky = sticky,
        Sortable = sortable
      },
      value,
      color));
    return this;
  }

  public ReportTableBuilder<TRow> Text(
    string key,
    string label,
    Func<TRow, object?> value,
    int? width = null,
    string? sticky = null) =>
    Column(key, label, value, ReportColumnKind.Text, width: width, sticky: sticky);

  public ReportTableBuilder<TRow> Date(
    string key,
    string label,
    Func<TRow, object?> value,
    int? width = null) =>
    Column(key, label, value, ReportColumnKind.Date, width: width);

  public ReportTableBuilder<TRow> Badge(
    string key,
    string label,
    Func<TRow, object?> value,
    Func<TRow, string?>? color = null,
    int? width = null) =>
    Column(key, label, value, ReportColumnKind.Badge, width: width, color: color);

  public ReportTableBuilder<TRow> Money(
    string key,
    string label,
    Func<TRow, decimal> value,
    string? currency = null,
    int? width = null,
    int? decimals = null) =>
    Column(key, label, r => value(r), ReportColumnKind.Money, width: width, currency: currency, decimals: decimals);

  public ReportTableBuilder<TRow> Number(
    string key,
    string label,
    Func<TRow, object?> value,
    int? width = null,
    int? decimals = null) =>
    Column(key, label, value, ReportColumnKind.Number, width: width, decimals: decimals);

  public ReportTableBuilder<TRow> Percent(
    string key,
    string label,
    Func<TRow, decimal> value,
    int? width = null,
    int? decimals = null) =>
    Column(key, label, r => value(r), ReportColumnKind.Percent, width: width, decimals: decimals);

  public ReportTableDto Build()
  {
    var canOpen = _openEnabled && !string.IsNullOrEmpty(_openFormService);
    if (canOpen && _openFormType != null && _hostApp is { } platformApp)
    {
      var targetFeat = platformApp.Catalog.FindFeature(_openFormType);
      if (targetFeat != null && !ServiceEndpointsExtensions.CanView(targetFeat, platformApp.User))
      {
        canOpen = false;
      }
    }

    var rows = new List<RowDto>(_sourceRows.Count);
    foreach (var item in _sourceRows)
    {
      var id = _idSelector?.Invoke(item);
      var cells = new Dictionary<string, object?>(_columns.Count, StringComparer.Ordinal);
      Dictionary<string, string>? colors = null;

      foreach (var (col, valueGetter, colorGetter) in _columns)
      {
        cells[col.Key] = valueGetter(item);
        if (colorGetter?.Invoke(item) is { Length: > 0 } hue)
        {
          colors ??= new Dictionary<string, string>(StringComparer.Ordinal);
          colors[col.Key] = hue;
        }
      }

      rows.Add(new RowDto
      {
        Id = id,
        Open = canOpen && id is > 0
          ? new OpenTargetDto { Service = _openFormService!, Id = id.Value }
          : null,
        Cells = cells,
        Colors = colors
      });
    }

    var table = new ReportTableDto
    {
      Key = _key,
      Title = _title,
      Subtitle = _subtitle,
      Tab = _tab,
      Searchable = _searchable,
      Sortable = _sortable,
      Compact = _compact,
      MaxHeight = _maxHeight,
      EmptyText = _emptyText,
      FilterField = _filterField,
      FilterChips = _filterChips,
      Columns = [.. _columns.Select(static c => c.Column)],
      Rows = rows,
      BottomRows = _bottomRows
    };

    if (_openFormType != null)
    {
      ReportTableRuntime.RegisterOpenFormType(table, _openFormType);
    }

    return table;
  }

  public static implicit operator ReportTableDto(ReportTableBuilder<TRow> builder) => builder.Build();

  private static PlatformAppService? ResolveHostApp(IPlatformService? host) => host switch
  {
    ReportService report => report.HostApp,
    Form.IFormService form => form.HostApp,
    _ => null
  };

  private string Localize(string text) =>
    _localizer != null && !string.IsNullOrEmpty(text) ? _localizer[text] : text;
}

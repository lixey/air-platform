using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Xl = DocumentFormat.OpenXml.Spreadsheet;

namespace Platform.App.Grid.Internals;

/// <summary>
/// Writes the rows of a grid into one worksheet: a bold frozen header of the column labels, typed cells,
/// an auto-filter over the data and, when the export was cut at its limit, a note under the data.
/// </summary>
internal static partial class GridExcelWriter
{
  public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

  private const uint DateFormatId = 14;
  private const uint DateTimeFormatId = 22;
  private const uint TimeFormatId = 21;
  private const uint FirstCustomFormatId = 164;

  private const uint PlainStyle = 0;
  private const uint HeaderStyle = 1;
  private const uint NoteStyle = 2;
  private const uint DateStyle = 3;
  private const uint DateTimeStyle = 4;
  private const uint TimeStyle = 5;

  private const int SheetNameLength = 31;
  private const int RowsSized = 200;
  private const double MaxColumnWidth = 255;

  public static async Task<(int Rows, bool IsTruncated)> WriteAsync(
    Stream stream,
    string sheetLabel,
    IReadOnlyList<GridColumnDto> columns,
    Func<GridColumnDto, Type?> memberTypeOf,
    IAsyncEnumerable<object> rows,
    Localizer localizer,
    TimeZoneInfo zone,
    int limit,
    string truncationNote,
    CancellationToken cancellationToken = default
  )
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
    ArgumentNullException.ThrowIfNull(zone);
    ArgumentNullException.ThrowIfNull(memberTypeOf);
    var sheet = new SheetWriter(columns, memberTypeOf, localizer["Yes"], localizer["No"], zone);
    await using var source = rows.GetAsyncEnumerator(cancellationToken);

    var headSize = Math.Min(RowsSized, limit);
    var head = new List<object>(headSize);
    var isTruncated = false;
    while (head.Count < headSize && await source.MoveNextAsync())
    {
      head.Add(source.Current);
    }

    var name = SheetNameOf(sheetLabel);
    using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
    {
      var workbook = document.AddWorkbookPart();
      var part = workbook.AddNewPart<WorksheetPart>();
      using (var writer = OpenXmlWriter.Create(part))
      {
        writer.WriteStartElement(new Xl.Worksheet());
        writer.WriteElement(FrozenHeader());
        if (columns.Count > 0)
        {
          writer.WriteElement(sheet.Widths(head));
        }

        writer.WriteStartElement(new Xl.SheetData());
        sheet.WriteHeader(writer);
        foreach (var row in head)
        {
          sheet.WriteRow(writer, row);
        }

        if (head.Count == headSize)
        {
          while (await source.MoveNextAsync())
          {
            if (sheet.Rows == limit)
            {
              isTruncated = true;
              break;
            }

            sheet.WriteRow(writer, source.Current);
          }
        }

        if (isTruncated)
        {
          sheet.WriteNote(writer, truncationNote);
        }

        writer.WriteEndElement();
        if (columns.Count > 0)
        {
          writer.WriteElement(new Xl.AutoFilter { Reference = sheet.DataRange });
        }

        writer.WriteEndElement();
      }

      workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = sheet.Styles();
      workbook.Workbook = new Xl.Workbook(new Xl.Sheets(new Xl.Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = name }));
      if (columns.Count > 0)
      {
        workbook.Workbook.Append(new Xl.DefinedNames(new Xl.DefinedName
        {
          Name = "_xlnm._FilterDatabase", LocalSheetId = 0, Hidden = true, Text = $"'{name.Replace("'", "''")}'!{sheet.AbsoluteDataRange}"
        }));
      }
    }

    return (sheet.Rows, isTruncated);
  }

  internal static string SheetNameOf(string label)
  {
    var name = ForbiddenInSheetName().Replace(label, "_").Trim('\'', ' ');
    if (name.Length > SheetNameLength)
    {
      name = name[..SheetNameLength].TrimEnd('\'', ' ');
    }

    return name.Length == 0 ? "Data" : name;
  }

  private static Xl.SheetViews FrozenHeader() =>
    new(new Xl.SheetView(
      new Xl.Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = Xl.PaneValues.BottomLeft, State = Xl.PaneStateValues.Frozen },
      new Xl.Selection { Pane = Xl.PaneValues.BottomLeft }
    )
    { TabSelected = true, WorkbookViewId = 0 });

  private static string ColumnName(int index)
  {
    var name = string.Empty;
    for (var n = index + 1; n > 0; n = (n - 1) / 26)
    {
      name = (char)('A' + (n - 1) % 26) + name;
    }

    return name;
  }

  private static string Escaped(string text)
  {
    if (!text.Any(c => !XmlConvert.IsXmlChar(c)) && !EscapeLike().IsMatch(text))
    {
      return text;
    }

    var escaped = new StringBuilder(text.Length + 16);
    for (var i = 0; i < text.Length; i++)
    {
      var c = text[i];
      if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], c))
      {
        escaped.Append(c).Append(text[++i]);
      }
      else if (!XmlConvert.IsXmlChar(c))
      {
        escaped.Append("_x").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
      }
      else if (c == '_' && EscapeLike().Match(text, i) is { Success: true, Index: var at } && at == i)
      {
        escaped.Append("_x005F_");
      }
      else
      {
        escaped.Append(c);
      }
    }

    return escaped.ToString();
  }

  [GeneratedRegex(@"[\\/*?:\[\]]")]
  private static partial Regex ForbiddenInSheetName();

  [GeneratedRegex("_x[0-9A-Fa-f]{4}_")]
  private static partial Regex EscapeLike();

  private sealed class SheetWriter
  {
    private readonly IReadOnlyList<GridColumnDto> _columns;
    private readonly Func<GridColumnDto, Type?> _memberTypeOf;
    private readonly string[] _names;
    private readonly uint[] _numberStyles;
    private readonly List<string> _numberFormats = [];
    private readonly string _yes;
    private readonly string _no;
    private readonly TimeZoneInfo _zone;
    private Func<object, object?>[]? _getters;

    public SheetWriter(
      IReadOnlyList<GridColumnDto> columns,
      Func<GridColumnDto, Type?> memberTypeOf,
      string yes,
      string no,
      TimeZoneInfo zone
    )
    {
      _columns = columns;
      _memberTypeOf = memberTypeOf;
      _yes = yes;
      _no = no;
      _zone = zone;
      _names = columns.Select((_, i) => ColumnName(i)).ToArray();
      _numberStyles = columns.Select(NumberStyleOf).ToArray();
    }

    public int Rows { get; private set; }
    public string DataRange => $"A1:{_names[^1]}{Rows + 1}";
    public string AbsoluteDataRange => $"$A$1:${_names[^1]}${Rows + 1}";

    public Xl.Columns Widths(IReadOnlyList<object> head)
    {
      var lengths = _columns.Select(c => HeaderOf(c).Length).ToArray();
      foreach (var row in head)
      {
        var getters = GettersOf(row);
        for (var c = 0; c < _columns.Count; c++)
        {
          lengths[c] = Math.Max(lengths[c], TextLengthOf(_columns[c], getters[c](row)));
        }
      }

      var widths = new Xl.Columns();
      for (var c = 0; c < _columns.Count; c++)
      {
        var width = Math.Min(MaxColumnWidth, Math.Round(lengths[c] * 1.1 + 2, 2));
        widths.Append(new Xl.Column { Min = (uint)c + 1, Max = (uint)c + 1, Width = width, CustomWidth = true });
      }

      return widths;
    }

    public void WriteHeader(OpenXmlWriter writer)
    {
      writer.WriteStartElement(new Xl.Row { RowIndex = 1, StyleIndex = HeaderStyle, CustomFormat = true });
      for (var c = 0; c < _columns.Count; c++)
      {
        writer.WriteElement(TextCell($"{_names[c]}1", HeaderOf(_columns[c]), HeaderStyle));
      }

      writer.WriteEndElement();
    }

    public void WriteRow(OpenXmlWriter writer, object row)
    {
      var getters = GettersOf(row);
      Rows++;
      var index = (uint)Rows + 1;
      writer.WriteStartElement(new Xl.Row { RowIndex = index });
      for (var c = 0; c < _columns.Count; c++)
      {
        if (CellOf($"{_names[c]}{index}", c, getters[c](row)) is { } cell)
        {
          writer.WriteElement(cell);
        }
      }

      writer.WriteEndElement();
    }

    public void WriteNote(OpenXmlWriter writer, string note)
    {
      var index = (uint)Rows + 3;
      writer.WriteStartElement(new Xl.Row { RowIndex = index });
      writer.WriteElement(TextCell($"A{index}", note, NoteStyle));
      writer.WriteEndElement();
    }

    public Xl.Stylesheet Styles()
    {
      var styles = new Xl.Stylesheet();
      if (_numberFormats.Count > 0)
      {
        styles.Append(new Xl.NumberingFormats(_numberFormats.Select((code, i) =>
          new Xl.NumberingFormat { NumberFormatId = FirstCustomFormatId + (uint)i, FormatCode = code }))
        {
          Count = (uint)_numberFormats.Count
        });
      }

      styles.Append(
        new Xl.Fonts(Font(), Font(new Xl.Bold()), Font(new Xl.Italic())) { Count = 3 },
        new Xl.Fills(new Xl.Fill(new Xl.PatternFill { PatternType = Xl.PatternValues.None }),
          new Xl.Fill(new Xl.PatternFill { PatternType = Xl.PatternValues.Gray125 }))
        { Count = 2 },
        new Xl.Borders(new Xl.Border(new Xl.LeftBorder(), new Xl.RightBorder(), new Xl.TopBorder(), new Xl.BottomBorder(), new Xl.DiagonalBorder()))
        {
          Count = 1
        },
        new Xl.CellStyleFormats(new Xl.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 }
      );

      var formats = new List<Xl.CellFormat>
      {
        Format(0, 0),
        Format(0, 1),
        Format(0, 2),
        Format(DateFormatId, 0),
        Format(DateTimeFormatId, 0),
        Format(TimeFormatId, 0)
      };
      formats.AddRange(_numberFormats.Select((_, i) => Format(FirstCustomFormatId + (uint)i, 0)));
      styles.Append(
        new Xl.CellFormats(formats) { Count = (uint)formats.Count },
        new Xl.CellStyles(new Xl.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 }
      );
      return styles;
    }

    private static Xl.Font Font(params OpenXmlElement[] emphasis) =>
      new(emphasis.Concat<OpenXmlElement>([new Xl.FontSize { Val = 11 }, new Xl.FontName { Val = "Calibri" }]));

    private static Xl.CellFormat Format(uint numberFormat, uint font) => new()
    {
      NumberFormatId = numberFormat,
      FontId = font,
      FillId = 0,
      BorderId = 0,
      FormatId = 0,
      ApplyNumberFormat = numberFormat != 0,
      ApplyFont = font != 0
    };

    private string HeaderOf(GridColumnDto column)
    {
      var label = column.Label ?? column.Path ?? string.Empty;
      return column.TemporalType == UiTemporalType.DateTimeOffset ? $"{label} ({_zone.Id})" : label;
    }

    private Func<object, object?>[] GettersOf(object row) =>
      _getters ??= _columns.Select(column => RowPathReader.Compile(row.GetType(), column.Path!) ?? (_ => null)).ToArray();

    private uint NumberStyleOf(GridColumnDto column)
    {
      var decimals = column.Cell switch
      {
        GridCellPercentModel percent => percent.Decimals ?? 2,
        GridCellMoneyModel money => money.Decimals,
        GridCellNumberModel number => number.Decimals,
        _ => null
      };
      if (decimals is not { } places || places < 0)
      {
        return PlainStyle;
      }

      var code = places == 0 ? "0" : "0." + new string('0', places);
      if (column.Cell is GridCellPercentModel)
      {
        code += "\"%\"";
      }

      var index = _numberFormats.IndexOf(code);
      if (index < 0)
      {
        _numberFormats.Add(code);
        index = _numberFormats.Count - 1;
      }

      return TimeStyle + 1 + (uint)index;
    }

    private Xl.Cell? CellOf(string at, int column, object? value) =>
      value switch
      {
        null or DBNull => null,
        Reference reference => TextCell(at, reference.Name, PlainStyle),
        IDictionary<string, object?> dict when dict.TryGetValue("name", out var n) => TextCell(at, n?.ToString() ?? string.Empty, PlainStyle),
        bool flag => TextCell(at, flag ? _yes : _no, PlainStyle),
        DateOnly date => NumberCell(at, date.ToDateTime(TimeOnly.MinValue).ToOADate(), DateStyle),
        DateTime stamp => StampCell(at, _columns[column], stamp),
        DateTimeOffset instant => StampCell(at, _columns[column], TimeZoneInfo.ConvertTime(instant, _zone).DateTime),
        TimeOnly time => NumberCell(at, time.ToTimeSpan().TotalDays, TimeStyle),
        TimeSpan span => NumberCell(at, span.TotalDays, TimeStyle),
        decimal number => NumberCell(at, (double)number, _numberStyles[column]),
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double =>
          NumberCell(at, Convert.ToDouble(value, CultureInfo.InvariantCulture), _numberStyles[column]),
        _ => TextCell(at, Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty, PlainStyle)
      };

    private static Xl.Cell StampCell(string reference, GridColumnDto column, DateTime stamp) =>
      column.Type == UiFieldType.Date
        ? NumberCell(reference, stamp.Date.ToOADate(), DateStyle)
        : NumberCell(reference, stamp.ToOADate(), DateTimeStyle);

    private static Xl.Cell NumberCell(string reference, double number, uint style) =>
      double.IsFinite(number)
        ? new Xl.Cell
        {
          CellReference = reference,
          StyleIndex = style,
          CellValue = new Xl.CellValue(number.ToString("R", CultureInfo.InvariantCulture))
        }
        : TextCell(reference, number.ToString(CultureInfo.InvariantCulture), PlainStyle);

    private static Xl.Cell TextCell(string reference, string text, uint style) => new()
    {
      CellReference = reference,
      StyleIndex = style,
      DataType = Xl.CellValues.InlineString,
      InlineString = new Xl.InlineString(new Xl.Text(Escaped(text)) { Space = SpaceProcessingModeValues.Preserve })
    };

    private int TextLengthOf(GridColumnDto column, object? value) =>
      value switch
      {
        null or DBNull => 0,
        Reference reference => reference.Name?.Length ?? 0,
        bool flag => (flag ? _yes : _no).Length,
        DateOnly => 10,
        DateTime or DateTimeOffset => column.Type == UiFieldType.Date ? 10 : 16,
        TimeOnly or TimeSpan => 8,
        _ => (Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty).Split('\n').Max(line => line.Length)
      };
  }
}

using Air.App.Form;
using Air.App.Nav;
using Air.App.Overlay;
using Air.Data.Contracts;

namespace Air.App.Grid;

[UiContract]
public sealed class MutationTextsDto
{
  public string ConflictTitle { get; set; } = string.Empty;
  public string ReloadLabel { get; set; } = string.Empty;
  public string UnknownTitle { get; set; } = string.Empty;
  public string UnknownDescription { get; set; } = string.Empty;
  public string VerifyLabel { get; set; } = string.Empty;
  public string CommittedWarningTitle { get; set; } = string.Empty;
  public string SelectionCorrectionLabel { get; set; } = string.Empty;
}

[UiContract]
public sealed record GridModel : PanelModel
{
  public string Service { get; set; } = string.Empty;
  public List<GridColumnDto> Columns { get; set; } = [];
  public List<List<GridHeaderCellDto>> Headers { get; set; } = [];
  public NavModel Nav { get; set; } = new();
  public NavModel ActionNav { get; set; } = new();
  public int PageSize { get; set; } = 25;
  public bool IsSelectable { get; set; } = true;
  public bool IsReadOnly { get; set; }
  public MutationTextsDto MutationTexts { get; set; } = new();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayModelDto? Form { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? InitialSearch { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? InitialFilter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool FocusesSearch { get; set; }
}

[UiContract]
public sealed class GridHeaderCellDto
{
  public string Label { get; set; } = string.Empty;
  public int Colspan { get; set; } = 1;
  public HeaderModel Model { get; set; } = new GridHeaderLabelModel();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }
}

[UiContract]
public sealed class GridNumberEditDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public double? Step { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public double? Min { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public double? Max { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool HasStepper { get; set; }
}

[UiContract]
public sealed class GridFilterOperatorDto
{
  [OneOf(typeof(FilterOperator))]
  public string Handle { get; set; } = FilterOperator.Equals;
  public string Label { get; set; } = string.Empty;
  public string? Symbol { get; set; }
}

[UiContract]
public sealed class GridColumnDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? FilterPath { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  [OneOf(typeof(UiFieldType))]
  public string Type { get; set; } = UiFieldType.Text;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiTemporalType))]
  public string? TemporalType { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Width { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxWidth { get; set; }

  public bool IsSortable { get; set; } = true;
  public bool IsFilterable { get; set; } = true;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<GridFilterOperatorDto>? Operators { get; set; }

  public bool IsResizable { get; set; } = true;
  public bool IsHidden { get; set; }
  public bool IsDisabled { get; set; }
  public bool IsRequired { get; set; }
  public bool IsSticky { get; set; }
  public bool IsStickyRight { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsWrapped { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsEditable { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool ConfirmsEdit { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ConfirmText { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ReferenceService { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? ReferenceLabelPaths { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? ReferenceFilter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public GridNumberEditDto? NumberEdit { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<OptionDto>? Options { get; set; }

  public CellModel Cell { get; set; } = new GridCellTextModel();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public HeaderModel? Header { get; set; }

  [JsonIgnore]
  internal Func<object, string?>? ColorEvaluator { get; set; }

  [JsonIgnore]
  internal System.Linq.Expressions.LambdaExpression? ColorExpression { get; set; }

  [JsonIgnore]
  internal string? ColorNavPath { get; set; }

  [JsonIgnore]
  internal bool IsSummed { get; set; }

  [JsonIgnore]
  internal bool IsComputed { get; set; }

  [JsonIgnore]
  internal Type? MemberType { get; set; }

  [JsonIgnore]
  internal Func<object, Type?>? FormServiceResolver { get; set; }

  [JsonIgnore]
  internal System.Linq.Expressions.LambdaExpression? FilterExpression { get; set; }
}

// Concrete Cell Models
[UiContract]
public sealed record GridCellTextModel : CellModel;

[UiContract]
public sealed record GridCellNumberModel : CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Decimals { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? IsGrouped { get; set; }
}

[UiContract]
public sealed record GridCellDateModel : CellModel;

[UiContract]
public sealed record GridCellDatetimeModel : CellModel;

[UiContract]
public sealed record GridCellTimeModel : CellModel;

[UiContract]
public sealed record GridCellCheckboxModel : CellModel;

[UiContract]
public sealed record GridCellOpenFormModel : CellModel;

[UiContract]
public sealed record GridCellSelectModel : CellModel;

[UiContract]
public sealed record GridCellMoneyModel : CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Decimals { get; set; }
}

[UiContract]
public sealed record GridCellPercentModel : CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Decimals { get; set; }
}

[UiContract]
public sealed record GridCellPhoneModel : CellModel;

[UiContract]
public sealed record GridCellEmailModel : CellModel;

[UiContract]
public sealed record GridCellUrlModel : CellModel;

[UiContract]
public sealed record GridCellReferenceModel : CellModel;

// Concrete Header Models
[UiContract]
public sealed record GridHeaderDateModel : HeaderModel;

[UiContract]
public sealed record GridHeaderCheckboxModel : HeaderModel;

[UiContract]
public sealed record GridHeaderLabelModel : HeaderModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiIcon))]
  public string? Icon { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? Filter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsHighlighted { get; set; }
}

[UiContract]
public sealed record GridHeaderToggleModel : HeaderModel
{
  public List<string> Toggles { get; set; } = [];
}

[UiContract]
public sealed record GridHeaderReferenceModel : HeaderModel;

// Grid Nav Models
[UiContract]
public sealed record GridNavSearchModel : NavElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }
}

[UiContract]
public sealed record GridNavAddModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Service { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayModelDto? Overlay { get; set; }
}

[UiContract]
public sealed record GridNavActionModel : NavButtonModel
{
  public string Service { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;
  public bool NeedsSelection { get; set; } = true;
  public bool IsDownload { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayConfirmModel? Confirm { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FormAskModel? Ask { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? UnavailableReasons { get; set; }
}

[UiContract]
public sealed record GridNavStatusMoveDto
{
  public List<long> From { get; set; } = [];
  public GridNavActionModel Action { get; set; } = new();
}

[UiContract]
public sealed record GridNavStatusModel : NavButtonModel
{
  public string Path { get; set; } = string.Empty;
  public string EmptyTitle { get; set; } = string.Empty;
  public List<GridNavStatusMoveDto> Moves { get; set; } = [];
}

[UiContract]
public sealed record GridNavNamedFilterModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsRemembered { get; set; }
}

[UiContract]
public sealed record GridNavNamedFilterGroupModel : NavButtonModel
{
  public List<GridNavNamedFilterModel> Items { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsRemembered { get; set; }
}

[UiContract]
public sealed record GridNavPickerModel : NavElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  public string ReferenceService { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;
  public int MinLength { get; set; } = 2;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool ReadsParent { get; set; }
}

[UiContract]
public sealed record GridFiltersOverlayModel : PanelModel;

[UiContract]
public sealed class GridCountDto
{
  public int Total { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, int>? NamedFilterBadges { get; set; }
}

[UiContract]
public sealed class GridDeleteDto
{
  public int Deleted { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(MutationOutcome))]
  public string? Outcome { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<MessageDto>? Warnings { get; set; }
}

[UiContract]
public sealed class GridActionDto
{
  public int Count { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? Id { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(MutationOutcome))]
  public string? Outcome { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<MessageDto>? Warnings { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? OpenScreen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public object? OpenId { get; set; }
}

[UiContract]
[ClientOptional]
public class GridPickRequest : GridRequest
{
  public long PickedId { get; set; }
}

[UiContract]
[ClientOptional]
public class GridCellRequest : GridRequest
{
  public string Path { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Ver { get; set; }

  public JsonElement Value { get; set; }
}

[UiContract]
public class GridCellDto
{
  public object? Row { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(MutationOutcome))]
  public string? Outcome { get; set; } = MutationOutcome.Committed;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<MessageDto>? Warnings { get; set; }
}


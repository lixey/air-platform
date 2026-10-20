using System.Text.Json;
using System.Text.Json.Serialization;
using Air.App.Components;
using Air.App.Grid;
using Air.App.Nav;
using Air.App.Overlay;
using Air.Data.Contracts;

namespace Air.App.Form;

[UiContract]
public static class FormTab
{
  public const string Group = "formTab";
  public const string Record = "record";
}

[UiContract]
public static class FormFieldType
{
  public const string Password = "password";
  public const string Email = "email";
  public const string Url = "url";
}

[UiContract]
public static class FormLengthUnit
{
  public const string Utf16 = "utf16";
  public const string Bytes = "bytes";
  public const string Items = "items";
}

[UiContract]
public static class FormValueKind
{
  public const string Scalar = "scalar";
  public const string List = "list";
  public const string Reference = "reference";
}

[UiContract]
public sealed class CheckboxOptionDto
{
  public long Id { get; set; }
  public string Label { get; set; } = string.Empty;
}

[UiContract]
public sealed class CheckboxTreeNodeDto
{
  public string Key { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Note { get; set; }

  public bool IsDisabled { get; set; }

  public List<CheckboxTreeNodeDto> Children { get; set; } = [];
}

[UiContract]
public sealed class FormConstraintsDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? AllowsNull { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? RejectsBlank { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(FormLengthUnit))]
  public string? LengthUnit { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinLength { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxLength { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Precision { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Scale { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [WireNumber]
  public JsonElement? Min { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [WireNumber]
  public JsonElement? Max { get; set; }
}

[UiContract]
[ClientOptional]
public class FormRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? Id { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ParentService { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? ParentId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Ver { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public JsonElement? Value { get; set; }
}

[UiContract]
public sealed class FormDataDto
{
  public long? Id { get; set; }
  public int? Ver { get; set; }
  public Dictionary<string, object?> Value { get; set; } = new(StringComparer.Ordinal);

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(MutationOutcome))]
  public string? Outcome { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<MessageDto>? Warnings { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, int>? Unavailable { get; set; }
}

[UiContract]
public sealed class FormTabDto
{
  public string Key { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;
}

[UiContract]
public sealed class SelectPatchDto
{
  public string TargetField { get; set; } = string.Empty;
  public string SourceColumn { get; set; } = string.Empty;
}

[UiContract]
public sealed record ReferenceDraftRuleDto
{
  public string SourceField { get; set; } = string.Empty;
  public string DraftField { get; set; } = string.Empty;
}

[UiContract]
public sealed class FormElementDto
{
  public string Path { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ReferencePath { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  [OneOf(typeof(UiFieldType), typeof(FormFieldType))]
  public string Type { get; set; } = UiFieldType.Text;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiTemporalType))]
  public string? TemporalType { get; set; }

  public FormElementModel Model { get; set; } = new FormInputModel();

  public int Span { get; set; } = 6;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Tab { get; set; }

  public bool IsRequired { get; set; }
  public FormConstraintsDto Constraints { get; set; } = new();
  public bool IsDisabled { get; set; }
  public bool IsHidden { get; set; }
  public bool IsServerOwned { get; set; }
  public bool IsInputOnly { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsFocused { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsSecret { get; set; }

  [OneOf(typeof(FormValueKind))]
  public string ValueKind { get; set; } = FormValueKind.Scalar;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool HasSeparatorAfter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? SeparatorLabel { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Placeholder { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Help { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<SelectPatchDto>? SelectPatches { get; set; }

  [JsonIgnore]
  internal string? UniqueMessage { get; set; }

  [JsonIgnore]
  internal List<string>? UniqueWithin { get; set; }

  [JsonIgnore]
  internal bool? ExplicitRequired { get; set; }

  [JsonIgnore]
  internal Type? ReferenceServiceType { get; set; }

  [JsonIgnore]
  internal bool AllowsCreate { get; set; }

  [JsonIgnore]
  internal Type? CreateFormServiceType { get; set; }

  [JsonIgnore]
  internal bool IsRightsTree { get; set; }
}


[UiContract]
public sealed class FormActionDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(MutationOutcome))]
  public string? Outcome { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<MessageDto>? Warnings { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? OpenScreen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? OpenId { get; set; }
}

[UiContract]
public sealed record FormModel : PanelModel
{
  public string Service { get; set; } = string.Empty;
  public List<FormElementDto> Elements { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<FormTabDto>? Tabs { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<Form.Contracts.FormFilesModel>? Files { get; set; }

  public bool IsReadOnly { get; set; }
  public OverlayDirtyModel DirtyDialog { get; set; } = new();
  public MutationTextsDto MutationTexts { get; set; } = new();
}

// Concrete Form Element Models
[UiContract]
public sealed record FormInputModel : FormElementModel;

[UiContract]
public sealed record FormTextareaModel : FormElementModel;

[UiContract]
public sealed record FormNumberModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public double? Step { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool HasStepper { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? IsGrouped { get; set; }
}

[UiContract]
public sealed record FormDateModel : FormElementModel;

[UiContract]
public sealed record FormDatetimeModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinuteStep { get; set; }
}

[UiContract]
public sealed record FormTimeModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinuteStep { get; set; }
}

[UiContract]
public sealed record FormCheckboxModel : FormElementModel;

[UiContract]
public sealed record FormSelectModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ReferenceService { get; set; }

  public List<string> ReferenceLabelPaths { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<OptionDto>? Options { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? ReferenceFilter { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReferenceDraftRuleDto>? ReferenceDraftRules { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsMultiple { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayModelDto? CreateOverlay { get; set; }
}

[UiContract]
public sealed record FormInfoModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? IsGrouped { get; set; }
}

[UiContract]
public sealed record FormPhoneModel : FormElementModel
{
  public List<string> Masks { get; set; } = [];
}

[UiContract]
public sealed record FormPercentModel : FormElementModel;

[UiContract]
public sealed record FormMoneyModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; set; }
}

[UiContract]
public sealed record FormTimeZoneModel : FormElementModel
{
  public List<string> Zones { get; set; } = [];
  public string Inherit { get; set; } = string.Empty;
}

[UiContract]
public sealed record FormCheckboxSetModel : FormElementModel
{
  public List<CheckboxOptionDto> Options { get; set; } = [];
}

[UiContract]
public sealed record FormCheckboxTreeModel : FormElementModel
{
  public List<CheckboxTreeNodeDto> Nodes { get; set; } = [];
}

[UiContract]
public sealed record FormAskModel : PanelModel
{
  public List<FormElementDto> Elements { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Message { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool HasDefaults { get; set; }
}

[UiContract]
public sealed record FormChildScreenModel : PanelModel
{
  public string Service { get; set; } = string.Empty;
  public string ParentService { get; set; } = string.Empty;
  public long ParentId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public NavModel? Tabs { get; set; }
}

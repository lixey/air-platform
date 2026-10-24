namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class FormElementDto
{
  public string Path { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ReferencePath { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [OneOf(typeof(UiFieldType), typeof(FormFieldType))]
  public string Type { get; set; } = UiFieldType.Text;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiTemporalType))]
  public string? TemporalType { get; set; }

  public FormElementModel Model { get; set; } = new FormInputModel();

  public int Span { get; set; } = 3;

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

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool SavesOnChange { get; set; }

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

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? VisibleWhen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? DisabledWhen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FilterNode? RequiredWhen { get; set; }

  [JsonIgnore]
  internal Func<object, bool>? RequiredWhenPredicate { get; set; }

  [JsonIgnore]
  internal Func<DbContext, long, CancellationToken, Task<List<long>>>? JoinLoader { get; set; }

  [JsonIgnore]
  internal Func<DbContext, long, IReadOnlyList<long>, CancellationToken, Task<bool>>? JoinSyncer { get; set; }

  [JsonIgnore]
  internal string? UniqueMessage { get; set; }

  [JsonIgnore]
  internal List<string>? UniqueWithin { get; set; }

  [JsonIgnore]
  internal bool? ExplicitRequired { get; set; }

  [JsonIgnore]
  public Type? ReferenceServiceType { get; set; }

  [JsonIgnore]
  public bool AllowsCreate { get; set; }

  [JsonIgnore]
  public Type? CreateFormServiceType { get; set; }

  [JsonIgnore]
  internal bool IsRightsTree { get; set; }
}

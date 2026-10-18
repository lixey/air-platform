namespace Air.App.Components;

public static class UiFieldType
{
  public const string Text = "text";
  public const string Number = "number";
  public const string Date = "date";
  public const string Datetime = "datetime";
  public const string Time = "time";
  public const string Checkbox = "checkbox";
}

public static class UiTemporalType
{
  public const string DateOnly = "dateOnly";
  public const string DateTime = "dateTime";
  public const string DateTimeOffset = "dateTimeOffset";
}

public static class ButtonVariant
{
  public const string Soft = "soft";
  public const string Solid = "solid";
  public const string Ghost = "ghost";
}

public static class ButtonSize
{
  public const string Small = "small";
  public const string Medium = "medium";
}

public static class UiColor
{
  public const string Primary = "primary";
  public const string Brand = "brand";
  public const string Slate = "slate";
  public const string Gray = "gray";
  public const string Zinc = "zinc";
  public const string Neutral = "neutral";
  public const string Stone = "stone";
  public const string Red = "red";
  public const string Orange = "orange";
  public const string Amber = "amber";
  public const string Yellow = "yellow";
  public const string Lime = "lime";
  public const string Green = "green";
  public const string Emerald = "emerald";
  public const string Teal = "teal";
  public const string Cyan = "cyan";
  public const string Sky = "sky";
  public const string Blue = "blue";
  public const string Indigo = "indigo";
  public const string Violet = "violet";
  public const string Purple = "purple";
  public const string Fuchsia = "fuchsia";
  public const string Pink = "pink";
  public const string Rose = "rose";
}

[UiContract]
public static class UiIcon
{
  public const string Alert = "alert";
  public const string Apply = "apply";
  public const string ArrowBottom = "arrow-bottom";
  public const string ArrowLeft = "arrow-left";
  public const string ArrowRight = "arrow-right";
  public const string ArrowTop = "arrow-top";
  public const string Ban = "ban";
  public const string Checkmark = "checkmark";
  public const string Close = "close";
  public const string Dots = "dots";
  public const string Download = "download";
  public const string Edit = "edit";
  public const string Excel = "excel";
  public const string Filter = "filter";
  public const string Import = "import";
  public const string Menu = "menu";
  public const string Photo = "photo";
  public const string Plus = "plus";
  public const string Reload = "reload";
  public const string Save = "save";
  public const string Search = "search";
  public const string Trash = "trash";
}


[UiContract]
public sealed class OptionDto
{
  public long Id { get; init; }
  public string Name { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }
}

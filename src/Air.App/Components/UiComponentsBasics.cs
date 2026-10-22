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
  public const string Bot = "bot";
  public const string Box = "box";
  public const string Calendar = "calendar";
  public const string Cart = "cart";
  public const string Chart = "chart";
  public const string Checkmark = "checkmark";
  public const string ChevronBottom = "chevron-bottom";
  public const string ChevronLeft = "chevron-left";
  public const string ChevronRight = "chevron-right";
  public const string ChevronTop = "chevron-top";
  public const string ChevronsDown = "chevrons-down";
  public const string ChevronsUp = "chevrons-up";
  public const string ChevronsUpDown = "chevrons-up-down";
  public const string Circle = "circle";
  public const string CircleFilled = "circle-filled";
  public const string Clock = "clock";
  public const string Close = "close";
  public const string Columns = "columns";
  public const string Compose = "compose";
  public const string Desktop = "desktop";
  public const string Dots = "dots";
  public const string Download = "download";
  public const string Edit = "edit";
  public const string EllipsisHorizontal = "ellipsis-horizontal";
  public const string EllipsisVertical = "ellipsis-vertical";
  public const string EllipsisVerticalHorizontal = "ellipsis-vertical-horizontal";
  public const string Excel = "excel";
  public const string Export = "export";
  public const string External = "external";
  public const string Eye = "eye";
  public const string Filter = "filter";
  public const string Folder = "folder";
  public const string Full = "full";
  public const string Fullscreen = "fullscreen";
  public const string FullscreenExit = "fullscreen-exit";
  public const string Gear = "gear";
  public const string Gift = "gift";
  public const string Globe = "globe";
  public const string GripVertical = "grip-vertical";
  public const string Heart = "heart";
  public const string Home = "home";
  public const string Import = "import";
  public const string Info = "info";
  public const string Internet = "internet";
  public const string Kanban = "kanban";
  public const string ListCheck = "list-check";
  public const string Location = "location";
  public const string Logout = "logout";
  public const string Mail = "mail";
  public const string Map = "map";
  public const string MapPin = "mapPin";
  public const string Maximize = "maximize";
  public const string Measure = "measure";
  public const string Menu = "menu";
  public const string Minimize = "minimize";
  public const string Minus = "minus";
  public const string Moon = "moon";
  public const string Move = "move";
  public const string Msg = "msg";
  public const string Multiple = "multiple";
  public const string Options = "options";
  public const string PanelBottomClose = "panelBottomClose";
  public const string PanelBottomOpen = "panelBottomOpen";
  public const string PanelRightClose = "panelRightClose";
  public const string PanelRightOpen = "panelRightOpen";
  public const string Phone = "phone";
  public const string Photo = "photo";
  public const string Pin = "pin";
  public const string Play = "play";
  public const string Plus = "plus";
  public const string Print = "print";
  public const string Question = "question";
  public const string Reload = "reload";
  public const string Rows = "rows";
  public const string Save = "save";
  public const string Search = "search";
  public const string Secondary = "secondary";
  public const string SecondaryBottom = "secondaryBottom";
  public const string SecondaryLeft = "secondaryLeft";
  public const string SecondaryRight = "secondaryRight";
  public const string SecondaryTop = "secondaryTop";
  public const string Settings = "settings";
  public const string Shelving = "shelving";
  public const string Sidebar = "sidebar";
  public const string SidebarLeft = "sidebar-left";
  public const string SidebarRight = "sidebar-right";
  public const string Signin = "signin";
  public const string Signout = "signout";
  public const string SortArrows = "sort-arrows";
  public const string Spinner = "spinner";
  public const string Star = "star";
  public const string Sun = "sun";
  public const string Telephone = "telephone";
  public const string Trash = "trash";
  public const string User = "user";
  public const string Users = "users";
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

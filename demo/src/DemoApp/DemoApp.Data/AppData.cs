namespace DemoApp.Data;

public static class AppData
{
  public const string ModuleHandle = "App";
  public const string CollationName = "cyrillic_ci";
  public const int InitializationId = 1;
  public static readonly DateTimeOffset SeedReferenceDate = new(
    2026,
    1,
    1,
    0,
    0,
    0,
    TimeSpan.Zero
  );
}

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

  public static class Role
  {
    public const string Administrator = "Administrator";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";
  }

  public static class Tenant
  {
    public const long EcomUaId = 1;
    public const long EcomEnId = 2;
    public const string EcomUa = "EcomUa";
    public const string EcomEn = "EcomEn";
  }

  public static class User
  {
    public const string AdministratorLogin = "admin";
  }

  public static class Language
  {
    public const long EnglishId = 1;
    public const long UkrainianId = 2;
    public const string English = "En";
    public const string Ukrainian = "Ua";
    public const string EnglishCode = "en-US";
    public const string UkrainianCode = "uk-UA";
  }
}

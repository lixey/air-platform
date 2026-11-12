namespace DemoApp.Migrations.Seeds;

public static class AppCodeRows
{
  private static readonly string[] Hues =
  [
    UiColor.Slate, UiColor.Gray, UiColor.Zinc, UiColor.Neutral, UiColor.Stone, UiColor.Red, UiColor.Orange, UiColor.Amber,
    UiColor.Yellow, UiColor.Lime, UiColor.Green, UiColor.Emerald, UiColor.Teal, UiColor.Cyan, UiColor.Sky, UiColor.Blue,
    UiColor.Indigo, UiColor.Violet, UiColor.Purple, UiColor.Fuchsia, UiColor.Pink, UiColor.Rose
  ];

  public static AppSeedTenant[] Tenants() =>
  [
    new(
      new AppTenant { Id = AppTenantData.EcomUaId, Handle = AppTenantData.EcomUa, Name = AppTenantData.EcomUa, TimeZone = "Europe/Kyiv", IsActive = true },
      AppLanguageData.UkrainianId,
      "uk"
    ),
    new(
      new AppTenant { Id = AppTenantData.EcomEnId, Handle = AppTenantData.EcomEn, Name = AppTenantData.EcomEn, TimeZone = "Europe/Kyiv", IsActive = true },
      AppLanguageData.EnglishId,
      "en"
    )
  ];

  public static AppLanguage[] Languages() =>
  [
    new() { Id = AppLanguageData.EnglishId, Handle = AppLanguageData.English, Code = AppLanguageData.EnglishCode, Name = "English", IsActive = true },
    new() { Id = AppLanguageData.UkrainianId, Handle = AppLanguageData.Ukrainian, Code = AppLanguageData.UkrainianCode, Name = "\u0423\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0430", IsActive = true }
  ];

  public static AppColor[] Colors() =>
    Hues.Select((hue, i) =>
      {
        var pascal = char.ToUpperInvariant(hue[0]) + hue[1..];
        return new AppColor { Id = i + 1, Handle = pascal, Name = pascal, IsActive = true };
      })
      .ToArray();

  public static async Task SyncAsync(AppDbContext db, IReadOnlyList<AppSeedTenant> tenants, CancellationToken cancellationToken = default)
  {
    await CodeRows.SyncAsync(db, tenants.Select(t => t.Row).ToArray(), cancellationToken);
    await CodeRows.SyncAsync(db, Languages(), cancellationToken);
    await CodeRows.SyncAsync(db, Colors(), cancellationToken);
  }
}

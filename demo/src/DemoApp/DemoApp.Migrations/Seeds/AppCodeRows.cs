using Air.App.Components;
using Air.Data;
using DemoApp.Data;
using DemoApp.Data.Models;

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
      new AppTenant { Id = AppData.Tenant.EcomUaId, Handle = AppData.Tenant.EcomUa, Name = AppData.Tenant.EcomUa, TimeZone = "Europe/Kyiv", IsActive = true },
      AppData.Language.UkrainianId,
      "uk"
    ),
    new(
      new AppTenant { Id = AppData.Tenant.EcomEnId, Handle = AppData.Tenant.EcomEn, Name = AppData.Tenant.EcomEn, TimeZone = "Europe/Kyiv", IsActive = true },
      AppData.Language.EnglishId,
      "en"
    )
  ];

  public static AppColor[] Colors() =>
    Hues.Select((hue, i) => new AppColor { Id = i + 1, Handle = hue, Name = char.ToUpperInvariant(hue[0]) + hue[1..], IsActive = true })
      .ToArray();

  public static async Task SyncAsync(AppDbContext db, IReadOnlyList<AppSeedTenant> tenants, CancellationToken cancellationToken = default)
  {
    await CodeRows.SyncAsync(db, tenants.Select(t => t.Row).ToArray(), cancellationToken);
    await CodeRows.SyncAsync(db, Colors(), cancellationToken);
  }
}

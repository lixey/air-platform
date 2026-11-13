namespace Platform.Api.Localization;

public sealed record PlatformCulture(
  long Id,
  string Handle,
  string Code,
  string ShortCode,
  string DisplayName,
  CultureInfo CultureInfo
);

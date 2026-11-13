namespace Platform.Api.Localization;

public sealed class CultureCatalog : ICultureCatalog
{
  public static CultureCatalog DefaultInstance { get; } = CreateDefault();

  private readonly PlatformCulture[] _cultures;

  public CultureCatalog(IEnumerable<PlatformCulture> cultures, string? defaultToken = null)
  {
    ArgumentNullException.ThrowIfNull(cultures);
    var list = new List<PlatformCulture>();
    foreach (var culture in cultures)
    {
      CodeRows.EnsurePascalCaseHandle(culture.Handle, $"Culture '{culture.Code}'");
      if (list.Any(c =>
            c.Id == culture.Id ||
            string.Equals(c.Handle, culture.Handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Code, culture.Code, StringComparison.OrdinalIgnoreCase)))
      {
        throw new InvalidOperationException($"Duplicate culture registration '{culture.Handle}' ({culture.Code}).");
      }

      list.Add(culture);
    }

    if (list.Count == 0)
    {
      throw new InvalidOperationException("CultureCatalog requires at least one culture.");
    }

    _cultures = [.. list];
    Default = MatchToken(defaultToken) ?? _cultures[0];
  }

  public IReadOnlyList<PlatformCulture> Cultures => _cultures;

  public PlatformCulture Default { get; }

  public PlatformCulture Resolve(string? token) => MatchToken(token) ?? Default;

  public PlatformCulture ResolveById(long? id) =>
    id is > 0
      ? _cultures.FirstOrDefault(c => c.Id == id.Value) ?? Default
      : Default;

  public PlatformCulture ResolveRequest(HttpRequest request)
  {
    if (request.Query.TryGetValue("lang", out var langVal) &&
        MatchToken(langVal.ToString()) is { } fromQuery)
    {
      return fromQuery;
    }

    if (request.Cookies.TryGetValue("culture", out var cookieVal) &&
        MatchToken(cookieVal) is { } fromCookie)
    {
      return fromCookie;
    }

    if (request.Headers.TryGetValue("Accept-Language", out var acceptVal))
    {
      foreach (var segment in acceptVal.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      {
        var rawTag = segment.Split(';', 2)[0].Trim();
        if (MatchToken(rawTag) is { } fromHeader)
        {
          return fromHeader;
        }
      }
    }

    return Default;
  }

  private PlatformCulture? MatchToken(string? token)
  {
    if (string.IsNullOrWhiteSpace(token))
    {
      return null;
    }

    var trimmed = token.Trim();
    foreach (var culture in _cultures)
    {
      if (string.Equals(culture.Code, trimmed, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(culture.ShortCode, trimmed, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(culture.Handle, trimmed, StringComparison.OrdinalIgnoreCase))
      {
        return culture;
      }
    }

    var prefix = trimmed.Split(['-', '_'], 2)[0];
    foreach (var culture in _cultures)
    {
      if (string.Equals(culture.ShortCode, prefix, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(culture.Handle, prefix, StringComparison.OrdinalIgnoreCase))
      {
        return culture;
      }
    }

    return null;
  }

  public static CultureCatalog CreateDefault(string? defaultCultureCode = "uk-UA") =>
    new(
      [
        new PlatformCulture(1, "En", "en-US", "en", "English", CultureInfo.GetCultureInfo("en-US")),
        new PlatformCulture(2, "Ua", "uk-UA", "uk", "\u0423\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0430", CultureInfo.GetCultureInfo("uk-UA"))
      ],
      defaultCultureCode);
}

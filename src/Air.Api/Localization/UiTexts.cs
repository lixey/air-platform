using System.Collections;
using System.Resources;

namespace Platform.Api.Localization;

public static class UiTexts
{
  public static readonly IReadOnlyDictionary<string, string> English = BuildEnglish();

  private static FrozenDictionary<string, string> BuildEnglish()
  {
    var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var mgr = new ResourceManager(typeof(PlatformResource));
    var set = mgr.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
    if (set != null)
    {
      foreach (DictionaryEntry entry in set)
      {
        if (entry.Key is string key && entry.Value is string val)
        {
          entries[key] = val;
        }
      }
    }

    return entries.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
  }
}

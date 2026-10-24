namespace Platform.Api.Files.Internals;

internal sealed record FileLink(
  [property: JsonPropertyName("k")] string Key,
  [property: JsonPropertyName("s"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Store,
  [property: JsonPropertyName("t")] string ContentType,
  [property: JsonPropertyName("n")] string Name,
  [property: JsonPropertyName("e")] long Expires,
  [property: JsonPropertyName("h")] bool IsThumbnail = false
);

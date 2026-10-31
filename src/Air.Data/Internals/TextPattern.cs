namespace Platform.Data.Internals;

internal readonly record struct TextPattern(TextPatternKind Kind, string Term)
{
  private static readonly char[] Masks = ['*'];

  public bool IsEmpty => Term.Length == 0;

  public bool IsPlain => Kind is TextPatternKind.Contains or TextPatternKind.Equals;

  public static TextPattern Parse(string text)
  {
    var raw = text.Trim();
    if (raw.StartsWith('='))
    {
      return new TextPattern(TextPatternKind.Equals, raw[1..].Trim());
    }

    var leading = raw.Length > 0 && Masks.Contains(raw[0]);
    var trailing = raw.Length > 1 && Masks.Contains(raw[^1]);
    var kind = (leading, trailing) switch
    {
      (true, false) => TextPatternKind.EndsWith,
      (false, true) => TextPatternKind.StartsWith,
      _ => TextPatternKind.Contains
    };

    return new TextPattern(kind, raw.Trim(Masks).Trim());
  }
}

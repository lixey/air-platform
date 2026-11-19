namespace Platform.Agent.Internals;

internal static class AgentTagBlockExtractor
{
  public static bool ContainsTagOutsideInlineCode(string? text, string startTag)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return false;
    }

    var inlineSpans = FindInlineCodeSpans(text);
    var idx = 0;
    while (idx < text.Length)
    {
      var found = text.IndexOf(startTag, idx, StringComparison.OrdinalIgnoreCase);
      if (found < 0)
      {
        return false;
      }

      if (!IsInsideSpan(found, inlineSpans))
      {
        return true;
      }

      idx = found + 1;
    }

    return false;
  }

  public static List<string> ExtractInnerBlocks(string text, string startTag, string endTag)
  {
    var results = new List<string>();
    if (string.IsNullOrWhiteSpace(text))
    {
      return results;
    }

    var inlineSpans = FindInlineCodeSpans(text);
    var idx = 0;
    while (idx < text.Length)
    {
      var start = FindNextOutsideInlineCode(text, startTag, idx, inlineSpans);
      if (start < 0)
      {
        break;
      }

      var contentStart = start + startTag.Length;
      var end = FindNextOutsideInlineCode(text, endTag, contentStart, inlineSpans);
      if (end < 0)
      {
        break;
      }

      var inner = text[contentStart..end].Trim();
      idx = end + endTag.Length;
      if (!string.IsNullOrWhiteSpace(inner))
      {
        results.Add(inner);
      }
    }

    return results;
  }

  private static int FindNextOutsideInlineCode(
    string text,
    string token,
    int startIndex,
    List<(int Start, int End)> inlineSpans)
  {
    var idx = startIndex;
    while (idx < text.Length)
    {
      var found = text.IndexOf(token, idx, StringComparison.OrdinalIgnoreCase);
      if (found < 0)
      {
        return -1;
      }

      if (!IsInsideSpan(found, inlineSpans))
      {
        return found;
      }

      idx = found + 1;
    }

    return -1;
  }

  private static List<(int Start, int End)> FindInlineCodeSpans(string text)
  {
    var spans = new List<(int Start, int End)>();
    var i = 0;
    while (i < text.Length)
    {
      if (i == 0 || text[i - 1] == '\n')
      {
        var fenceStart = i;
        while (fenceStart < text.Length && fenceStart - i < 3 && text[fenceStart] == ' ')
        {
          fenceStart++;
        }

        if (fenceStart + 3 <= text.Length && text.AsSpan(fenceStart, 3).SequenceEqual("```"))
        {
          var lineEnd = text.IndexOf('\n', fenceStart + 3);
          if (lineEnd < 0)
          {
            break;
          }

          var search = lineEnd + 1;
          var closed = false;
          while (search < text.Length)
          {
            var nextNl = text.IndexOf('\n', search);
            var lineLen = (nextNl < 0 ? text.Length : nextNl) - search;
            var line = text.AsSpan(search, lineLen).TrimEnd('\r');
            if (IsClosingFenceLine(line))
            {
              i = nextNl < 0 ? text.Length : nextNl + 1;
              closed = true;
              break;
            }

            if (nextNl < 0)
            {
              break;
            }

            search = nextNl + 1;
          }

          if (!closed)
          {
            break;
          }

          continue;
        }
      }

      if (text[i] == '`')
      {
        var ticks = 0;
        while (i + ticks < text.Length && text[i + ticks] == '`')
        {
          ticks++;
        }

        var openEnd = i + ticks;
        if (ticks < 3)
        {
          var lineEnd = text.IndexOf('\n', openEnd);
          var limit = lineEnd < 0 ? text.Length : lineEnd;
          var j = openEnd;
          var foundClose = -1;
          while (j < limit)
          {
            if (text[j] == '`')
            {
              var closeTicks = 0;
              while (j + closeTicks < limit && text[j + closeTicks] == '`')
              {
                closeTicks++;
              }

              if (closeTicks == ticks)
              {
                foundClose = j + closeTicks;
                break;
              }

              j += closeTicks;
            }
            else
            {
              j++;
            }
          }

          if (foundClose > openEnd)
          {
            spans.Add((i, foundClose));
            i = foundClose;
            continue;
          }
        }

        i = openEnd;
        continue;
      }

      i++;
    }

    return spans;
  }

  private static bool IsClosingFenceLine(ReadOnlySpan<char> line)
  {
    var spaces = 0;
    while (spaces < line.Length && spaces < 3 && line[spaces] == ' ')
    {
      spaces++;
    }

    if (spaces + 3 > line.Length || !line.Slice(spaces, 3).SequenceEqual("```"))
    {
      return false;
    }

    for (var k = spaces + 3; k < line.Length; k++)
    {
      if (line[k] is not (' ' or '\t'))
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsInsideSpan(int offset, List<(int Start, int End)> spans)
  {
    foreach (var (start, end) in spans)
    {
      if (offset >= start && offset < end)
      {
        return true;
      }
    }

    return false;
  }
}

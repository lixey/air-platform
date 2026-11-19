import type { AgentBlockPlugin, AgentContentBlock } from './agent-block-models';
import { splitMarkdownAndGfmTables } from './agent-markdown';

interface TextSpan {
  readonly start: number;
  readonly end: number;
}

interface TagBlockMatch {
  readonly start: number;
  readonly end: number;
  readonly inner: string;
  readonly closed: boolean;
}

function findInlineCodeSpans(text: string): readonly TextSpan[] {
  const spans: TextSpan[] = [];
  let i = 0;
  while (i < text.length) {
    const atLineStart = i === 0 || text[i - 1] === '\n';
    if (atLineStart) {
      let fenceStart = i;
      while (fenceStart < text.length && fenceStart - i < 3 && text[fenceStart] === ' ') {
        fenceStart++;
      }
      if (text.startsWith('```', fenceStart)) {
        const lineEnd = text.indexOf('\n', fenceStart + 3);
        if (lineEnd < 0) {
          break;
        }
        let search = lineEnd + 1;
        let closed = false;
        while (search < text.length) {
          const nextNl = text.indexOf('\n', search);
          const line = nextNl < 0 ? text.slice(search) : text.slice(search, nextNl);
          if (/^[ ]{0,3}```[ \t]*\r?$/.test(line)) {
            i = nextNl < 0 ? text.length : nextNl + 1;
            closed = true;
            break;
          }
          if (nextNl < 0) {
            break;
          }
          search = nextNl + 1;
        }
        if (!closed) {
          break;
        }
        continue;
      }
    }

    if (text[i] === '`') {
      let ticks = 0;
      while (i + ticks < text.length && text[i + ticks] === '`') {
        ticks++;
      }
      const openEnd = i + ticks;
      if (ticks < 3) {
        // Inline code mentions stay on a single line so stray backticks cannot bridge across blocks.
        const lineEnd = text.indexOf('\n', openEnd);
        const limit = lineEnd < 0 ? text.length : lineEnd;
        let j = openEnd;
        let foundClose = -1;
        while (j < limit) {
          if (text[j] === '`') {
            let closeTicks = 0;
            while (j + closeTicks < limit && text[j + closeTicks] === '`') {
              closeTicks++;
            }
            if (closeTicks === ticks) {
              foundClose = j + closeTicks;
              break;
            }
            j += closeTicks;
          } else {
            j++;
          }
        }
        if (foundClose > openEnd) {
          spans.push({ start: i, end: foundClose });
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

function isInsideSpan(offset: number, spans: readonly TextSpan[]): boolean {
  return spans.some(s => offset >= s.start && offset < s.end);
}

function expandOptionalWrappingFence(text: string, start: number, end: number, fenceLangs: string): TextSpan {
  const before = text.slice(0, start);
  const openFence = new RegExp(`\`\`\`(?:${fenceLangs})?[ \\t]*\\r?\\n?[ \\t]*$`, 'i').exec(before);
  if (!openFence) {
    return { start, end };
  }
  const expandedStart = openFence.index;
  const after = text.slice(end);
  const closeFence = /^[ \t]*\r?\n?[ \t]*```/.exec(after);
  const expandedEnd = closeFence ? end + closeFence[0].length : end;
  return { start: expandedStart, end: expandedEnd };
}

function findTagBlocksOutsideInlineCode(
  text: string,
  tag: string,
  inlineSpans: readonly TextSpan[],
  fenceLangs: string,
): readonly TagBlockMatch[] {
  const openRegex = new RegExp(`<${tag}\\b[^>]*>`, 'gi');
  const closeRegex = new RegExp(`<\\/${tag}>`, 'gi');

  const opens: Array<{ start: number; end: number }> = [];
  const closes: Array<{ start: number; end: number }> = [];
  let m: RegExpExecArray | null;

  while ((m = openRegex.exec(text)) !== null) {
    if (!isInsideSpan(m.index, inlineSpans)) {
      opens.push({ start: m.index, end: m.index + m[0].length });
    }
  }
  while ((m = closeRegex.exec(text)) !== null) {
    if (!isInsideSpan(m.index, inlineSpans)) {
      closes.push({ start: m.index, end: m.index + m[0].length });
    }
  }

  const matches: TagBlockMatch[] = [];
  let cursor = 0;
  for (const open of opens) {
    if (open.start < cursor) {
      continue;
    }
    const close = closes.find(c => c.start >= open.end);
    if (close) {
      const span = expandOptionalWrappingFence(text, open.start, close.end, fenceLangs);
      matches.push({
        start: span.start,
        end: span.end,
        inner: text.slice(open.end, close.start),
        closed: true,
      });
      cursor = span.end;
    } else {
      const span = expandOptionalWrappingFence(text, open.start, text.length, fenceLangs);
      matches.push({
        start: span.start,
        end: text.length,
        inner: '',
        closed: false,
      });
      break;
    }
  }

  return matches;
}

function removeSpans(text: string, spans: readonly TextSpan[]): string {
  if (spans.length === 0) {
    return text;
  }
  let out = '';
  let cursor = 0;
  for (const span of spans) {
    if (span.start > cursor) {
      out += text.slice(cursor, span.start);
    }
    cursor = Math.max(cursor, span.end);
  }
  if (cursor < text.length) {
    out += text.slice(cursor);
  }
  return out;
}

/**
 * Removes inline `<ui_proposal>` and `<agent_memory>` XML blocks (including incomplete trailing tags while streaming)
 * from raw assistant text before displaying or copying prose.
 */
export function stripAgentControlBlocks(raw: string): string {
  let text = raw;
  for (const tag of ['ui_proposal', 'agent_memory']) {
    const inlineSpans = findInlineCodeSpans(text);
    const blocks = findTagBlocksOutsideInlineCode(text, tag, inlineSpans, 'json|xml');
    text = removeSpans(text, blocks);
  }
  return text.replace(/\n{3,}/g, '\n\n').trim();
}

function stripProposalAndIncompleteBlocks(raw: string, plugins: readonly AgentBlockPlugin[]): string {
  let text = stripAgentControlBlocks(raw);

  for (const plugin of plugins) {
    const inlineSpans = findInlineCodeSpans(text);
    const incomplete = findTagBlocksOutsideInlineCode(text, plugin.tag, inlineSpans, 'json|xml|html').find(b => !b.closed);
    if (incomplete) {
      text = text.slice(0, incomplete.start);
    }

    for (const fence of plugin.fences ?? []) {
      const escapedFence = fence.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const openFenceRegex = new RegExp(`\`\`\`${escapedFence}\\b(?![\\s\\S]*?\`\`\`)[\\s\\S]*$`, 'i');
      text = text.replace(openFenceRegex, '');
    }
  }

  return text.trim();
}

interface MatchedPluginSpan {
  readonly start: number;
  readonly end: number;
  readonly plugin: AgentBlockPlugin;
  readonly inner: string;
}

/**
 * Parses a raw assistant response into an ordered list of modular content blocks (`markdown`, `chart`, `table`, `kpi`, `html`, or custom plugins).
 */
export function parseAgentBlocks(rawText: string, plugins: readonly AgentBlockPlugin[], messageId = 'm'): readonly AgentContentBlock[] {
  const cleaned = stripProposalAndIncompleteBlocks(rawText, plugins);
  if (!cleaned) {
    return [];
  }

  const inlineSpans = findInlineCodeSpans(cleaned);
  const spans: MatchedPluginSpan[] = [];

  for (const plugin of plugins) {
    for (const block of findTagBlocksOutsideInlineCode(cleaned, plugin.tag, inlineSpans, 'json|xml|html')) {
      if (block.closed) {
        spans.push({
          start: block.start,
          end: block.end,
          plugin,
          inner: block.inner,
        });
      }
    }

    for (const fence of plugin.fences ?? []) {
      const escapedFence = fence.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const fenceRegex = new RegExp(`\\\`\\\`\\\`${escapedFence}[ \\t]*\\r?\\n([\\s\\S]*?)\\\`\\\`\\\``, 'gi');
      let m: RegExpExecArray | null;
      while ((m = fenceRegex.exec(cleaned)) !== null) {
        const start = m.index;
        const end = m.index + m[0].length;
        const overlapsExisting = spans.some(s => start < s.end && end > s.start);
        if (!overlapsExisting) {
          spans.push({
            start,
            end,
            plugin,
            inner: m[1] ?? '',
          });
        }
      }
    }
  }

  spans.sort((a, b) => a.start - b.start);

  const blocks: AgentContentBlock[] = [];
  let cursor = 0;
  let seq = 0;
  let pendingMarkdown = '';

  const flushPendingMarkdown = (): void => {
    const trimmed = pendingMarkdown.replace(/\n{3,}/g, '\n\n').trim();
    pendingMarkdown = '';
    if (!trimmed) {
      return;
    }
    const parts = splitMarkdownAndGfmTables(trimmed);
    for (const part of parts) {
      const id = `${messageId}-b${++seq}`;
      if (part.kind === 'table') {
        blocks.push({ id, kind: 'table', table: part.table });
      } else {
        blocks.push({ id, kind: 'markdown', content: part.content });
      }
    }
  };

  for (const span of spans) {
    if (span.start < cursor) {
      continue;
    }
    if (span.start > cursor) {
      pendingMarkdown += cleaned.slice(cursor, span.start);
    }

    const parsedBlocks = span.plugin.parse(span.inner, `${messageId}-b${++seq}`);
    if (parsedBlocks.length > 0) {
      flushPendingMarkdown();
      for (const block of parsedBlocks) {
        if (block.kind === 'custom') {
          blocks.push({
            ...block,
            customKind: block.customKind ?? span.plugin.kind,
            tagName: block.tagName ?? span.plugin.tag,
            component: block.component ?? span.plugin.component,
          });
        } else {
          blocks.push(block);
        }
      }
    }
    cursor = span.end;
  }

  if (cursor < cleaned.length) {
    pendingMarkdown += cleaned.slice(cursor);
  }
  flushPendingMarkdown();

  return blocks;
}

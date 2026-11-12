import type { AgentBlockPlugin, AgentContentBlock } from './agent-block-models';
import { splitMarkdownAndGfmTables } from './agent-markdown';

/**
 * Removes inline `<ui_proposal>` and `<agent_memory>` XML blocks (including incomplete trailing tags while streaming)
 * from raw assistant text before displaying or copying prose.
 */
export function stripAgentControlBlocks(raw: string): string {
  return raw
    .replace(/```(?:json|xml)?\s*<ui_proposal\b[^>]*>[\s\S]*?(?:<\/ui_proposal>\s*```|<\/ui_proposal>|$)/gi, '')
    .replace(/<ui_proposal\b[^>]*>[\s\S]*?(?:<\/ui_proposal>|$)/gi, '')
    .replace(/```(?:json|xml)?\s*<agent_memory\b[^>]*>[\s\S]*?(?:<\/agent_memory>\s*```|<\/agent_memory>|$)/gi, '')
    .replace(/<agent_memory\b[^>]*>[\s\S]*?(?:<\/agent_memory>|$)/gi, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

function stripProposalAndIncompleteBlocks(raw: string, plugins: readonly AgentBlockPlugin[]): string {
  let text = stripAgentControlBlocks(raw);

  for (const plugin of plugins) {
    const openTagRegex = new RegExp(`<${plugin.tag}\\b[^>]*>(?![\\s\\S]*<\\/${plugin.tag}>)[\\s\\S]*$`, 'i');
    text = text.replace(openTagRegex, '');

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

  const spans: MatchedPluginSpan[] = [];

  for (const plugin of plugins) {
    const tagRegex = new RegExp(
      `(?:\\\`\\\`\\\`(?:json|xml|html)?\\s*)?<${plugin.tag}\\b[^>]*>([\\s\\S]*?)<\\/${plugin.tag}>(?:\\s*\\\`\\\`\\\`)?`,
      'gi',
    );
    let m: RegExpExecArray | null;
    while ((m = tagRegex.exec(cleaned)) !== null) {
      spans.push({
        start: m.index,
        end: m.index + m[0].length,
        plugin,
        inner: m[1] ?? '',
      });
    }

    for (const fence of plugin.fences ?? []) {
      const escapedFence = fence.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const fenceRegex = new RegExp(`\\\`\\\`\\\`${escapedFence}[ \\t]*\\r?\\n([\\s\\S]*?)\\\`\\\`\\\``, 'gi');
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

  const pushMarkdownChunk = (chunk: string): void => {
    const trimmed = chunk.trim();
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
      pushMarkdownChunk(cleaned.slice(cursor, span.start));
    }

    const parsedBlocks = span.plugin.parse(span.inner, `${messageId}-b${++seq}`);
    blocks.push(...parsedBlocks);
    cursor = span.end;
  }

  if (cursor < cleaned.length) {
    pushMarkdownChunk(cleaned.slice(cursor));
  }

  return blocks;
}

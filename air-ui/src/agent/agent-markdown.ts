import type { ReportColumnDto, ReportTableDto } from '../core/generated/contracts';

const ALLOWED_TAGS = new Set([
  'a',
  'b',
  'blockquote',
  'br',
  'code',
  'del',
  'details',
  'div',
  'em',
  'h1',
  'h2',
  'h3',
  'h4',
  'h5',
  'h6',
  'hr',
  'i',
  'kbd',
  'li',
  'mark',
  'ol',
  'p',
  'pre',
  's',
  'small',
  'span',
  'strong',
  'sub',
  'summary',
  'sup',
  'table',
  'tbody',
  'td',
  'tfoot',
  'th',
  'thead',
  'tr',
  'u',
  'ul',
]);

const ALLOWED_ATTRS = new Set(['class', 'href', 'title', 'target', 'rel', 'colspan', 'rowspan', 'align', 'open']);

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/**
 * Sanitizes an HTML string against XSS while preserving safe formatting, table, and layout tags.
 */
export function sanitizeAgentHtml(rawHtml: string): string {
  const withoutDangerousBlocks = rawHtml
    .replace(/<(script|style|iframe|object|embed|applet|form|meta|link)\b[^>]*>[\s\S]*?<\/\1>/gi, '')
    .replace(/<(script|style|iframe|object|embed|applet|form|meta|link|input|button|textarea)\b[^>]*\/?>/gi, '')
    .replace(/<!--[\s\S]*?-->/g, '');

  const safeTags: string[] = [];
  const tokenized = withoutDangerousBlocks.replace(/<\/?([a-zA-Z][a-zA-Z0-9]*)\b([^>]*)>/g, (full, tagName: string, attrsRaw: string) => {
    const lowerTag = tagName.toLowerCase();
    if (!ALLOWED_TAGS.has(lowerTag)) {
      return escapeHtml(full);
    }

    if (full.startsWith('</')) {
      const idx = safeTags.length;
      safeTags.push(`</${lowerTag}>`);
      return `\u0000SAFETAG${idx}\u0000`;
    }

    const selfClosing = full.endsWith('/>') || lowerTag === 'br' || lowerTag === 'hr';
    const cleanAttrs: string[] = [];
    const attrRegex = /([a-zA-Z0-9_-]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?/g;
    let match: RegExpExecArray | null;

    while ((match = attrRegex.exec(attrsRaw)) !== null) {
      const attrName = match[1].toLowerCase();
      if (attrName.startsWith('on') || !ALLOWED_ATTRS.has(attrName)) {
        continue;
      }
      const attrVal = match[2] ?? match[3] ?? match[4] ?? '';
      if (attrName === 'href') {
        const trimmedUrl = attrVal.trim().toLowerCase();
        if (trimmedUrl.startsWith('javascript:') || trimmedUrl.startsWith('vbscript:') || trimmedUrl.startsWith('data:')) {
          continue;
        }
        cleanAttrs.push(`href="${escapeHtml(attrVal)}" target="_blank" rel="noopener noreferrer"`);
      } else if (attrName === 'open' && !attrVal) {
        cleanAttrs.push('open');
      } else if (attrVal) {
        cleanAttrs.push(`${attrName}="${escapeHtml(attrVal)}"`);
      }
    }

    const attrStr = cleanAttrs.length > 0 ? ' ' + cleanAttrs.join(' ') : '';
    const rendered = selfClosing && (lowerTag === 'br' || lowerTag === 'hr') ? `<${lowerTag}${attrStr} />` : `<${lowerTag}${attrStr}>`;
    const idx = safeTags.length;
    safeTags.push(rendered);
    return `\u0000SAFETAG${idx}\u0000`;
  });

  const escapedRemainder = tokenized.replace(/</g, '&lt;').replace(/>/g, '&gt;');
  return escapedRemainder.replace(/\u0000SAFETAG(\d+)\u0000/g, (_m, idxStr: string) => safeTags[Number(idxStr)] ?? '');
}

function renderInlineMarkdown(line: string): string {
  const codeSpans: string[] = [];
  let text = line.replace(/`([^`\n]+)`/g, (_m, code: string) => {
    const idx = codeSpans.length;
    codeSpans.push(`<code class="rounded bg-surface-3 px-1.5 py-0.5 font-mono text-[12px] text-ink">${escapeHtml(code)}</code>`);
    return `\u0000CODE${idx}\u0000`;
  });

  text = sanitizeAgentHtml(text);

  text = text.replace(
    /\[([^\]]+)\]\((https?:\/\/[^\s)"'<>]+)\)/g,
    (_m, label: string, url: string) =>
      `<a href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer" class="font-medium text-mark underline underline-offset-2 hover:opacity-80">${label}</a>`,
  );
  text = text.replace(/\*\*([^*]+)\*\*/g, '<strong class="font-semibold text-ink">$1</strong>');
  text = text.replace(/__([^_]+)__/g, '<strong class="font-semibold text-ink">$1</strong>');
  text = text.replace(/(?<!\*)\*([^*\n]+)\*(?!\*)/g, '<em class="italic">$1</em>');
  text = text.replace(/~~([^~]+)~~/g, '<del class="line-through opacity-70">$1</del>');

  return text.replace(/\u0000CODE(\d+)\u0000/g, (_m, idxStr: string) => codeSpans[Number(idxStr)] ?? '');
}

/**
 * Converts Markdown (with optional inline safe HTML) into styled HTML fragments using platform semantic classes.
 */
export function renderMarkdownToSafeHtml(markdown: string): string {
  if (!markdown.trim()) {
    return '';
  }

  const codeBlocks: string[] = [];
  const withCodePlaceholders = markdown.replace(/```([a-zA-Z0-9_-]*)\n([\s\S]*?)```/g, (_m, lang: string, code: string) => {
    const idx = codeBlocks.length;
    const langBadge = lang
      ? `<div class="border-b border-line bg-surface-3 px-3 py-1 font-mono text-[11px] text-muted">${escapeHtml(lang)}</div>`
      : '';
    codeBlocks.push(
      `<div class="my-2 overflow-hidden rounded-lg border border-line bg-surface">${langBadge}<pre class="overflow-x-auto p-3 font-mono text-xs leading-relaxed text-ink"><code>${escapeHtml(code.replace(/\n$/, ''))}</code></pre></div>`,
    );
    return `\n\u0000BLOCKCODE${idx}\u0000\n`;
  });

  const lines = withCodePlaceholders.replace(/\r\n/g, '\n').split('\n');
  const out: string[] = [];
  let inUl = false;
  let inOl = false;
  let quoteLines: string[] = [];
  let paraLines: string[] = [];

  const flushPara = (): void => {
    if (paraLines.length > 0) {
      out.push(`<p class="leading-relaxed">${paraLines.map(renderInlineMarkdown).join('<br />')}</p>`);
      paraLines = [];
    }
  };

  const flushLists = (): void => {
    if (inUl) {
      out.push('</ul>');
      inUl = false;
    }
    if (inOl) {
      out.push('</ol>');
      inOl = false;
    }
  };

  const flushQuote = (): void => {
    if (quoteLines.length === 0) {
      return;
    }
    const first = quoteLines[0].trim();
    const alertMatch = /^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*(.*)$/i.exec(first);
    if (alertMatch) {
      const kind = alertMatch[1].toUpperCase();
      const restFirst = alertMatch[2];
      const bodyLines = [restFirst, ...quoteLines.slice(1)].filter(Boolean);
      const borderCls =
        kind === 'WARNING' || kind === 'CAUTION'
          ? 'border-amber-500/60 bg-amber-500/10'
          : kind === 'TIP'
            ? 'border-emerald-500/60 bg-emerald-500/10'
            : 'border-sky-500/60 bg-sky-500/10';
      out.push(
        `<blockquote class="my-1.5 rounded-r-lg border-l-3 ${borderCls} px-3 py-2 text-xs leading-relaxed">${bodyLines.map(renderInlineMarkdown).join('<br />')}</blockquote>`,
      );
    } else {
      out.push(
        `<blockquote class="my-1.5 border-l-3 border-mark/60 bg-surface/60 pl-3 py-1 text-xs leading-relaxed text-muted">${quoteLines.map(renderInlineMarkdown).join('<br />')}</blockquote>`,
      );
    }
    quoteLines = [];
  };

  for (const rawLine of lines) {
    const trimmed = rawLine.trim();

    const codeBlockMatch = /^\u0000BLOCKCODE(\d+)\u0000$/.exec(trimmed);
    if (codeBlockMatch) {
      flushPara();
      flushLists();
      flushQuote();
      out.push(codeBlocks[Number(codeBlockMatch[1])] ?? '');
      continue;
    }

    if (!trimmed) {
      flushPara();
      flushLists();
      flushQuote();
      continue;
    }

    if (/^(?:---|\*\*\*|___)\s*$/.test(trimmed)) {
      flushPara();
      flushLists();
      flushQuote();
      out.push('<hr class="my-2 border-t border-line" />');
      continue;
    }

    const headingMatch = /^(#{1,6})\s+(.+)$/.exec(trimmed);
    if (headingMatch) {
      flushPara();
      flushLists();
      flushQuote();
      const level = headingMatch[1].length;
      const content = renderInlineMarkdown(headingMatch[2]);
      const sizeCls =
        level === 1
          ? 'text-base font-bold text-ink mt-2 mb-1'
          : level === 2
            ? 'text-sm font-bold text-ink mt-2 mb-0.5'
            : 'text-xs font-semibold uppercase tracking-wide text-muted mt-1.5 mb-0.5';
      out.push(`<h${level} class="${sizeCls}">${content}</h${level}>`);
      continue;
    }

    const quoteMatch = /^>\s?(.*)$/.exec(trimmed);
    if (quoteMatch) {
      flushPara();
      flushLists();
      quoteLines.push(quoteMatch[1]);
      continue;
    }
    flushQuote();

    const ulMatch = /^[-*•]\s+(.+)$/.exec(trimmed);
    if (ulMatch) {
      flushPara();
      if (inOl) {
        out.push('</ol>');
        inOl = false;
      }
      if (!inUl) {
        out.push('<ul class="my-1 list-disc space-y-1 pl-5 leading-relaxed">');
        inUl = true;
      }
      out.push(`<li>${renderInlineMarkdown(ulMatch[1])}</li>`);
      continue;
    }

    const olMatch = /^(\d+)[.)]\s+(.+)$/.exec(trimmed);
    if (olMatch) {
      flushPara();
      if (inUl) {
        out.push('</ul>');
        inUl = false;
      }
      if (!inOl) {
        out.push('<ol class="my-1 list-decimal space-y-1 pl-5 leading-relaxed">');
        inOl = true;
      }
      out.push(`<li>${renderInlineMarkdown(olMatch[2])}</li>`);
      continue;
    }

    flushLists();
    paraLines.push(trimmed);
  }

  flushPara();
  flushLists();
  flushQuote();

  return out.join('\n');
}

function splitMarkdownTableRow(line: string): string[] {
  const trimmed = line.trim().replace(/^\|/, '').replace(/\|$/, '');
  return trimmed.split('|').map(cell =>
    cell
      .trim()
      .replace(/^\*\*(.+)\*\*$/, '$1')
      .replace(/^`(.+)`$/, '$1')
      .trim(),
  );
}

function isSeparatorRow(line: string): boolean {
  const trimmed = line.trim();
  if (!trimmed.includes('|') || !trimmed.includes('-')) {
    return false;
  }
  const cells = splitMarkdownTableRow(trimmed);
  return cells.length >= 2 && cells.every(c => /^:?-{2,}:?$/.test(c));
}

function parseNumericCell(val: string): { num: number; isPercent: boolean } | null {
  if (!val) {
    return null;
  }
  const clean = val
    .replace(/\*\*/g, '')
    .replace(/(?:грн|uah|usd|eur|₴|\$|€)/gi, '')
    .trim();
  if (!clean || /[a-zA-Zа-яА-ЯіїєґІЇЄҐ]/.test(clean)) {
    return null;
  }
  const isPercent = clean.endsWith('%');
  const numPart = (isPercent ? clean.slice(0, -1) : clean).replace(/[\s\u00A0\u202F']/g, '').replace(',', '.');
  if (!/^[+-]?\d+(?:\.\d+)?$/.test(numPart)) {
    return null;
  }
  const num = Number(numPart);
  return Number.isFinite(num) ? { num, isPercent } : null;
}

/**
 * Splits a markdown chunk into alternating markdown segments and parsed `ReportTableDto` objects whenever
 * a GitHub-Flavored Markdown (GFM) pipe table is encountered.
 */
export function splitMarkdownAndGfmTables(
  markdown: string,
): readonly ({ kind: 'markdown'; content: string } | { kind: 'table'; table: ReportTableDto })[] {
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  const result: ({ kind: 'markdown'; content: string } | { kind: 'table'; table: ReportTableDto })[] = [];
  let mdBuffer: string[] = [];
  let i = 0;

  const flushMd = (): void => {
    const text = mdBuffer.join('\n').trim();
    if (text) {
      result.push({ kind: 'markdown', content: text });
    }
    mdBuffer = [];
  };

  while (i < lines.length) {
    const line = lines[i];
    const nextLine = i + 1 < lines.length ? lines[i + 1] : '';

    if (line.trim().includes('|') && isSeparatorRow(nextLine)) {
      const headers = splitMarkdownTableRow(line);
      const alignments = splitMarkdownTableRow(nextLine).map(sep => (sep.endsWith(':') && !sep.startsWith(':') ? 'right' : 'left'));
      const rawDataRows: string[][] = [];
      i += 2;

      while (i < lines.length && lines[i].trim().includes('|')) {
        if (!isSeparatorRow(lines[i])) {
          rawDataRows.push(splitMarkdownTableRow(lines[i]));
        }
        i++;
      }

      if (headers.length >= 2 && rawDataRows.length >= 1) {
        flushMd();

        const columns: ReportColumnDto[] = headers.map((h, colIdx) => {
          let numericCount = 0;
          let percentCount = 0;
          let nonEmptyCount = 0;

          for (const r of rawDataRows) {
            const cellVal = r[colIdx] ?? '';
            if (!cellVal || cellVal === '-' || cellVal === '—') {
              continue;
            }
            nonEmptyCount++;
            const parsed = parseNumericCell(cellVal);
            if (parsed) {
              numericCount++;
              if (parsed.isPercent) {
                percentCount++;
              }
            }
          }

          const isMostlyNumeric = colIdx > 0 && nonEmptyCount > 0 && numericCount / nonEmptyCount >= 0.6;
          const isPercentCol = isMostlyNumeric && percentCount / numericCount >= 0.5;
          const kind: ReportColumnDto['kind'] = isPercentCol
            ? 'percent'
            : isMostlyNumeric || alignments[colIdx] === 'right'
              ? 'number'
              : 'text';

          return {
            key: `c${colIdx}`,
            label: h,
            kind,
          };
        });

        const mapCellsToRow = (cells: string[]): Record<string, unknown> => {
          const rowObj: Record<string, unknown> = {};
          columns.forEach((col, colIdx) => {
            const rawCell = cells[colIdx] ?? '';
            if (col.kind === 'number' || col.kind === 'money' || col.kind === 'percent') {
              const parsed = parseNumericCell(rawCell);
              rowObj[col.key] = parsed ? parsed.num : rawCell;
            } else {
              rowObj[col.key] = rawCell;
            }
          });
          return rowObj;
        };

        let bodyCells = rawDataRows;
        let bottomRows: Record<string, unknown>[] | null = null;
        if (rawDataRows.length > 1) {
          const lastFirstCell = (rawDataRows[rawDataRows.length - 1][0] ?? '').toLowerCase();
          if (
            lastFirstCell.startsWith('разом') ||
            lastFirstCell.startsWith('всього') ||
            lastFirstCell.startsWith('усього') ||
            lastFirstCell.startsWith('итого') ||
            lastFirstCell.startsWith('всего') ||
            lastFirstCell.startsWith('total')
          ) {
            bodyCells = rawDataRows.slice(0, -1);
            bottomRows = [mapCellsToRow(rawDataRows[rawDataRows.length - 1])];
          }
        }

        result.push({
          kind: 'table',
          table: {
            columns,
            rows: bodyCells.map(mapCellsToRow),
            bottomRows,
          },
        });
        continue;
      }
    }

    mdBuffer.push(line);
    i++;
  }

  flushMd();
  return result;
}

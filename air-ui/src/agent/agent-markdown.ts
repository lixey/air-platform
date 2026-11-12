export { splitMarkdownAndGfmTables } from './agent-gfm-table';

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

const ALLOWED_ATTRS = new Set(['href', 'title', 'target', 'rel', 'colspan', 'rowspan', 'align', 'open']);

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function isAllowedHref(rawHref: string): boolean {
  const normalizedUrl = rawHref.replace(/[\u0000-\u0020]+/g, '');
  return /^(?:https?:\/\/|mailto:|\/|#)/i.test(normalizedUrl);
}

function tokenizeSafeHtml(rawHtml: string, safeTokens: string[]): string {
  const withoutDangerousBlocks = rawHtml
    .replace(/<(script|style|iframe|object|embed|applet|form|meta|link)\b[^>]*>[\s\S]*?<\/\1>/gi, '')
    .replace(/<(script|style|iframe|object|embed|applet|form|meta|link|input|button|textarea)\b[^>]*\/?>/gi, '')
    .replace(/<!--[\s\S]*?-->/g, '');

  const tokenized = withoutDangerousBlocks.replace(/<\/?([a-zA-Z][a-zA-Z0-9]*)\b([^>]*)>/g, (full, tagName: string, attrsRaw: string) => {
    const lowerTag = tagName.toLowerCase();
    if (!ALLOWED_TAGS.has(lowerTag)) {
      return escapeHtml(full);
    }

    if (full.startsWith('</')) {
      const idx = safeTokens.length;
      safeTokens.push(`</${lowerTag}>`);
      return `\u0000TOK${idx}\u0000`;
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
        if (!isAllowedHref(attrVal)) {
          continue;
        }
        cleanAttrs.push(`href="${escapeHtml(attrVal.trim())}" target="_blank" rel="noopener noreferrer"`);
      } else if (attrName === 'open' && !attrVal) {
        cleanAttrs.push('open');
      } else if (attrVal) {
        cleanAttrs.push(`${attrName}="${escapeHtml(attrVal)}"`);
      }
    }

    const attrStr = cleanAttrs.length > 0 ? ' ' + cleanAttrs.join(' ') : '';
    const rendered = selfClosing && (lowerTag === 'br' || lowerTag === 'hr') ? `<${lowerTag}${attrStr} />` : `<${lowerTag}${attrStr}>`;
    const idx = safeTokens.length;
    safeTokens.push(rendered);
    return `\u0000TOK${idx}\u0000`;
  });

  return tokenized.replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function restoreTokens(text: string, safeTokens: readonly string[]): string {
  let current = text;
  for (let pass = 0; pass < 3 && current.includes('\u0000TOK'); pass++) {
    current = current.replace(/\u0000TOK(\d+)\u0000/g, (_m, idxStr: string) => safeTokens[Number(idxStr)] ?? '');
  }
  return current;
}

/**
 * Sanitizes an HTML string against XSS while preserving safe formatting, table, and layout tags.
 */
export function sanitizeAgentHtml(rawHtml: string): string {
  const safeTokens: string[] = [];
  return restoreTokens(tokenizeSafeHtml(rawHtml, safeTokens), safeTokens);
}

function applyInlineEmphasis(text: string): string {
  return text
    .replace(/\*\*([^*]+)\*\*/g, '<strong class="font-semibold text-ink">$1</strong>')
    .replace(/(?<![a-zA-Z0-9])__([^_]+)__(?![a-zA-Z0-9])/g, '<strong class="font-semibold text-ink">$1</strong>')
    .replace(/(?<!\*)\*([^*\n]+)\*(?!\*)/g, '<em class="italic">$1</em>')
    .replace(/~~([^~]+)~~/g, '<del class="line-through opacity-70">$1</del>');
}

function renderInlineMarkdown(line: string): string {
  const tokens: string[] = [];
  const stash = (rendered: string): string => {
    const idx = tokens.length;
    tokens.push(rendered);
    return `\u0000TOK${idx}\u0000`;
  };

  let text = line.replace(/`([^`\n]+)`/g, (_m, code: string) =>
    stash(`<code class="rounded bg-surface-3 px-1.5 py-0.5 font-mono text-[12px] text-ink">${escapeHtml(code)}</code>`),
  );

  text = text.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (full, rawLabel: string, rawUrl: string) => {
    const trimmedUrl = rawUrl.trim();
    if (!isAllowedHref(trimmedUrl)) {
      return full;
    }
    const labelHtml = applyInlineEmphasis(tokenizeSafeHtml(rawLabel, tokens));
    return stash(
      `<a href="${escapeHtml(trimmedUrl)}" target="_blank" rel="noopener noreferrer" class="font-medium text-link underline underline-offset-2 hover:text-link-hover">${labelHtml}</a>`,
    );
  });

  text = tokenizeSafeHtml(text, tokens);
  text = applyInlineEmphasis(text);
  return restoreTokens(text, tokens);
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
      const alertTone =
        kind === 'WARNING' || kind === 'CAUTION'
          ? 'hue-amber border-(--btn-edge) bg-(--btn-ground) text-(--btn-ink)'
          : kind === 'TIP'
            ? 'hue-emerald border-(--btn-edge) bg-(--btn-ground) text-(--btn-ink)'
            : 'hue-sky border-(--btn-edge) bg-(--btn-ground) text-(--btn-ink)';
      out.push(
        `<blockquote class="my-1.5 rounded-r-lg border-l-3 ${alertTone} px-3 py-2 text-xs leading-relaxed">${bodyLines.map(renderInlineMarkdown).join('<br />')}</blockquote>`,
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

import { describe, expect, it } from 'vitest';
import { renderMarkdownToSafeHtml, sanitizeAgentHtml } from './agent-markdown';

describe('agent-markdown security & rendering', () => {
  it('strips untrusted class, style, and event attributes from raw HTML', () => {
    const raw =
      '<div class="fixed inset-0 z-50 bg-surface" style="position:fixed" onclick="alert(1)"><span class="text-red-500">Hello</span></div>';
    const clean = sanitizeAgentHtml(raw);

    expect(clean).toBe('<div><span>Hello</span></div>');
    expect(clean).not.toContain('class=');
    expect(clean).not.toContain('style=');
    expect(clean).not.toContain('onclick');
  });

  it('rejects javascript: URLs while keeping https:// links with rel="noopener noreferrer"', () => {
    const raw = '<a href="javascript:alert(1)" class="spoof">Bad</a> <a href="https://example.com/docs">Docs</a>';
    const clean = sanitizeAgentHtml(raw);

    expect(clean).toBe('<a>Bad</a> <a href="https://example.com/docs" target="_blank" rel="noopener noreferrer">Docs</a>');
  });

  it('preserves platform semantic classes for trusted Markdown syntax while stripping injected HTML classes', () => {
    const md = 'Here is **bold** and `code` plus <span class="spoofed-ui-banner">inline html</span>.';
    const rendered = renderMarkdownToSafeHtml(md);

    expect(rendered).toContain('<strong class="font-semibold text-ink">bold</strong>');
    expect(rendered).toContain('<code class="rounded bg-surface-3');
    expect(rendered).toContain('<span>inline html</span>');
    expect(rendered).not.toContain('spoofed-ui-banner');
  });
});

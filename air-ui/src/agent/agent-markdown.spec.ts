import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AgentHtmlBlockComponent } from './agent-html-block.component';
import { renderMarkdownToSafeHtml, sanitizeAgentHtml, splitMarkdownAndGfmTables } from './agent-markdown';
import { AgentMarkdownBlockComponent } from './agent-markdown-block.component';

describe('agent-markdown security & rendering', () => {
  it('strips untrusted class, style, and event attributes from raw HTML', () => {
    const raw =
      '<div class="fixed inset-0 z-50 bg-surface" style="position:fixed" onclick="alert(1)"><span class="spoofed">Hello</span></div>';
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

  it('never injects <strong>, <em>, or <del> inside href attributes when URLs contain underscores, asterisks, or tildes', () => {
    const url = 'https://example.com/pkg/__init__.py?glob=*test*&rev=~~draft~~';
    const md = `Open [**init** *module*](${url}) or <a href="${url}">raw link</a>.`;
    const rendered = renderMarkdownToSafeHtml(md);

    expect(rendered).toContain(`href="https://example.com/pkg/__init__.py?glob=*test*&amp;rev=~~draft~~"`);
    expect(rendered).toContain('<strong class="font-semibold text-ink">init</strong>');
    expect(rendered).toContain('<em class="italic">module</em>');
    expect(rendered).not.toContain('href="https://example.com/pkg/<strong');
    expect(rendered).not.toContain('<em class="italic">test</em>"');
    expect(rendered).not.toContain('<del class="line-through opacity-70">draft</del>"');
  });

  it('renders alert blockquotes with semantic hue roles and parses generic GFM tables with bold summary rows', () => {
    const alertHtml = renderMarkdownToSafeHtml('> [!WARNING] Check stock levels');
    expect(alertHtml).toContain('hue-amber');
    expect(alertHtml).not.toMatch(/\b(?:amber|emerald|sky)-\d+\b/);

    const tableMd = [
      '| Category | Amount | Share |',
      '| --- | ---: | ---: |',
      '| Parts | $1,250.50 | 80% |',
      '| Shipping | 312.50 USD | 20% |',
      '| **Total** | **$1,563.00** | **100%** |',
    ].join('\n');

    const segments = splitMarkdownAndGfmTables(tableMd);
    expect(segments).toHaveLength(1);
    expect(segments[0].kind).toBe('table');
    if (segments[0].kind === 'table') {
      expect(segments[0].table.columns.map(c => c.kind)).toEqual(['text', 'number', 'percent']);
      expect(segments[0].table.rows).toHaveLength(2);
      expect(segments[0].table.rows[0].cells).toEqual({ c0: 'Parts', c1: 1250.5, c2: 80 });
      expect(segments[0].table.bottomRows).toHaveLength(1);
      expect(segments[0].table.bottomRows?.[0].cells).toEqual({ c0: 'Total', c1: 1563, c2: 100 });
    }
  });

  it('passes <ui_html> and markdown blocks through Angular real DOM sanitizer without bypassSecurityTrustHtml', () => {
    const htmlFixture = TestBed.createComponent(AgentHtmlBlockComponent);
    htmlFixture.componentRef.setInput(
      'html',
      '<table><tr><th>Metric</th></tr><tr><td><svg><style><img src=x onerror=alert(1)></style></svg>42</td></tr></table>',
    );
    htmlFixture.detectChanges();
    const htmlEl = htmlFixture.nativeElement as HTMLElement;
    expect(htmlEl.innerHTML).not.toContain('onerror');
    expect(htmlEl.textContent).toContain('Metric');
    expect(htmlEl.textContent).toContain('42');

    const mdFixture = TestBed.createComponent(AgentMarkdownBlockComponent);
    mdFixture.componentRef.setInput('content', '**Safe** `code` and [link](https://example.com)');
    mdFixture.detectChanges();
    const mdEl = mdFixture.nativeElement as HTMLElement;
    expect(mdEl.querySelector('strong.font-semibold')).toBeTruthy();
    expect(mdEl.querySelector('code.font-mono')).toBeTruthy();
    expect(mdEl.querySelector('a')?.getAttribute('href')).toBe('https://example.com');
  });
});

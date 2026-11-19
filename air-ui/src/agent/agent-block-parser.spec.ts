import { describe, expect, it } from 'vitest';
import { DEFAULT_AGENT_BLOCK_PLUGINS } from './agent-block-models';
import { parseAgentBlocks, stripAgentControlBlocks } from './agent-block-parser';

describe('agent-block-parser', () => {
  it('strips both <ui_proposal> and <agent_memory> blocks including trailing incomplete tags during streaming', () => {
    const streaming = [
      'Saved your preference.',
      '<agent_memory>{"kind":"Preference","title":"Default currency","content":"Prefer USD"}</agent_memory>',
      'Preparing draft:',
      '<ui_proposal>{"service":"ecom-order","action":"create"',
    ].join('\n\n');

    expect(stripAgentControlBlocks(streaming)).toBe('Saved your preference.\n\nPreparing draft:');

    const blocks = parseAgentBlocks(streaming, DEFAULT_AGENT_BLOCK_PLUGINS, 'm1');
    expect(blocks).toHaveLength(1);
    expect(blocks[0]).toMatchObject({
      kind: 'markdown',
      content: 'Saved your preference.\n\nPreparing draft:',
    });
  });

  it('strips fenced <agent_memory> blocks and parses structured <ui_kpi> blocks cleanly', () => {
    const text = [
      'Overview ready.',
      '```xml\n<agent_memory>{"kind":"Fact","title":"Office","content":"Kyiv"}</agent_memory>\n```',
      '<ui_kpi>[{"label":"Orders","value":12}]</ui_kpi>',
    ].join('\n\n');

    const blocks = parseAgentBlocks(text, DEFAULT_AGENT_BLOCK_PLUGINS, 'm2');
    expect(blocks.map(b => b.kind)).toEqual(['markdown', 'kpi']);
  });

  it('preserves inline code mentions of <ui_*> and control tags without splitting or truncating prose', () => {
    const prose = [
      'Графіки працюють так: я вписую блок `<ui_chart>...</ui_chart>`, а інтерфейс його малює.',
      'Також можна згадати `<ui_chart>`, `<ui_proposal>` або `<agent_memory>` у тексті без обрізання кінця відповіді.',
    ].join('\n\n');

    expect(stripAgentControlBlocks(prose)).toBe(prose);

    const blocks = parseAgentBlocks(prose, DEFAULT_AGENT_BLOCK_PLUGINS, 'm3');
    expect(blocks).toHaveLength(1);
    expect(blocks[0]).toMatchObject({
      kind: 'markdown',
      content: prose,
    });
  });

  it('still parses real blocks and strips real control blocks that follow an inline mention of the same tag', () => {
    const chartAfterMention = [
      'Ось графік через `<ui_chart>`:',
      '<ui_chart>{"kind":"bar","title":"Sales","categories":["A","B"],"series":[{"name":"S","data":[1,2]}]}</ui_chart>',
    ].join('\n\n');

    const chartBlocks = parseAgentBlocks(chartAfterMention, DEFAULT_AGENT_BLOCK_PLUGINS, 'm3b');
    expect(chartBlocks.map(b => b.kind)).toEqual(['markdown', 'chart']);
    expect(chartBlocks[0]).toMatchObject({ kind: 'markdown', content: 'Ось графік через `<ui_chart>`:' });

    const proposalAfterMention = [
      'Створюю чернетку через `<ui_proposal>`:',
      '<ui_proposal>{"service":"ecom/order","operation":"create"}</ui_proposal>',
    ].join('\n\n');
    expect(stripAgentControlBlocks(proposalAfterMention)).toBe('Створюю чернетку через `<ui_proposal>`:');
  });

  it('does not let stray backticks inside blocks bridge across lines and erase subsequent blocks', () => {
    const text = [
      '<ui_html><p>a ` b</p></ui_html>',
      '<ui_chart>{"kind":"bar","title":"c ` d","categories":["X"],"series":[{"name":"S","data":[5]}]}</ui_chart>',
    ].join('\n');

    const blocks = parseAgentBlocks(text, DEFAULT_AGENT_BLOCK_PLUGINS, 'm3c');
    expect(blocks.map(b => b.kind)).toEqual(['html', 'chart']);
  });

  it('coalesces surrounding markdown when an unparseable visual block is skipped', () => {
    const text = 'Intro paragraph.\n\n<ui_chart>not-valid-json</ui_chart>\n\nOutro paragraph.';
    const blocks = parseAgentBlocks(text, DEFAULT_AGENT_BLOCK_PLUGINS, 'm4');
    expect(blocks).toHaveLength(1);
    expect(blocks[0]).toMatchObject({
      kind: 'markdown',
      content: 'Intro paragraph.\n\nOutro paragraph.',
    });
  });

  it('normalizes both native series and Chart.js datasets shapes in <ui_chart>', () => {
    const barText =
      '<ui_chart>{"type":"bar","title":"Sales","labels":["Jan","Feb"],"datasets":[{"label":"Revenue","data":[10,20]}]}</ui_chart>';
    const barBlocks = parseAgentBlocks(barText, DEFAULT_AGENT_BLOCK_PLUGINS, 'm5');
    expect(barBlocks).toHaveLength(1);
    expect(barBlocks[0]).toMatchObject({
      kind: 'chart',
      chart: {
        kind: 'bar',
        categories: ['Jan', 'Feb'],
        series: [{ name: 'Revenue', data: [10, 20] }],
      },
    });

    const donutText = '<ui_chart>{"kind":"donut","title":"Share","labels":["A","B"],"datasets":[{"data":[30,70]}]}</ui_chart>';
    const donutBlocks = parseAgentBlocks(donutText, DEFAULT_AGENT_BLOCK_PLUGINS, 'm6');
    expect(donutBlocks).toHaveLength(1);
    expect(donutBlocks[0]).toMatchObject({
      kind: 'chart',
      chart: {
        kind: 'donut',
        slices: [
          { name: 'A', value: 30 },
          { name: 'B', value: 70 },
        ],
      },
    });
  });
});

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
});

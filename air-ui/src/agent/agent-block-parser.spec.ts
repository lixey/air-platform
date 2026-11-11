import { describe, expect, it } from 'vitest';
import { DEFAULT_AGENT_BLOCK_PLUGINS } from './agent-block-models';
import { parseAgentBlocks, stripAgentControlBlocks } from './agent-block-parser';

describe('agent-block-parser', () => {
  it('strips both <air_proposal> and <air_memory> blocks including trailing incomplete tags during streaming', () => {
    const streaming = [
      'Saved your preference.',
      '<air_memory>{"kind":"Preference","title":"Default currency","content":"Prefer USD"}</air_memory>',
      'Preparing draft:',
      '<air_proposal>{"service":"ecom-order","action":"create"',
    ].join('\n\n');

    expect(stripAgentControlBlocks(streaming)).toBe('Saved your preference.\n\nPreparing draft:');

    const blocks = parseAgentBlocks(streaming, DEFAULT_AGENT_BLOCK_PLUGINS, 'm1');
    expect(blocks).toHaveLength(1);
    expect(blocks[0]).toMatchObject({
      kind: 'markdown',
      content: 'Saved your preference.\n\nPreparing draft:',
    });
  });

  it('strips fenced <air_memory> blocks and parses structured <air_kpi> blocks cleanly', () => {
    const text = [
      'Overview ready.',
      '```xml\n<air_memory>{"kind":"Fact","title":"Office","content":"Kyiv"}</air_memory>\n```',
      '<air_kpi>[{"label":"Orders","value":12}]</air_kpi>',
    ].join('\n\n');

    const blocks = parseAgentBlocks(text, DEFAULT_AGENT_BLOCK_PLUGINS, 'm2');
    expect(blocks.map(b => b.kind)).toEqual(['markdown', 'kpi']);
  });
});

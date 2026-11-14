import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { credentialsInterceptor } from '../app/auth/credentials.interceptor';
import { OverlayService } from '../overlay/overlay.service';
import { type AgentBlockPlugin, type AgentCustomBlock, DEFAULT_AGENT_BLOCK_PLUGINS, provideAgentBlocks } from './agent-block-models';
import { parseAgentBlocks } from './agent-block-parser';
import { AgentComposerComponent } from './agent-composer.component';
import { AgentMessageItemComponent } from './agent-message-item.component';
import type { AgentChatMessage } from './agent-models';
import { AgentTableBlockComponent } from './agent-table-block.component';
import { AgentService } from './agent.service';

@Component({
  selector: 'test-custom-agent-block',
  template: `
    <div data-custom-block>{{ block().customKind }}:{{ block().tagName }}</div>
  `,
})
class TestCustomAgentBlockComponent {
  readonly block = input.required<AgentCustomBlock>();
}

describe('Agent SSE CRLF streaming, block narrowing, file-drop guard, and ReportTable reuse', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([credentialsInterceptor])),
        provideHttpClientTesting(),
        provideAgentBlocks(DEFAULT_AGENT_BLOCK_PLUGINS),
        AgentService,
        {
          provide: OverlayService,
          useValue: {
            open: vi.fn(),
            menu: () => ({ isOpen: () => false, id: () => null, ref: null, open: vi.fn(), close: vi.fn() }),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    http.verify();
  });

  it('parses CRLF (\\r\\n\\r\\n) SSE frames even when split mid-boundary across chunks', async () => {
    const chunk1 = 'data: {"type":"delta","text":"Hello "}\r\n\r';
    const chunk2 = '\ndata: {"type":"delta","text":"CRLF world"}\r\n\r\ndata: {"type":"done","modelId":"agy","modelName":"Agy"}\r\n\r\n';
    const encoder = new TextEncoder();

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        new ReadableStream({
          start(controller) {
            controller.enqueue(encoder.encode(chunk1));
            controller.enqueue(encoder.encode(chunk2));
            controller.close();
          },
        }),
        { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
      ),
    );

    const agent = TestBed.inject(AgentService);
    await agent.send('Test CRLF stream');

    const assistant = agent.messages().find(m => m.role === 'assistant');
    expect(assistant?.text).toBe('Hello CRLF world');
    expect(assistant?.modelName).toBe('Agy');
  });

  it('never flashes raw JSON when streaming starts with an incomplete <ui_table> or <ui_chart> block (m.blocks = [])', () => {
    const partialStreamText = '<ui_table>{"title":"Sales","columns":[{"key":"sku","label":"SKU"}';
    const blocks = parseAgentBlocks(partialStreamText, DEFAULT_AGENT_BLOCK_PLUGINS, 'msg-stream');
    expect(blocks).toEqual([]);

    const streamingMsg: AgentChatMessage = {
      id: 'msg-stream',
      role: 'assistant',
      text: partialStreamText,
      blocks,
      statusText: 'Generating table...',
      proposals: [],
      memories: [],
      attachments: [],
      createdAt: new Date(),
    };

    const fixture = TestBed.createComponent(AgentMessageItemComponent);
    fixture.componentRef.setInput('msg', streamingMsg);
    fixture.detectChanges();

    const renderedText = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(renderedText).not.toContain('<ui_table>');
    expect(renderedText).not.toContain('"columns"');
    expect(renderedText).toContain('Generating table...');
  });

  it('narrows custom blocks strictly via kind: "custom" without $any casts', () => {
    const customPlugin: AgentBlockPlugin = {
      kind: 'widget',
      tag: 'ui_widget',
      component: TestCustomAgentBlockComponent,
      parse: (rawJson, id) => [
        {
          id,
          kind: 'custom',
          customKind: 'widget',
          tagName: 'ui_widget',
          data: JSON.parse(rawJson),
          component: TestCustomAgentBlockComponent,
        },
      ],
    };

    const blocks = parseAgentBlocks('<ui_widget>{"ok":true}</ui_widget>', [...DEFAULT_AGENT_BLOCK_PLUGINS, customPlugin], 'm-c');
    expect(blocks).toHaveLength(1);
    expect(blocks[0].kind).toBe('custom');

    const msg: AgentChatMessage = {
      id: 'm-c',
      role: 'assistant',
      text: '<ui_widget>{"ok":true}</ui_widget>',
      blocks,
      proposals: [],
      memories: [],
      attachments: [],
      createdAt: new Date(),
    };

    const fixture = TestBed.createComponent(AgentMessageItemComponent);
    fixture.componentRef.setInput('msg', msg);
    fixture.detectChanges();

    const customEl = (fixture.nativeElement as HTMLElement).querySelector('[data-custom-block]');
    expect(customEl?.textContent).toBe('widget:ui_widget');
  });

  it('prevents browser file navigation on drop while streaming without uploading files', () => {
    const agent = TestBed.inject(AgentService);
    agent.isStreaming.set(true);
    const uploadSpy = vi.spyOn(agent, 'uploadFiles').mockResolvedValue();

    const fixture = TestBed.createComponent(AgentComposerComponent);
    fixture.detectChanges();

    const textarea = (fixture.nativeElement as HTMLElement).querySelector('textarea') as HTMLTextAreaElement;
    const file = new File(['png'], 'photo.png', { type: 'image/png' });
    const dropEvent = new Event('drop', { bubbles: true, cancelable: true }) as DragEvent;
    Object.defineProperty(dropEvent, 'dataTransfer', { value: { files: [file], types: ['Files'] } });
    textarea.dispatchEvent(dropEvent);

    expect(dropEvent.defaultPrevented).toBe(true);
    expect(uploadSpy).not.toHaveBeenCalled();
  });

  it('reuses <ui-report-table> in AgentTableBlockComponent with sorting, fractional precision, and expand action slot', () => {
    const overlay = TestBed.inject(OverlayService);
    const fixture = TestBed.createComponent(AgentTableBlockComponent);
    fixture.componentRef.setInput('table', {
      title: 'Top Margin Rates',
      columns: [
        { key: 'name', label: 'Item', kind: 'text' },
        { key: 'rate', label: 'Rate', kind: 'number' },
      ],
      rows: [{ cells: { name: 'Alpha', rate: 12.45 } }, { cells: { name: 'Beta', rate: 3.125 } }],
    });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('ui-report-table')).toBeTruthy();
    expect(el.textContent).toContain('Top Margin Rates');
    expect(el.textContent).toContain('12.45');
    expect(el.textContent).toContain('3.125');
    const expandBtn = el.querySelector('ui-btn[icon="fullscreen"]') as HTMLElement;
    expect(expandBtn).toBeTruthy();
    expandBtn.click();
    expect(overlay.open).toHaveBeenCalledTimes(1);
  });
});

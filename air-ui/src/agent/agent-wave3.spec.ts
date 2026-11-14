import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { credentialsInterceptor, PLATFORM_IDEMPOTENCY_KEY_HEADER } from '../app/auth/credentials.interceptor';
import { OverlayService } from '../overlay/overlay.service';
import { DEFAULT_AGENT_BLOCK_PLUGINS, provideAgentBlocks } from './agent-block-models';
import { AgentMemoryPillComponent } from './agent-memory-pill.component';
import type { AgentProposal } from './agent-models';
import { AgentProposalCardComponent } from './agent-proposal-card.component';
import { AgentService } from './agent.service';

describe('Wave 3 & Wave 4 Agent UI capabilities', () => {
  let http: HttpTestingController;
  let openedOverlay: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    openedOverlay = vi.fn(() => ({ closed: new Subject<unknown>() }));
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([credentialsInterceptor])),
        provideHttpClientTesting(),
        provideAgentBlocks(DEFAULT_AGENT_BLOCK_PLUGINS),
        AgentService,
        { provide: OverlayService, useValue: { open: openedOverlay } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    http.verify();
  });

  it('renders multi-child <ui_proposal> cards across 2+ domain-agnostic child tables and replace/append modes', () => {
    const proposal: AgentProposal = {
      id: 'prop-multi-1',
      sessionId: 'sess-1',
      operation: 'update',
      service: 'qa/inspection-form',
      title: 'Inspection #42',
      badge: 'Edit',
      badgeColor: 'amber',
      fields: [{ label: 'Station', value: 'Line B' }],
      children: [
        {
          childKey: 'measurements',
          childService: 'qa/measurement-grid',
          title: 'Measurements',
          mode: 'replace',
          columns: [
            { key: 'recordedAt', label: 'Recorded at', isNumeric: false },
            { key: 'temperatureC', label: 'Temp °C', isNumeric: true },
          ],
          rows: [
            { cells: { recordedAt: '2026-11-12 08:00', temperatureC: '21.4' } },
            { cells: { recordedAt: '2026-11-12 09:00', temperatureC: '22.1' } },
          ],
        },
        {
          childKey: 'checkpoints',
          childService: 'qa/checkpoint-grid',
          title: 'Checkpoints',
          mode: 'append',
          columns: [
            { key: 'code', label: 'Code', isNumeric: false },
            { key: 'passed', label: 'Passed', isNumeric: false },
          ],
          rows: [{ cells: { code: 'SEAL-01', passed: 'Yes' } }],
        },
      ],
      attachmentIds: [],
      status: 'pending',
    };

    const fixture = TestBed.createComponent(AgentProposalCardComponent);
    fixture.componentRef.setInput('proposal', proposal);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Measurements (2)');
    expect(text).toContain('Replace all');
    expect(text).toContain('21.4');
    expect(text).toContain('Checkpoints (1)');
    expect(text).toContain('Add');
    expect(text).toContain('SEAL-01');
  });

  it('renders move-status <ui_proposal> cards with transition details and status confirmation prompt', () => {
    const proposal: AgentProposal = {
      id: 'prop-flow-1',
      sessionId: 'sess-1',
      operation: 'move-status',
      service: 'ecom/order-form',
      transition: 'Approve',
      recordId: 77,
      title: '#77 · Order AC-77',
      subtitle: 'Approve order',
      badge: 'Approve order',
      badgeColor: 'green',
      fields: [
        { label: 'Status', value: 'Draft → Approved', badge: 'Changed', color: 'green' },
        { label: 'Comment', value: 'Verified payment' },
      ],
      children: [],
      attachmentIds: [],
      status: 'pending',
    };

    const fixture = TestBed.createComponent(AgentProposalCardComponent);
    fixture.componentRef.setInput('proposal', proposal);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('#77 · Order AC-77');
    expect(text).toContain('Approve order');
    expect(text).toContain('Draft → Approved');
    expect(text).toContain('Check the status change before confirming');
  });

  it('handles SSE memory events, renders the memory pill, and posts /api/agent/undo-memory with Idempotency-Key', async () => {
    const sseFrames = [
      'data: {"type":"delta","text":"Noted."}\n\n',
      'data: {"type":"memory","memory":{"id":91,"category":"Preference","title":"Default currency","content":"Prefer EUR"}}\n\n',
      'data: {"type":"done","modelId":"agy-1","modelName":"Agy"}\n\n',
    ].join('');

    const encoder = new TextEncoder();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        new ReadableStream({
          start(controller) {
            controller.enqueue(encoder.encode(sseFrames));
            controller.close();
          },
        }),
        { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
      ),
    );

    const agent = TestBed.inject(AgentService);
    await agent.send('Remember that I prefer EUR');

    const assistantMsg = agent.messages().find(m => m.role === 'assistant');
    expect(assistantMsg?.memories).toEqual([{ id: 91, category: 'Preference', title: 'Default currency', content: 'Prefer EUR' }]);

    const pillFixture = TestBed.createComponent(AgentMemoryPillComponent);
    pillFixture.componentRef.setInput('memory', assistantMsg!.memories[0]);
    let undoneId: number | null = null;
    pillFixture.componentInstance.undo.subscribe(id => {
      undoneId = id;
    });
    pillFixture.detectChanges();

    expect((pillFixture.nativeElement as HTMLElement).textContent).toContain('Remembered: Default currency');
    (pillFixture.nativeElement as HTMLElement).querySelector<HTMLElement>('ui-btn')?.click();
    expect(undoneId).toBe(91);

    const undoPromise = agent.undoMemory(assistantMsg!.id, 91);
    const req = http.expectOne('/api/agent/undo-memory');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ id: 91 });
    expect(req.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER)).toBeTruthy();
    req.flush({ outcome: 'committed' });
    await undoPromise;

    const updatedMem = agent.messages().find(m => m.id === assistantMsg!.id)?.memories[0];
    expect(updatedMem?.undone).toBe(true);

    pillFixture.componentRef.setInput('memory', updatedMem!);
    pillFixture.detectChanges();
    expect((pillFixture.nativeElement as HTMLElement).textContent).toContain('Memory removed: Default currency');
  });

  it('renders pending SSE memory items, confirms via /api/agent/confirm-memory with Idempotency-Key, and supports Dismiss', async () => {
    const sseFrames = [
      'data: {"type":"delta","text":"Shall I save this?"}\n\n',
      'data: {"type":"memory","memory":{"id":0,"category":"Preference","title":"Date format","content":"Use ISO","status":"pending","pendingToken":"tok-mem-1"}}\n\n',
      'data: {"type":"memory","memory":{"id":0,"category":"Fact","title":"Skip note","content":"Ignore","status":"pending","pendingToken":"tok-mem-2"}}\n\n',
      'data: {"type":"done","modelId":"agy-1","modelName":"Agy"}\n\n',
    ].join('');

    const encoder = new TextEncoder();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        new ReadableStream({
          start(controller) {
            controller.enqueue(encoder.encode(sseFrames));
            controller.close();
          },
        }),
        { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
      ),
    );

    const agent = TestBed.inject(AgentService);
    await agent.send('Use ISO dates');

    const assistantMsg = agent.messages().find(m => m.role === 'assistant')!;
    expect(assistantMsg.memories).toHaveLength(2);

    const pillFixture = TestBed.createComponent(AgentMemoryPillComponent);
    pillFixture.componentRef.setInput('memory', assistantMsg.memories[0]);
    let confirmedToken: string | null = null;
    pillFixture.componentInstance.confirm.subscribe(tok => {
      confirmedToken = tok;
    });
    pillFixture.detectChanges();

    expect((pillFixture.nativeElement as HTMLElement).textContent).toContain('Remember "Date format"?');
    const buttons = (pillFixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('ui-btn');
    expect(buttons).toHaveLength(2);
    buttons[0].click();
    expect(confirmedToken).toBe('tok-mem-1');

    const confirmPromise = agent.confirmMemory(assistantMsg.id, 'tok-mem-1');
    const req = http.expectOne('/api/agent/confirm-memory');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ pendingToken: 'tok-mem-1' });
    expect(req.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER)).toBeTruthy();
    req.flush({ id: 105, category: 'Preference', title: 'Date format', content: 'Use ISO', status: 'saved' });
    await confirmPromise;

    const afterConfirm = agent.messages().find(m => m.id === assistantMsg.id)!.memories;
    expect(afterConfirm[0]).toMatchObject({ id: 105, status: 'saved', title: 'Date format' });
    expect(afterConfirm[0].pendingToken).toBeUndefined();

    agent.dismissMemory(assistantMsg.id, 'tok-mem-2');
    const afterDismiss = agent.messages().find(m => m.id === assistantMsg.id)!.memories[1];
    expect(afterDismiss.dismissed).toBe(true);

    pillFixture.componentRef.setInput('memory', afterDismiss);
    pillFixture.detectChanges();
    expect((pillFixture.nativeElement as HTMLElement).textContent).toContain('Not saved: Skip note');
  });

  it('sends Idempotency-Key on confirmProposal/confirmBatch and defaults openRecord fallback presentation to drawer', async () => {
    const agent = TestBed.inject(AgentService);
    agent.messages.set([
      {
        id: 'm-1',
        role: 'assistant',
        text: 'Ready',
        attachments: [],
        memories: [],
        createdAt: new Date(),
        proposals: [
          {
            id: 'p-1',
            sessionId: 's-1',
            operation: 'create',
            service: 'ecom/order-form',
            title: 'Order 1',
            fields: [],
            children: [],
            attachmentIds: [],
            status: 'pending',
          },
          {
            id: 'p-2',
            sessionId: 's-1',
            operation: 'create',
            service: 'ecom/order-form',
            title: 'Order 2',
            fields: [],
            children: [],
            attachmentIds: [],
            status: 'pending',
          },
        ],
      },
    ]);

    // First attempt on p-1 AND p-2 fails with network unknown (status 0) -> UncertainIdempotency retains BOTH keys concurrently
    const firstTryP1 = agent.confirmProposal('m-1', 'p-1');
    const req1 = http.expectOne('/api/agent/confirm-proposal');
    expect(req1.request.body).toEqual({ proposalId: 'p-1' });
    const firstKeyP1 = req1.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(firstKeyP1).toBeTruthy();
    req1.error(new ProgressEvent('error'));
    await firstTryP1;

    const firstTryP2 = agent.confirmProposal('m-1', 'p-2');
    const reqP2 = http.expectOne('/api/agent/confirm-proposal');
    expect(reqP2.request.body).toEqual({ proposalId: 'p-2' });
    const firstKeyP2 = reqP2.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(firstKeyP2).toBeTruthy();
    expect(firstKeyP2).not.toBe(firstKeyP1);
    reqP2.error(new ProgressEvent('error'));
    await firstTryP2;

    // Retrying p-1 still reuses firstKeyP1 (not evicted by p-2's failure)
    const secondTry = agent.confirmProposal('m-1', 'p-1');
    const req2 = http.expectOne('/api/agent/confirm-proposal');
    expect(req2.request.body).toEqual({ proposalId: 'p-1' });
    expect(req2.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER)).toBe(firstKeyP1);
    req2.flush({
      proposalId: 'p-1',
      recordId: 501,
      recordLabel: '#501',
      formService: 'ecom/order-form',
      refreshServices: [],
    });
    await secondTry;

    // confirmBatch also sends Idempotency-Key via canonical /api/agent/confirm-batch
    const batchTry = agent.confirmBatch('m-1');
    const batchReq = http.expectOne('/api/agent/confirm-batch');
    expect(batchReq.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER)).toBeTruthy();
    expect(batchReq.request.body).toEqual({ proposalIds: ['p-2'] });
    batchReq.flush({
      committed: [{ proposalId: 'p-2', recordId: 502, recordLabel: '#502', formService: 'ecom/order-form', refreshServices: [] }],
      refreshServices: [],
    });
    await batchTry;

    // openRecord without explicit proposal.overlay defaults presentation to 'drawer'
    const committedProposal = agent.messages()[0].proposals[0];
    agent.openRecord(committedProposal);
    expect(openedOverlay).toHaveBeenCalledWith(
      expect.objectContaining({
        presentation: 'drawer',
        screen: expect.objectContaining({ service: 'ecom/order-form', params: { id: 501 } }),
      }),
    );
  });

  it('posts canonical /api/agent/get-status, /api/agent/reset, and /api/agent/upload via typed Api', async () => {
    const agent = TestBed.inject(AgentService);

    const statusPromise = agent.ensureStatusLoaded();
    const statusReq = http.expectOne('/api/agent/get-status');
    expect(statusReq.request.method).toBe('POST');
    expect(statusReq.request.body).toEqual({});
    statusReq.flush({
      activeProvider: 'Agy',
      defaultModel: 'agy-pro',
      providers: [{ handle: 'Agy', name: 'Agy', isAvailable: true }],
      models: [{ id: 'agy-pro', name: 'Agy Pro', provider: 'Agy', isDefault: true, isForPhotos: false }],
      syncSources: [],
    });
    await statusPromise;
    expect(agent.model()).toBe('agy-pro');

    const prevSession = agent.sessionId();
    const resetPromise = agent.reset();
    const resetReq = http.expectOne('/api/agent/reset');
    expect(resetReq.request.method).toBe('POST');
    expect(resetReq.request.body).toEqual({ sessionId: prevSession });
    resetReq.flush({ outcome: 'committed' });
    await resetPromise;
    expect(agent.sessionId()).not.toBe(prevSession);

    const file = new File(['hello'], 'notes.txt', { type: 'text/plain' });
    const uploadPromise = agent.uploadSingleFile(file);
    await new Promise(resolve => setTimeout(resolve, 0));
    const uploadReq = http.expectOne('/api/agent/upload');
    expect(uploadReq.request.method).toBe('POST');
    expect(uploadReq.request.body instanceof FormData).toBe(true);
    uploadReq.flush({ id: 'att-1', fileName: 'notes.txt', contentType: 'text/plain', size: 5 });
    const uploaded = await uploadPromise;
    expect(uploaded?.id).toBe('att-1');
  });
});

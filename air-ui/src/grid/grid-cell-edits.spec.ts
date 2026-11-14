import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { errorAlertInterceptor } from '../app/alert/error-alert.interceptor';
import { credentialsInterceptor, PLATFORM_IDEMPOTENCY_KEY_HEADER } from '../app/auth/credentials.interceptor';
import type { GridColumnDto } from '../core/generated/contracts';
import { GridCellEdits } from './grid-cell-edits';
import type { GridRow } from './grid-row';

function editableTextColumn(path: string, label: string): GridColumnDto {
  return {
    path,
    label,
    type: 'text',
    isSortable: true,
    isFilterable: true,
    isResizable: true,
    isSticky: false,
    isStickyRight: false,
    cell: { kind: 'GridCellText' },
    operators: [],
    isEditable: true,
    isHidden: false,
    isDisabled: false,
    isRequired: false,
  };
}

describe('GridCellEdits multi-slot uncertain idempotency', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('retains unanswered cell Idempotency-Keys across cancel() and edits of neighboring cells', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([credentialsInterceptor, errorAlertInterceptor])), provideHttpClientTesting()],
    });

    const http = TestBed.inject(HttpTestingController);
    const titleCol = editableTextColumn('title', 'Title');
    const row1: GridRow = { id: 1, ver: 1, cells: { title: 'Row 1' } };
    const row2: GridRow = { id: 2, ver: 1, cells: { title: 'Row 2' } };
    const rows = signal<readonly GridRow[]>([row1, row2]);

    const edits = TestBed.runInInjectionContext(
      () =>
        new GridCellEdits({
          resetBy: () => 0,
          model: () => null,
          service: () => 'item-grid',
          params: () => ({}),
          rows,
          columns: () => [titleCol],
          isPicking: () => false,
          capture: () => () => true,
          beginMutation: () => undefined,
          finishMutation: () => undefined,
          adoptRow: () => undefined,
          moveActive: () => undefined,
          reload: () => undefined,
        }),
    );

    const url = '/api/item-grid/save-cell';

    // 1. Edit cell A -> 503 network/server failure leaves outcome uncertain
    expect(edits.start(row1, titleCol)).toBe(true);
    edits.update('Alpha');
    const commitA1 = edits.commit('stay');
    const reqA1 = http.expectOne(url);
    const keyA = reqA1.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(keyA).toBeTruthy();
    reqA1.flush('Gateway timeout', { status: 503, statusText: 'Service Unavailable' });
    await commitA1;

    // 2. Cancel cell A and edit neighboring cell B (succeeds) -> cell A's uncertain key must NOT be wiped
    edits.cancel();
    expect(edits.start(row2, titleCol)).toBe(true);
    edits.update('Beta');
    const commitB = edits.commit('stay');
    const reqB = http.expectOne(url);
    const keyB = reqB.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(keyB).toBeTruthy();
    expect(keyB).not.toBe(keyA);
    reqB.flush({ outcome: 'committed', row: { id: 2, ver: 2, cells: { title: 'Beta' } } });
    await commitB;

    // 3. Saving cell A with a different value uses a fresh key without evicting 'Alpha'
    expect(edits.start(row1, titleCol)).toBe(true);
    edits.update('Alpha-Alt');
    const commitAlt = edits.commit('stay');
    const reqAlt = http.expectOne(url);
    expect(reqAlt.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER)).not.toBe(keyA);
    reqAlt.flush(
      { outcome: 'refused', messages: [{ type: 'error', path: 'title', detail: 'Invalid' }] },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await commitAlt;

    // 4. Retrying cell A with the original value 'Alpha' reuses keyA, then clears it once committed
    edits.update('Alpha');
    const commitA2 = edits.commit('stay');
    const reqA2 = http.expectOne(url);
    expect(reqA2.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER)).toBe(keyA);
    reqA2.flush({ outcome: 'committed', row: { id: 1, ver: 2, cells: { title: 'Alpha' } } });
    await commitA2;

    // 5. Subsequent edit of cell A back to 'Alpha' (if row reverted) gets a fresh key
    expect(edits.start(row1, titleCol)).toBe(true);
    edits.update('Alpha');
    const commitA3 = edits.commit('stay');
    const reqA3 = http.expectOne(url);
    expect(reqA3.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER)).not.toBe(keyA);
    reqA3.flush({ outcome: 'committed', row: { id: 1, ver: 3, cells: { title: 'Alpha' } } });
    await commitA3;

    http.verify();
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { navModel } from '../../tests/nav-model';
import type { GridColumnDto, OverlayDto } from '../core/generated/contracts';
import { NavComponent } from '../nav/nav.component';
import { resolveOverlay } from '../overlay/overlay-box';
import type { OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { GridCellEditorComponent } from './cell/grid-cell-editor.component';
import { fieldOf, type GridFilter } from './filter/grid-filter-set';
import { GridCellEdits } from './grid-cell-edits';
import { type GridRow, rowColorAt } from './grid-row';
import { GridService } from './grid.service';
import { GridHeaderDefaultComponent } from './header/grid-header-default.component';

function textCol(path: string, label: string, isEditable = false): GridColumnDto {
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
    operators: [
      { handle: 'matches', label: 'Contains', symbol: '~' },
      { handle: 'equals', label: 'Equals', symbol: '=' },
    ],
    isEditable,
    isHidden: false,
    isDisabled: false,
    isRequired: false,
  };
}

function numberCol(path: string, label: string, isEditable = false): GridColumnDto {
  return {
    path,
    label,
    type: 'number',
    isSortable: true,
    isFilterable: true,
    isResizable: true,
    isSticky: false,
    isStickyRight: false,
    cell: { kind: 'GridCellNumber' },
    operators: [
      { handle: 'equals', label: 'Equals', symbol: '=' },
      { handle: 'greaterThan', label: 'Greater than', symbol: '>' },
    ],
    isEditable,
    isHidden: false,
    isDisabled: false,
    isRequired: false,
  };
}

describe('GridHeaderDefaultComponent raw input retention and rowColorAt guard', () => {
  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('never paints a cell from its own text when colorPath equals columnPath', () => {
    const row: GridRow = { id: 1, cells: { name: 'Green', status: { id: 1, name: 'Paid', color: 'emerald' } } };
    expect(rowColorAt(row, 'name', 'name')).toBeNull();
    expect(rowColorAt({ ...row, colors: { name: 'amber' } }, 'name', 'name')).toBe('amber');
    expect(rowColorAt(row, 'status', 'status.color')).toBe('emerald');
  });

  it('preserves intermediate numeric ("12.", "-") and trailing-space ("John ") input while focused, normalizing on blur', () => {
    vi.useFakeTimers();

    const activeFilter = signal<GridFilter | undefined>(undefined);
    const gridStub = {
      sortOf: () => null,
      sortPositionOf: () => null,
      columnFilterOf: (path: string | null | undefined) => {
        const current = activeFilter();
        return current && path === fieldOf(current) ? current : undefined;
      },
      setColumnFilter: (rule: GridFilter) => activeFilter.set(rule),
      clearColumnFilter: () => activeFilter.set(undefined),
    };

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: GridService, useValue: gridStub }],
    });

    const numFixture = TestBed.createComponent(GridHeaderDefaultComponent);
    const amountCol = numberCol('amount', 'Amount');
    numFixture.componentRef.setInput('cell', { row: null, column: amountCol, value: null, index: 0 });
    numFixture.detectChanges();

    const numInput = numFixture.nativeElement.querySelector('input') as HTMLInputElement;
    numInput.dispatchEvent(new FocusEvent('focus'));
    numInput.value = '12.';
    numInput.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(450);
    numFixture.detectChanges();

    expect(activeFilter()).toEqual({ kind: 'FilterRule', field: 'amount', operator: 'equals', value: 12 });
    expect(numInput.value).toBe('12.');

    numInput.value = '-';
    numInput.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(450);
    numFixture.detectChanges();

    expect(activeFilter()).toBeUndefined();
    expect(numInput.value).toBe('-');

    numInput.value = '12.';
    numInput.dispatchEvent(new Event('input'));
    numInput.dispatchEvent(new FocusEvent('blur'));
    numFixture.detectChanges();

    expect(activeFilter()).toEqual({ kind: 'FilterRule', field: 'amount', operator: 'equals', value: 12 });
    expect(numInput.value).toBe('12');

    activeFilter.set(undefined);
    const textFixture = TestBed.createComponent(GridHeaderDefaultComponent);
    const nameCol = textCol('name', 'Name');
    textFixture.componentRef.setInput('cell', { row: null, column: nameCol, value: null, index: 0 });
    textFixture.detectChanges();

    const textInput = textFixture.nativeElement.querySelector('input') as HTMLInputElement;
    textInput.dispatchEvent(new FocusEvent('focus'));
    textInput.value = 'John ';
    textInput.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(450);
    textFixture.detectChanges();

    expect(activeFilter()).toEqual({ kind: 'FilterRule', field: 'name', operator: 'matches', value: 'John' });
    expect(textInput.value).toBe('John ');

    textInput.dispatchEvent(new FocusEvent('blur'));
    textFixture.detectChanges();
    expect(textInput.value).toBe('John');
  });
});

describe('GridCellEdits & GridCellEditorComponent auto-commit on focusout and cell switch', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('auto-commits dirty cell edits when switching rows or columns via start(), keeping refused cells open', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);

    const row1: GridRow = { id: 1, ver: 1, cells: { title: 'Row 1', note: 'N1' } };
    const row2: GridRow = { id: 2, ver: 1, cells: { title: 'Row 2', note: 'N2' } };
    const rows = signal<readonly GridRow[]>([row1, row2]);
    const titleColumn = textCol('title', 'Title', true);
    const noteColumn = textCol('note', 'Note', true);
    const columns = signal<readonly GridColumnDto[]>([titleColumn, noteColumn]);
    const adopted: Array<{ id: number | string; row: GridRow | null }> = [];

    const edits = TestBed.runInInjectionContext(
      () =>
        new GridCellEdits({
          resetBy: () => 0,
          model: () => null,
          service: () => 'item-grid',
          params: () => ({}),
          rows,
          columns,
          isPicking: () => false,
          capture: () => () => true,
          beginMutation: () => undefined,
          finishMutation: () => undefined,
          adoptRow: (id, row) => adopted.push({ id, row }),
          moveActive: () => undefined,
          reload: () => undefined,
        }),
    );

    // 1a. Refused cross-row switch keeps Row 1 open with server validation messages instead of discarding the edit
    expect(edits.start(row1, titleColumn)).toBe(true);
    edits.update('Bad Title');
    expect(edits.start(row2, titleColumn)).toBe(true);

    const refusedSwitchReq = http.expectOne('/api/item-grid/save-cell');
    expect(refusedSwitchReq.request.body).toEqual({ id: 1, ver: 1, path: 'title', value: 'Bad Title' });
    refusedSwitchReq.flush(
      {
        outcome: 'refused',
        title: 'Invalid cell',
        messages: [{ type: 'error', path: 'title', detail: 'Title is reserved.' }],
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(edits.current()?.key).toBe(1);
    expect(edits.current()?.path).toBe('title');
    expect(edits.current()?.value).toBe('Bad Title');
    expect(edits.current()?.messages).toEqual([{ type: 'error', path: 'title', detail: 'Title is reserved.' }]);

    // 1b. Valid cross-row switch commits Row 1 and then opens Row 2
    edits.update('Row 1 Updated');
    expect(edits.start(row2, titleColumn)).toBe(true);

    const rowSwitchReq = http.expectOne('/api/item-grid/save-cell');
    expect(rowSwitchReq.request.body).toEqual({ id: 1, ver: 1, path: 'title', value: 'Row 1 Updated' });
    rowSwitchReq.flush({ data: { kind: 'committed', row: { id: 1, ver: 2, cells: { title: 'Row 1 Updated', note: 'N1' } } } });
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(adopted).toHaveLength(1);
    expect(adopted[0]?.id).toBe(1);
    expect(edits.current()?.key).toBe(2);
    expect(edits.current()?.path).toBe('title');

    // 2. Switch to another column on the SAME row while holding a dirty edit -> commits first, then opens pendingStart
    edits.update('Row 2 Updated');
    expect(edits.start(row2, noteColumn)).toBe(true);

    const colSwitchReq = http.expectOne('/api/item-grid/save-cell');
    expect(colSwitchReq.request.body).toEqual({ id: 2, ver: 1, path: 'title', value: 'Row 2 Updated' });
    colSwitchReq.flush({ data: { kind: 'committed', row: { id: 2, ver: 2, cells: { title: 'Row 2 Updated', note: 'N2' } } } });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(edits.current()?.key).toBe(2);
    expect(edits.current()?.path).toBe('note');
    http.verify();
  });

  it('auto-commits on focusout outside editor unless a child popup overlay is anchored inside the editor', async () => {
    const overlayStack = signal<readonly OverlayRef[]>([]);
    const commitSpy = vi.fn(() => Promise.resolve());
    const editSignal = signal({
      key: 1,
      path: 'title',
      value: 'Edited',
      isSaving: false,
      messages: [],
    });

    const gridStub = {
      edits: {
        current: editSignal,
        isConfirming: false,
        update: (val: unknown) => editSignal.update(cur => ({ ...cur, value: val as string })),
        commit: commitSpy,
        cancel: vi.fn(),
      },
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: GridService, useValue: gridStub },
        { provide: OverlayService, useValue: { stack: overlayStack } },
      ],
    });

    const row: GridRow = { id: 1, ver: 1, cells: { title: 'Initial' } };
    const col = textCol('title', 'Title', true);
    const fixture = TestBed.createComponent(GridCellEditorComponent);
    fixture.componentRef.setInput('cell', { row, column: col, value: 'Initial', index: 0 });
    fixture.detectChanges();

    const editorHost = fixture.nativeElement as HTMLElement;
    const input = editorHost.querySelector('input') as HTMLInputElement;
    const outsideBtn = document.createElement('button');
    document.body.appendChild(outsideBtn);

    try {
      // Case A: child overlay anchored inside editor -> focusout must NOT commit
      overlayStack.set([{ anchor: input } as unknown as OverlayRef]);
      outsideBtn.focus();
      input.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: outsideBtn }));
      await new Promise(resolve => queueMicrotask(() => resolve(undefined)));
      expect(commitSpy).not.toHaveBeenCalled();

      // Case B: no child overlay -> focusout outside editor commits with 'stay'
      overlayStack.set([]);
      outsideBtn.focus();
      input.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: outsideBtn }));
      await new Promise(resolve => queueMicrotask(() => resolve(undefined)));
      expect(commitSpy).toHaveBeenCalledWith('stay');
    } finally {
      outsideBtn.remove();
    }
  });
});

describe('Narrow menu bottom-sheet placement and auto-closing More menu', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('resolves narrow menu (< 640px) as a bottom sheet up to 85% height while keeping dialog/drawer/popover full-screen', () => {
    const narrowViewport = { left: 0, top: 0, width: 390, height: 800 };
    const units = { gap: 8, margin: 8, offset: 12 };

    const menuBox = resolveOverlay(
      { key: 'm', presentation: 'menu' },
      { work: narrowViewport, viewport: narrowViewport, anchor: { left: 300, top: 20, width: 40, height: 40 }, units },
    );
    expect(menuBox).toEqual({
      presentation: 'menu',
      layer: narrowViewport,
      align: { x: 'start', y: 'end' },
      offset: { x: 0, y: 0 },
      width: 390,
      height: null,
      minWidth: 0,
      maxWidth: 390,
      maxHeight: 680,
      isNarrow: true,
    });

    for (const presentation of ['dialog', 'drawer', 'popover'] as const) {
      const box = resolveOverlay({ key: presentation, presentation }, { work: narrowViewport, viewport: narrowViewport, units });
      expect(box.align).toEqual({ x: 'start', y: 'start' });
      expect(box.height).toBe(800);
      expect(box.maxHeight).toBe(800);
      expect(box.isNarrow).toBe(true);
    }
  });

  it('passes closesOnAction: true to NavMenuBody when opening NavComponent More menu on a narrow viewport', () => {
    const originalInnerWidth = window.innerWidth;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 390 });

    try {
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
      const openSpy = vi.spyOn(TestBed.inject(OverlayService), 'open');

      const fixture = TestBed.createComponent(NavComponent);
      fixture.componentRef.setInput('model', {
        ...navModel('toolbar'),
        more: [{ kind: 'NavClose', key: 'close', label: 'Close' }],
      });
      fixture.detectChanges();

      const moreTrigger = fixture.nativeElement.querySelector('ui-btn[aria-haspopup="dialog"]') as HTMLElement;
      expect(moreTrigger).toBeTruthy();
      moreTrigger.click();

      expect(openSpy).toHaveBeenCalledTimes(1);
      const openedModel = openSpy.mock.calls[0]?.[0] as OverlayDto;
      expect(openedModel.presentation).toBe('menu');
      const panelChild = openedModel.layout?.root?.children?.[0];
      expect(panelChild?.model).toMatchObject({
        kind: 'NavMenuBody',
        closesOnAction: true,
      });
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: originalInnerWidth });
    }
  });
});

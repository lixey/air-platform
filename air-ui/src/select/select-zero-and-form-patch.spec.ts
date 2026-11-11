import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formElement, formModel } from '../../tests/form-model';
import { AlertService } from '../app/alert/alert.service';
import { errorAlertInterceptor } from '../app/alert/error-alert.interceptor';
import { provideDynamicComponents } from '../core/dynamic-component/provide-dynamic-components';
import type { GridColumnDto, RowDto } from '../core/generated/contracts';
import { FormSelectComponent } from '../form/element/form-select.component';
import { FormComponent } from '../form/form.component';
import { FormService } from '../form/form.service';
import { EMPTY_SELECT_LABEL, emptySelectRowOf, selectSourceOf } from '../grid/column/grid-column-select-source';
import { referenceFilterOf } from '../grid/filter/grid-filter-set';
import { OverlayService } from '../overlay/overlay.service';
import { UI_COMPONENTS } from '../ui-components';
import { optionOfPickedValue, optionOfRow, pickedValueOf } from './select-option';
import { SelectComponent } from './select.component';
import { SelectService } from './select.service';

describe('Select zero-valued options vs null empty sentinel', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('preserves id: 0 distinct from id: null across optionOfRow, pickedValueOf, selectSourceOf and referenceFilterOf', () => {
    const emptyRow = emptySelectRowOf(['title'], []);
    expect(emptyRow).toEqual({ id: null, cells: { name: EMPTY_SELECT_LABEL, title: EMPTY_SELECT_LABEL } });

    const emptyOpt = optionOfRow(emptyRow, ['title']);
    expect(emptyOpt.id).toBeNull();
    expect(emptyOpt.label).toBe(EMPTY_SELECT_LABEL);

    const zeroDto: RowDto = { id: 0, cells: { title: 'Draft' } };
    const zeroOpt = optionOfRow(zeroDto, ['title']);
    expect(zeroOpt.id).toBe(0);
    expect(zeroOpt.label).toBe('Draft');

    expect(optionOfPickedValue(pickedValueOf(emptyOpt))).toEqual({ id: null, label: EMPTY_SELECT_LABEL });
    expect(optionOfPickedValue(pickedValueOf(zeroOpt))).toEqual({ id: 0, label: 'Draft' });

    const selectColumn: GridColumnDto = {
      path: 'priority',
      label: 'Priority',
      type: 'text',
      isSortable: true,
      isFilterable: true,
      isResizable: true,
      isSticky: false,
      isStickyRight: false,
      cell: { kind: 'GridCellSelect' },
      options: [
        { id: 0, name: 'Draft' },
        { id: 1, name: 'Normal' },
      ],
      isHidden: false,
      isDisabled: false,
      isRequired: false,
    };

    const source = selectSourceOf(selectColumn);
    expect(source?.options).toEqual([
      { id: null, label: EMPTY_SELECT_LABEL },
      { id: 0, label: 'Draft' },
      { id: 1, label: 'Normal' },
    ]);

    const filter = referenceFilterOf('priority', [emptyOpt, zeroOpt]);
    expect(filter).toEqual({
      kind: 'FilterRule',
      field: 'priority',
      operator: 'in',
      value: [null, 0],
      picked: [
        { id: null, label: EMPTY_SELECT_LABEL },
        { id: 0, label: 'Draft' },
      ],
    });
  });

  it('keeps id: 0 in single SelectComponent while clearing on id: null, and keeps both in multiple mode', () => {
    const mockRef = () => ({
      id: 'overlay-1',
      isClosed: false,
      ask: vi.fn(),
      close: vi.fn(() => Promise.resolve()),
      closed: { subscribe: () => ({ unsubscribe: () => undefined }) },
    });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: OverlayService,
          useValue: {
            menu: () => ({ isOpen: () => false, id: () => null, ref: null, open: vi.fn(mockRef), close: vi.fn() }),
            open: vi.fn(mockRef),
          },
        },
      ],
    });

    const singleFixture = TestBed.createComponent(SelectComponent);
    singleFixture.componentRef.setInput('model', {
      label: 'Priority',
      options: [
        { id: null, label: EMPTY_SELECT_LABEL },
        { id: 0, label: 'Draft' },
      ],
    });
    singleFixture.detectChanges();
    const singleService = singleFixture.debugElement.injector.get(SelectService);
    (singleFixture.nativeElement.querySelector('[role="combobox"]') as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    );
    singleService.pick({ id: 0, label: 'Draft' });
    expect(singleFixture.componentInstance.value()).toBe(0);
    expect(singleFixture.componentInstance.picked()).toEqual([{ id: 0, label: 'Draft' }]);

    (singleFixture.nativeElement.querySelector('[role="combobox"]') as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    );
    singleService.pick({ id: null, label: EMPTY_SELECT_LABEL });
    expect(singleFixture.componentInstance.value()).toBeNull();
    expect(singleFixture.componentInstance.picked()).toEqual([]);

    const multiFixture = TestBed.createComponent(SelectComponent);
    multiFixture.componentRef.setInput('multiple', true);
    multiFixture.componentRef.setInput('model', { label: 'Priority', options: [] });
    multiFixture.detectChanges();
    const multiService = multiFixture.debugElement.injector.get(SelectService);
    (multiFixture.nativeElement.querySelector('[role="combobox"]') as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    );
    multiService.pick({ id: null, label: EMPTY_SELECT_LABEL });
    multiService.pick({ id: 0, label: 'Draft' });
    multiService.apply();
    expect(multiFixture.componentInstance.value()).toEqual([null, 0]);
    expect(multiFixture.componentInstance.picked()).toHaveLength(2);
  });
});

describe('FormService array message retention, disabled PatchOnSelect, and ProblemDetails alert', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('retains server validation messages on array/multiselect fields when editing an adjacent field, clearing only when the array changes', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), FormService],
    });
    const http = TestBed.inject(HttpTestingController);
    const form = TestBed.inject(FormService);
    form.setModel(
      formModel([
        formElement('title'),
        formElement('tagIds', {
          valueKind: 'list',
          model: {
            kind: 'FormSelect',
            isMultiple: true,
            referenceLabelPaths: ['name'],
            options: [
              { id: 1, name: 'A' },
              { id: 2, name: 'B' },
            ],
          },
        }),
      ]),
    );
    form.setData({ id: 5, ver: 1, value: { title: 'Initial', tagIds: [1, 2] } });

    form.setValueAt('title', 'Changed');
    const savePromise = form.save();
    http.expectOne('/api/sample-form/save').flush(
      {
        outcome: 'refused',
        title: 'Invalid form',
        messages: [{ type: 'error', path: 'tagIds', detail: 'Tag 2 is archived.' }],
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await savePromise;

    expect(form.messages()).toEqual([{ type: 'error', path: 'tagIds', detail: 'Tag 2 is archived.' }]);

    // Typing in adjacent scalar field `title` creates a new frozen form.value() snapshot with a cloned `tagIds` array,
    // which must NOT wipe the server error on `tagIds`.
    form.setValueAt('title', 'Changed Again');
    expect(form.messages()).toEqual([{ type: 'error', path: 'tagIds', detail: 'Tag 2 is archived.' }]);

    // Changing `tagIds` itself clears its stale server error.
    form.setValueAt('tagIds', [1]);
    expect(form.messages()).toEqual([]);
    http.verify();
  });

  it('patches disabled target fields on client reference pick from a RowDto while respecting onlyWhenEmpty', () => {
    const mockRef = () => ({
      id: 'overlay-2',
      isClosed: false,
      ask: vi.fn(),
      close: vi.fn(() => Promise.resolve()),
      closed: { subscribe: () => ({ unsubscribe: () => undefined }) },
    });
    TestBed.configureTestingModule({
      imports: [FormComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideDynamicComponents(UI_COMPONENTS),
        {
          provide: OverlayService,
          useValue: {
            menu: () => ({ isOpen: () => false, id: () => null, ref: null, open: vi.fn(mockRef), close: vi.fn() }),
            open: vi.fn(mockRef),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(FormComponent);
    fixture.componentRef.setInput(
      'model',
      formModel([
        formElement('tagId', {
          valueKind: 'reference',
          referencePath: 'tag',
          selectPatches: [
            { targetField: 'amount', sourceColumn: 'defaultPrice', onlyWhenEmpty: false },
            { targetField: 'memo', sourceColumn: 'name', onlyWhenEmpty: true },
          ],
          model: { kind: 'FormSelect', referenceService: 'tag-grid', referenceLabelPaths: ['name'] },
        }),
        formElement('amount', { type: 'number', isDisabled: true, model: { kind: 'FormNumber' } }),
        formElement('memo', { isDisabled: true }),
      ]),
    );
    fixture.componentRef.setInput('data', { id: null, ver: null, value: { tagId: null, tag: null, amount: 0, memo: '' } });
    fixture.detectChanges();

    const form = fixture.debugElement.injector.get(FormService);
    // Normal user writes to disabled fields are ignored
    form.setValueAt('amount', 999);
    expect(form.valueOf('amount')).toBe(0);

    // Picking a RowDto in FormSelectComponent patches disabled `amount` and empty `memo` via patchValueAt
    const selectDebug = fixture.debugElement.children[0];
    expect(selectDebug).toBeDefined();
    const formSelectEl = fixture.debugElement.query(d => d.componentInstance instanceof FormSelectComponent);
    const selectCtrl = formSelectEl.query(d => d.componentInstance instanceof SelectComponent);
    const selectSvc = selectCtrl.injector.get(SelectService);
    (selectCtrl.nativeElement.querySelector('[role="combobox"]') as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    );

    const pickedRow1: RowDto = { id: 10, cells: { name: 'Starter', defaultPrice: 42.75 } };
    selectSvc.pickRow(pickedRow1);
    fixture.detectChanges();

    expect(form.valueOf('tagId')).toBe(10);
    expect(form.valueOf('amount')).toBe(42.75);
    expect(form.valueOf('memo')).toBe('Starter');

    // Picking a second RowDto updates `amount`, while `onlyWhenEmpty` keeps non-empty `memo`
    (selectCtrl.nativeElement.querySelector('[role="combobox"]') as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    );
    const pickedRow2: RowDto = { id: 11, cells: { name: 'Enterprise', defaultPrice: 105.5 } };
    selectSvc.pickRow(pickedRow2);
    fixture.detectChanges();

    expect(form.valueOf('amount')).toBe(105.5);
    expect(form.valueOf('memo')).toBe('Starter');
  });

  it('shows problem.detail ahead of generic problem.title in errorAlertInterceptor', async () => {
    const errorSpy = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorAlertInterceptor])),
        provideHttpClientTesting(),
        { provide: AlertService, useValue: { error: errorSpy, open: vi.fn(), warnings: vi.fn() } },
      ],
    });

    const httpClient = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);

    const p1 = firstValueFrom(httpClient.get('/api/broken'));
    controller
      .expectOne('/api/broken')
      .flush({ title: 'Bad Request', detail: 'Warehouse 4 is closed for inventory.' }, { status: 400, statusText: 'Bad Request' });
    await expect(p1).rejects.toBeDefined();
    expect(errorSpy).toHaveBeenLastCalledWith('Error 400', 'Warehouse 4 is closed for inventory.');

    const p2 = firstValueFrom(httpClient.get('/api/title-only'));
    controller.expectOne('/api/title-only').flush({ title: 'Gateway Timeout' }, { status: 504, statusText: 'Gateway Timeout' });
    await expect(p2).rejects.toBeDefined();
    expect(errorSpy).toHaveBeenLastCalledWith('Error 504', 'Gateway Timeout');

    controller.verify();
  });
});

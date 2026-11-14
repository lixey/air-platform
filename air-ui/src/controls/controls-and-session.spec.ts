import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { formElement, formModel } from '../../tests/form-model';
import { AlertService } from '../app/alert/alert.service';
import { errorAlertInterceptor } from '../app/alert/error-alert.interceptor';
import { AppService } from '../app/app.service';
import { AuthService } from '../app/auth/auth.service';
import { credentialsInterceptor } from '../app/auth/credentials.interceptor';
import type { GridColumnDto } from '../core/generated/contracts';
import { FormCheckboxSetComponent } from '../form/element/form-checkbox-set.component';
import { FormInputComponent } from '../form/element/form-input.component';
import { FormSelectComponent } from '../form/element/form-select.component';
import { FormService } from '../form/form.service';
import { GridCellEditorComponent } from '../grid/cell/grid-cell-editor.component';
import { GridCellEdits } from '../grid/grid-cell-edits';
import type { GridRow } from '../grid/grid-row';
import { GridService } from '../grid/grid.service';
import { VirtualScrollObserver } from '../grid/table/virtual-scroll-observer';
import { LayoutSwitcherComponent, PAGE_RELOAD } from '../layout/layout-switcher.component';
import { OverlayService } from '../overlay/overlay.service';
import type { ScreenAddress } from '../screen/screen-address';
import { screenLoader } from '../screen/screen-loader';
import { SelectComponent } from '../select/select.component';
import { parseAmount, plainAmountText } from './amount-text';

describe('Controls, virtual scroll sticky footer, accessible labels, SavesOnChange, and session resilience', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('formats small decimals (< 1e-6) without exponential notation in plainAmountText and round-trips via parseAmount', () => {
    expect(plainAmountText(5e-7, 'en-US')).toBe('0.0000005');
    expect(plainAmountText(-1.25e-7, 'uk-UA')).toBe('-0,000000125');
    expect(plainAmountText(1e-8, 'en-US')).toBe('0.00000001');
    expect(parseAmount(plainAmountText(5e-7, 'en-US'), 'en-US', 8)).toBe(0.0000005);
    expect(parseAmount(plainAmountText(-1.25e-7, 'uk-UA'), 'uk-UA', 9)).toBe(-0.000000125);
  });

  it('subtracts tfoot height in VirtualScrollObserver only when tfoot is position: sticky', () => {
    const observer = new VirtualScrollObserver(36, () => undefined);
    const container = document.createElement('div');
    Object.defineProperty(container, 'clientHeight', { value: 500 });
    Object.defineProperty(container, 'clientWidth', { value: 800 });

    const thead = document.createElement('thead');
    thead.getBoundingClientRect = () => ({ height: 40 }) as DOMRect;
    const tfoot = document.createElement('tfoot');
    tfoot.style.position = 'sticky';
    tfoot.getBoundingClientRect = () => ({ height: 38 }) as DOMRect;
    container.append(thead, tfoot);
    document.body.appendChild(container);

    try {
      observer.measureViewport(container, () => undefined);
      expect(observer.headerHeightPx).toBe(40);
      expect(observer.footerHeightPx).toBe(38);
      expect(observer.bodyViewportHeight()).toBe(422);

      // Non-sticky mobile tfoot stands in document flow -> 0 sticky inset
      const nonStickyFoot = document.createElement('tfoot');
      nonStickyFoot.style.position = 'static';
      nonStickyFoot.getBoundingClientRect = () => ({ height: 38 }) as DOMRect;
      tfoot.replaceWith(nonStickyFoot);
      observer.measureViewport(container, () => undefined);
      expect(observer.footerHeightPx).toBe(0);
      expect(observer.bodyViewportHeight()).toBe(460);
    } finally {
      container.remove();
    }
  });

  it('wires inputId for <label for> on SelectComponent while preserving host id & aria-label for FilterTree', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), FormService] });

    const hostIdFixture = TestBed.createComponent(SelectComponent);
    const filterRow = document.createElement('div');
    filterRow.appendChild(hostIdFixture.nativeElement);
    (hostIdFixture.nativeElement as HTMLElement).id = 'op-rule-1';
    hostIdFixture.componentRef.setInput('model', { label: 'Operator', options: [{ id: 'eq', label: 'Equals' }] });
    hostIdFixture.detectChanges();

    const opCombo = filterRow.querySelector('#op-rule-1 [role="combobox"]') as HTMLButtonElement;
    expect(opCombo).toBeTruthy();
    expect(opCombo.getAttribute('aria-label')).toBe('Operator');

    const statusEl = formElement('status', {
      label: 'Order Status',
      model: { kind: 'FormSelect', referenceLabelPaths: [], options: [{ id: 1, name: 'New' }] },
    });
    const flagsEl = formElement('flags', {
      label: 'Channels',
      valueKind: 'list',
      model: { kind: 'FormCheckboxSet', options: [{ id: 1, name: 'Web' }] },
    });

    const form = TestBed.inject(FormService);
    form.setModel(formModel([statusEl, flagsEl]));
    form.setData({ id: 1, ver: 1, value: { status: 1, flags: [1] } });

    const formSelectFixture = TestBed.createComponent(FormSelectComponent);
    formSelectFixture.componentRef.setInput('element', statusEl);
    formSelectFixture.componentRef.setInput('model', statusEl.model);
    formSelectFixture.detectChanges();

    const label = formSelectFixture.nativeElement.querySelector('label') as HTMLLabelElement;
    const formTrigger = formSelectFixture.nativeElement.querySelector('button[role="combobox"]') as HTMLButtonElement;
    expect(label.htmlFor).toBeTruthy();
    expect(formTrigger.id).toBe(label.htmlFor);

    const checkboxSetFixture = TestBed.createComponent(FormCheckboxSetComponent);
    checkboxSetFixture.componentRef.setInput('element', flagsEl);
    checkboxSetFixture.componentRef.setInput('model', flagsEl.model);
    checkboxSetFixture.detectChanges();
    expect(checkboxSetFixture.nativeElement.querySelector('[role="group"]')).toBeTruthy();
    expect(checkboxSetFixture.nativeElement.querySelector('label[for]')).toBeNull();
  });

  it('supports inline GridCellPhone editing and SavesOnChange on external focusout for text inputs', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: OverlayService, useValue: { stack: signal([]) } }],
    });

    const phoneCol: GridColumnDto = {
      path: 'phone',
      label: 'Phone',
      type: 'text',
      isSortable: true,
      isFilterable: true,
      isResizable: true,
      isSticky: false,
      isStickyRight: false,
      cell: { kind: 'GridCellPhone' },
      operators: [],
      isEditable: true,
      isHidden: false,
      isDisabled: false,
      isRequired: false,
    };
    const row: GridRow = { id: 7, ver: 1, cells: { phone: '+380501112233' } };
    const rows = signal<readonly GridRow[]>([row]);

    const edits = TestBed.runInInjectionContext(
      () =>
        new GridCellEdits({
          resetBy: () => 0,
          model: () => null,
          service: () => 'contact-grid',
          params: () => ({}),
          rows,
          columns: () => [phoneCol],
          isPicking: () => false,
          capture: () => () => true,
          beginMutation: () => undefined,
          finishMutation: () => undefined,
          adoptRow: () => undefined,
          moveActive: () => undefined,
          reload: () => undefined,
        }),
    );

    expect(edits.start(row, phoneCol)).toBe(true);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        FormService,
        { provide: GridService, useValue: { edits } },
        { provide: OverlayService, useValue: { stack: signal([]) } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);

    const editorFixture = TestBed.createComponent(GridCellEditorComponent);
    editorFixture.componentRef.setInput('cell', { row, column: phoneCol, value: '+380501112233', index: 0 });
    editorFixture.detectChanges();
    expect(editorFixture.nativeElement.querySelector('ui-phone-box input')).toBeTruthy();

    const saveSpy = vi.fn(() => Promise.resolve(null));
    const codeEl = formElement('code', { label: 'Code', savesOnChange: true });
    const formStub = TestBed.inject(FormService);
    formStub.setModel(formModel([codeEl]));
    formStub.setData({ id: 5, ver: 1, value: { code: 'A1' } });
    vi.spyOn(formStub, 'save').mockImplementation(saveSpy);

    const inputFixture = TestBed.createComponent(FormInputComponent);
    inputFixture.componentRef.setInput('element', codeEl);
    inputFixture.detectChanges();

    const textBoxEl = inputFixture.nativeElement.querySelector('ui-text-box') as HTMLElement;
    const textEl = inputFixture.nativeElement.querySelector('input') as HTMLInputElement;
    textEl.value = 'A2';
    textEl.dispatchEvent(new Event('input'));
    inputFixture.detectChanges();
    expect(saveSpy).not.toHaveBeenCalled();

    // Focus staying inside ui-text-box does not save
    textEl.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: textBoxEl }));
    expect(saveSpy).not.toHaveBeenCalled();

    // Focus leaving ui-text-box triggers save()
    textEl.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
    await new Promise(resolve => queueMicrotask(() => resolve(undefined)));
    expect(saveSpy).toHaveBeenCalledTimes(1);
    http.verify();
  });

  it('silences 401/403 cookie expiry while surfacing 5xx outages, recovers AppService once on navigation, and preserves screenLoader patch', async () => {
    const reloadSpy = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(withInterceptors([credentialsInterceptor, errorAlertInterceptor])),
        provideHttpClientTesting(),
        { provide: PAGE_RELOAD, useValue: reloadSpy },
      ],
    });

    const http = TestBed.inject(HttpTestingController);
    const alertSpy = vi.spyOn(TestBed.inject(AlertService), 'error');
    const auth = TestBed.inject(AuthService);

    // 1a. Expired access cookie (401) + refused refresh cookie (403) -> silent null, zero error toasts!
    const restoreExpired = firstValueFrom(auth.restore());
    http.expectOne('/api/auth/get-profile').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/refresh').flush({ title: 'Session expired' }, { status: 403, statusText: 'Forbidden' });
    expect(await restoreExpired).toBeNull();
    expect(alertSpy).not.toHaveBeenCalled();

    // 1b. Real 500 server outage on restore -> surfaces single error alert!
    const restore500 = firstValueFrom(auth.restore());
    http.expectOne('/api/auth/get-profile').flush({ title: 'DB unreachable' }, { status: 500, statusText: 'Server Error' });
    expect(await restore500).toBeNull();
    expect(alertSpy).toHaveBeenCalledTimes(1);
    alertSpy.mockClear();

    // 2. AppService transient metadata/get-data failure fires ONCE on startup and recovers on navigation!
    const app = TestBed.inject(AppService);
    const router = TestBed.inject(Router);
    const initPromise = app.init();

    http.expectOne('/api/auth/get-profile').flush({
      id: 1,
      name: 'Admin',
      login: 'admin',
      mustChangePassword: false,
      permissions: ['*'],
      claims: {},
    });
    http.expectOne('/api/metadata/get-data').flush({ title: 'Temporary outage' }, { status: 503, statusText: 'Unavailable' });
    await initPromise;
    TestBed.tick();
    http.expectNone('/api/metadata/get-data');
    expect(alertSpy).toHaveBeenCalledTimes(1);
    expect(app.routes()).toEqual([]);

    await router.navigateByUrl('/ecom/orders');
    TestBed.tick();
    http.expectOne('/api/metadata/get-data').flush({
      routes: [{ path: 'ecom/orders', name: 'Orders', screen: 'ecom/order-grid', isSeparator: false, items: [] }],
      menu: [],
      texts: { ScreenLoadFailed: 'Failed to load screen' },
    });
    expect(app.routes()).toHaveLength(1);

    // 3. LayoutSwitcherComponent posts via Api.call without hardcoded service names
    const switcherFixture = TestBed.createComponent(LayoutSwitcherComponent);
    switcherFixture.componentRef.setInput('model', {
      label: 'Language',
      service: 'platform-language',
      method: 'switch',
      current: { id: 1, name: 'EN' },
      options: [
        { id: 1, name: 'EN' },
        { id: 2, name: 'UA' },
      ],
    });
    switcherFixture.detectChanges();
    (switcherFixture.componentInstance as unknown as { switchTo: (o: { id: number; label: string }) => void }).switchTo({
      id: 2,
      label: 'UA',
    });
    const switchReq = http.expectOne('/api/platform-language/switch');
    expect(switchReq.request.body).toEqual({ id: 2 });
    switchReq.flush({ id: 1, name: 'Admin', login: 'admin', mustChangePassword: false, permissions: ['*'], claims: {} });
    expect(reloadSpy).toHaveBeenCalledTimes(1);

    // 4. screenLoader exposes errorTitle and preserves reload(patch) when baseAddress emits a cloned equal object
    const baseAddr = signal<ScreenAddress>({ service: 'ecom/order-form', params: { mode: 'edit' } });
    const loader = TestBed.runInInjectionContext(() => screenLoader(() => baseAddr()));
    TestBed.tick();

    http.expectOne('/api/ecom/order-form/get-data').flush({ title: 'Order locked' }, { status: 423, statusText: 'Locked' });
    await new Promise(resolve => setTimeout(resolve, 0));
    TestBed.tick();
    expect(loader.errorTitle()).toBe('Order locked');

    loader.reload({ id: 99 });
    TestBed.tick();
    const patchedLoad = http.expectOne('/api/ecom/order-form/get-data');
    expect(patchedLoad.request.body).toEqual({ mode: 'edit', id: 99 });
    patchedLoad.flush({ layout: { key: 'root', root: { key: 'r', direction: 'column', children: [] } }, data: { id: 99 } });

    // Emitting a fresh object with identical { service, params } must NOT wipe patch { id: 99 }!
    baseAddr.set({ service: 'ecom/order-form', params: { mode: 'edit' } });
    TestBed.tick();
    expect(loader.address()).toEqual({ service: 'ecom/order-form', params: { mode: 'edit', id: 99 } });

    http.verify();
  });
});

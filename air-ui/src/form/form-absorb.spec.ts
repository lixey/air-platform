import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { formElement, formModel } from '../../tests/form-model';
import { errorAlertInterceptor } from '../app/alert/error-alert.interceptor';
import { credentialsInterceptor } from '../app/auth/credentials.interceptor';
import { Lifetime } from '../core/lifetime';
import type { OverlayRef } from '../overlay/overlay-ref';
import type { ScreenMutationEvent } from '../screen/screen-mutations';
import { FormChildScope } from './form-child-scope';
import { FormService } from './form.service';

describe('FormService.absorb and FormChildScope master-detail synchronization', () => {
  let http: HttpTestingController;
  let form: FormService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([credentialsInterceptor, errorAlertInterceptor])),
        provideHttpClientTesting(),
        FormService,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    form = TestBed.inject(FormService);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('absorbs server-recalculated totals and version bump while keeping dirty header edits intact without 409 conflict', async () => {
    form.setModel(
      formModel(
        [
          formElement('note'),
          formElement('customerId', {
            type: 'number',
            referencePath: 'customer',
            model: { kind: 'FormSelect', referenceLabelPaths: ['name'] },
          }),
          formElement('totalAmount', { type: 'number', isServerOwned: true }),
        ],
        { service: 'ecom/customer-order-form' },
      ),
    );
    form.setData({
      id: 42,
      ver: 1,
      value: { note: 'Initial note', customerId: 10, customer: { id: 10, name: 'Acme' }, totalAmount: 100 },
      unavailable: { 'status.confirm': 0 },
    });

    expect(form.isDirty()).toBe(false);
    form.setValueAt('note', 'Deliver before 17:00');
    expect(form.isDirty()).toBe(true);

    const revBefore = form.dataRevision();
    form.notifyChildMutated({
      service: 'ecom/customer-order-line-form',
      operation: 'save',
      recordId: 7,
      affected: [
        {
          service: 'ecom/customer-order-form',
          id: 42,
          prevVer: 1,
          ver: 2,
          value: { note: 'Initial note', customerId: 10, customer: { id: 10, name: 'Acme' }, totalAmount: 250 },
          unavailable: {},
        },
      ],
    });

    expect(form.ver()).toBe(2);
    expect(form.valueOf('totalAmount')).toBe(250);
    expect(form.valueOf('note')).toBe('Deliver before 17:00');
    expect(form.unavailable()).toEqual({});
    expect(form.isDirty()).toBe(true);
    expect(form.isStale()).toBe(false);
    expect(form.dataRevision()).toBe(revBefore + 1);

    const savePromise = form.save();
    const saveReq = http.expectOne('/api/ecom/customer-order-form/save');
    expect(saveReq.request.body).toMatchObject({
      id: 42,
      ver: 2,
      value: { note: 'Deliver before 17:00', customerId: 10, totalAmount: 250 },
    });
    saveReq.flush({
      outcome: 'committed',
      id: 42,
      ver: 3,
      value: { note: 'Deliver before 17:00', customerId: 10, customer: { id: 10, name: 'Acme' }, totalAmount: 250 },
      unavailable: {},
    });

    const saved = await savePromise;
    expect(saved?.ver).toBe(3);
    expect(form.ver()).toBe(3);
    expect(form.isDirty()).toBe(false);
    expect(form.isStale()).toBe(false);
  });

  it('falls back to marking a dirty parent form stale when prevVer does not match the held version', () => {
    form.setModel(
      formModel([formElement('note'), formElement('totalAmount', { type: 'number', isServerOwned: true })], {
        service: 'ecom/customer-order-form',
      }),
    );
    form.setData({
      id: 42,
      ver: 1,
      value: { note: 'Initial', totalAmount: 100 },
    });

    form.setValueAt('note', 'Local edit');
    expect(form.isDirty()).toBe(true);

    form.notifyChildMutated({
      service: 'ecom/customer-order-line-form',
      operation: 'save',
      recordId: 8,
      affected: [
        {
          service: 'ecom/customer-order-form',
          id: 42,
          prevVer: 2,
          ver: 3,
          value: { note: 'Concurrent server edit', totalAmount: 300 },
        },
      ],
    });

    expect(form.ver()).toBe(1);
    expect(form.valueOf('note')).toBe('Local edit');
    expect(form.isStale()).toBe(true);
  });

  it('ignores untouched child overlay closure while forwarding committed child mutations once with affected records', () => {
    const parentLifetime = new Lifetime();
    const childScope = new FormChildScope(parentLifetime);
    const received: ScreenMutationEvent[] = [];
    childScope.onChange(event => received.push(event));

    // Simulate tracking an untouched child overlay and closing it without saving (closeCause is undefined)
    const untouchedClosed = new Subject<unknown>();
    const untouchedRef = {
      isClosed: false,
      closeCause: undefined,
      closed: untouchedClosed.asObservable(),
    } as unknown as OverlayRef;
    childScope.mutations.track(untouchedRef, 'ecom/customer-order-line-form', () => true);
    untouchedClosed.next(undefined);
    untouchedClosed.complete();

    expect(received).toHaveLength(0);

    // Simulate a child overlay that publishes a save with affected records and closes under that operation cause
    const op = childScope.mutations.begin();
    const savedClosed = new Subject<unknown>();
    const savedRef = {
      isClosed: false,
      closeCause: op.id,
      closed: savedClosed.asObservable(),
    } as unknown as OverlayRef;
    childScope.mutations.track(savedRef, 'ecom/customer-order-line-form', () => true);
    childScope.mutations.publish(op, {
      service: 'ecom/customer-order-line-form',
      operation: 'save',
      recordId: 15,
      affected: [{ service: 'ecom/customer-order-form', id: 42, prevVer: 1, ver: 2, value: { totalAmount: 500 } }],
    });
    savedClosed.next({ id: 15 });
    savedClosed.complete();
    childScope.mutations.finish(op);

    expect(received).toHaveLength(1);
    expect(received[0].affected?.[0]).toMatchObject({ service: 'ecom/customer-order-form', id: 42, prevVer: 1, ver: 2 });

    parentLifetime.end();
  });
});

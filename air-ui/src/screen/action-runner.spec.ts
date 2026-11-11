import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formElement } from '../../tests/form-model';
import { errorAlertInterceptor } from '../app/alert/error-alert.interceptor';
import { credentialsInterceptor, PLATFORM_IDEMPOTENCY_KEY_HEADER } from '../app/auth/credentials.interceptor';
import type { FormAskModel, MessageDto } from '../core/generated/contracts';
import type { AskService } from '../overlay/ask.service';
import type { ConfirmationService } from '../overlay/confirmation.service';
import { ActionRunner } from './action-runner';

describe('ActionRunner', () => {
  let http: HttpTestingController;
  let runner: ActionRunner;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([credentialsInterceptor, errorAlertInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    runner = new ActionRunner(TestBed.inject(HttpClient));
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('attaches Idempotency-Key, reuses it on retry after an unknown failure, and rotates it after commit, refusal, or payload change', async () => {
    const url = '/api/ecom/customer-order/confirm-order';
    const body = { id: 10, ver: 1 };

    // 1. First attempt fails with 503 Service Unavailable ('unknown' outcome)
    const firstAttempt = runner.post(url, body).catch(err => err);
    const req1 = http.expectOne(url);
    const key1 = req1.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(key1).toBeTruthy();
    req1.flush('Gateway timeout', { status: 503, statusText: 'Service Unavailable' });
    await firstAttempt;

    // 2. Retry with identical URL and body reuses key1
    const secondAttempt = runner.post<{ outcome: string }>(url, body);
    const req2 = http.expectOne(url);
    const key2 = req2.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(key2).toBe(key1);
    req2.flush({ outcome: 'committed' });
    expect(await secondAttempt).toEqual({ outcome: 'committed' });

    // 3. Next invocation after commit generates a fresh key
    const thirdAttempt = runner.post<{ outcome: string }>(url, body);
    const req3 = http.expectOne(url);
    const key3 = req3.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(key3).toBeTruthy();
    expect(key3).not.toBe(key1);
    req3.flush({ outcome: 'committed' });
    await thirdAttempt;

    // 4. Deterministic 422 refusal rotates key on next attempt
    const fourthAttempt = runner.post(url, body).catch(err => err);
    const req4 = http.expectOne(url);
    const key4 = req4.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    req4.flush({ title: 'Refused', status: 422 }, { status: 422, statusText: 'Unprocessable Entity' });
    await fourthAttempt;

    const fifthAttempt = runner.post<{ outcome: string }>(url, body);
    const req5 = http.expectOne(url);
    const key5 = req5.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(key5).not.toBe(key4);
    req5.flush({ outcome: 'committed' });
    await fifthAttempt;

    // 5. Unknown failure followed by a modified payload generates a fresh key (preventing 422 payload mismatch)
    const sixthAttempt = runner.post(url, { id: 10, ver: 1, qty: 1 }).catch(err => err);
    const req6 = http.expectOne(url);
    const key6 = req6.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    req6.flush('Network error', { status: 503, statusText: 'Service Unavailable' });
    await sixthAttempt;

    const seventhAttempt = runner.post<{ outcome: string }>(url, { id: 10, ver: 1, qty: 2 });
    const req7 = http.expectOne(url);
    const key7 = req7.request.headers.get(PLATFORM_IDEMPOTENCY_KEY_HEADER);
    expect(key7).not.toBe(key6);
    req7.flush({ outcome: 'committed' });
    await seventhAttempt;
  });

  it('fetches get-action-input defaults when ask.hasDefaults is true and aborts opening dialog on error', async () => {
    const askModel: FormAskModel = {
      kind: 'FormAsk',
      hasDefaults: true,
      elements: [formElement('reason')],
    };

    const askSpy = vi.fn(
      async (
        _model: FormAskModel,
        submit: (value: Readonly<Record<string, unknown>>) => Promise<readonly MessageDto[]>,
        _injector?: Injector,
        given?: Readonly<Record<string, unknown>>,
      ) => {
        await submit({ ...given, extra: 'ok' });
        return true;
      },
    );
    const asks = { ask: askSpy } as unknown as AskService;
    const confirmation = { confirm: vi.fn() } as unknown as ConfirmationService;
    const submitted: Record<string, unknown>[] = [];
    const reportError = vi.fn();

    const promptPromise = runner.prompt({
      service: 'ecom/customer-order-form',
      method: 'cancel-order',
      label: 'Cancel Order',
      ask: askModel,
      asks,
      confirmation,
      injector: TestBed.inject(Injector),
      isCurrent: () => true,
      buildDefaultParams: async () => ({ id: 42, ver: 2 }),
      reportDefaultsError: reportError,
      submit: async val => {
        submitted.push({ ...val });
        return [];
      },
    });

    await Promise.resolve();
    const defaultsReq = http.expectOne('/api/ecom/customer-order-form/get-action-input');
    expect(defaultsReq.request.body).toEqual({ id: 42, ver: 2, method: 'cancel-order' });
    defaultsReq.flush({ value: { reason: 'Customer requested' } });

    await promptPromise;
    expect(askSpy).toHaveBeenCalledTimes(1);
    expect(submitted).toEqual([{ reason: 'Customer requested', extra: 'ok' }]);
    expect(reportError).not.toHaveBeenCalled();

    // When get-action-input fails, AskService.ask must not open and reportDefaultsError must be invoked
    askSpy.mockClear();
    const failedPrompt = runner.prompt({
      service: 'ecom/customer-order-form',
      method: 'cancel-order',
      label: 'Cancel Order',
      ask: askModel,
      asks,
      confirmation,
      injector: TestBed.inject(Injector),
      isCurrent: () => true,
      buildDefaultParams: async () => ({ id: 42, ver: 2 }),
      reportDefaultsError: reportError,
      submit: async () => [],
    });

    await Promise.resolve();
    const failedReq = http.expectOne('/api/ecom/customer-order-form/get-action-input');
    failedReq.flush({ title: 'Forbidden', status: 403 }, { status: 403, statusText: 'Forbidden' });
    await failedPrompt;

    expect(askSpy).not.toHaveBeenCalled();
    expect(reportError).toHaveBeenCalledTimes(1);
  });
});

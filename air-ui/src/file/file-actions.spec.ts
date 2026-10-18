import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { createEnvironmentInjector, EnvironmentInjector, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, type MockInstance, vi } from 'vitest';
import { AlertService } from '../app/alert/alert.service';
import { errorAlertInterceptor, SKIP_GLOBAL_ERROR } from '../app/alert/error-alert.interceptor';
import type { MutationOutcome } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import type { GridRow } from '../grid/grid-row';
import { type FileActionOpener, type FileActionRequest, isCommitted, postFileAction, reportFileRefusal } from './file-actions';

const service = 'sample-report-file-grid';

function provideHttp(): void {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(withInterceptors([errorAlertInterceptor])), provideHttpClientTesting()],
  });
}

describe('postFileAction', () => {
  let http: HttpTestingController;
  let error: MockInstance<AlertService['error']>;
  let setBusy: ReturnType<typeof vi.fn<(busy: boolean) => void>>;
  let requestOf: ReturnType<typeof vi.fn<FileActionOpener['requestOf']>>;
  let isCurrent: boolean;
  const rows: readonly GridRow[] = [{ id: 12, ver: 3 }];
  const request: FileActionRequest = { service, body: { parentService: 'sample-report-form', parentId: 7, selectedIds: [12] } };

  const openerOf = (injector: Injector = TestBed.inject(Injector)): FileActionOpener => ({
    injector,
    requestOf,
    capture: () => () => isCurrent,
  });

  beforeEach(() => {
    provideHttp();
    http = TestBed.inject(HttpTestingController);
    error = vi.spyOn(TestBed.inject(AlertService), 'error');
    setBusy = vi.fn<(busy: boolean) => void>();
    requestOf = vi.fn<FileActionOpener['requestOf']>(() => request);
    isCurrent = true;
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it("posts the method with the request's body and extra, the global alert silent, and answers the answer", async () => {
    const outcome = postFileAction(openerOf(), 'move', rows, { beforeId: 15 }, setBusy);
    const posted = http.expectOne(`/api/${service}/move`);
    posted.flush({ count: 1 });

    expect(requestOf).toHaveBeenCalledWith('move', rows);
    expect(posted.request.body).toEqual({ parentService: 'sample-report-form', parentId: 7, selectedIds: [12], beforeId: 15 });
    expect(posted.request.context.get(SKIP_GLOBAL_ERROR)).toBe(true);
    expect(await outcome).toEqual({ answer: { count: 1 } });
    expect(error).not.toHaveBeenCalled();
  });

  it('raises setBusy while the request is under way, then lowers it', async () => {
    const outcome = postFileAction(openerOf(), 'set-cover', rows, {}, setBusy);
    expect(setBusy.mock.calls).toEqual([[true]]);

    http.expectOne(`/api/${service}/set-cover`).flush({ count: 1 });
    await outcome;

    expect(setBusy.mock.calls).toEqual([[true], [false]]);
  });

  it('answers the archive of download-all under the name of its Content-Disposition, files.zip without one', async () => {
    const archive = new Blob([new Uint8Array([0x50, 0x4b, 3, 4])]);

    const named = postFileAction(openerOf(), 'download-all', null, {}, setBusy);
    const posted = http.expectOne(`/api/${service}/download-all`);
    posted.flush(archive, { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''Folder_7.zip" } });
    expect(requestOf).toHaveBeenCalledWith('download-all', null);
    expect(posted.request.body).toEqual(request.body);
    expect(posted.request.responseType).toBe('blob');
    expect(posted.request.context.get(SKIP_GLOBAL_ERROR)).toBe(true);
    expect(await named).toEqual({ answer: { blob: archive, name: 'Folder_7.zip' } });

    const unnamed = postFileAction(openerOf(), 'download-all', null, {}, setBusy);
    http.expectOne(`/api/${service}/download-all`).flush(archive);
    expect(await unnamed).toEqual({ answer: { blob: archive, name: 'files.zip' } });
  });

  it('reports a failure the opener stands for as a refused action is, once, answers failed and lowers setBusy', async () => {
    const outcome = postFileAction(openerOf(), 'delete', rows, {}, setBusy);
    http
      .expectOne(`/api/${service}/delete`)
      .flush({ title: 'Not deleted', detail: 'The file is in use.' }, { status: 400, statusText: 'Bad Request' });

    expect(await outcome).toBe('failed');
    expect(error).toHaveBeenCalledTimes(1);
    expect(error).toHaveBeenCalledWith('Not deleted', 'The file is in use.');
    expect(setBusy.mock.calls).toEqual([[true], [false]]);
  });

  it('reports a stale version as the failed request: a file action has no conflict title', async () => {
    const outcome = postFileAction(openerOf(), 'delete', rows, {}, setBusy);
    http.expectOne(`/api/${service}/delete`).flush({}, { status: 409, statusText: 'Conflict' });

    expect(await outcome).toBe('failed');
    expect(error).toHaveBeenCalledWith('The request failed.', undefined);
  });

  it('answers null for an answer the opener no longer stands for, and lowers setBusy', async () => {
    const outcome = postFileAction(openerOf(), 'set-cover', rows, {}, setBusy);
    isCurrent = false;
    http.expectOne(`/api/${service}/set-cover`).flush({ count: 1 });

    expect(await outcome).toBeNull();
    expect(setBusy.mock.calls).toEqual([[true], [false]]);
  });

  it('answers null for a failure the opener no longer stands for, and reports nothing', async () => {
    const outcome = postFileAction(openerOf(), 'delete', rows, {}, setBusy);
    isCurrent = false;
    http.expectOne(`/api/${service}/delete`).flush({ title: 'Not deleted' }, { status: 400, statusText: 'Bad Request' });

    expect(await outcome).toBeNull();
    expect(error).not.toHaveBeenCalled();
    expect(setBusy.mock.calls).toEqual([[true], [false]]);
  });

  it('posts nothing where the opener stands in no record: null, setBusy untouched, no currency taken', async () => {
    requestOf.mockReturnValue(null);
    const capture = vi.fn(() => () => true);

    const outcome = postFileAction({ ...openerOf(), capture }, 'delete', rows, {}, setBusy);

    expect(await outcome).toBeNull();
    http.expectNone(`/api/${service}/delete`);
    expect(setBusy).not.toHaveBeenCalled();
    expect(capture).not.toHaveBeenCalled();
  });

  it("does not lower setBusy once the opener's injector is destroyed", async () => {
    const injector = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));

    const outcome = postFileAction(openerOf(injector), 'set-cover', rows, {}, setBusy);
    injector.destroy();
    http.expectOne(`/api/${service}/set-cover`).flush({ count: 1 });

    expect(await outcome).toEqual({ answer: { count: 1 } });
    expect(setBusy.mock.calls).toEqual([[true]]);
  });
});

describe('isCommitted', () => {
  it('holds an answer that names no outcome, or committed, committed, and no answer at all', () => {
    expect(isCommitted({})).toBe(true);
    expect(isCommitted({ outcome: null })).toBe(true);
    expect(isCommitted({ outcome: 'committed' })).toBe(true);
    expect(isCommitted(null)).toBe(true);
    expect(isCommitted(undefined)).toBe(true);
  });

  it('holds every other outcome not committed', () => {
    const others: readonly MutationOutcome[] = ['refused', 'rolledBack', 'conflict', 'unknown'];

    expect(others.map(outcome => isCommitted({ outcome }))).toEqual([false, false, false, false]);
  });
});

describe('reportFileRefusal', () => {
  let alert: AlertService;
  let texts: TextsService;
  let error: MockInstance<AlertService['error']>;

  beforeEach(() => {
    alert = TestBed.inject(AlertService);
    texts = TestBed.inject(TextsService);
    error = vi.spyOn(alert, 'error');
  });
  afterEach(() => TestBed.resetTestingModule());

  it("reports a refusing answer under its title, with its messages' titles as the detail", () => {
    reportFileRefusal(alert, texts, {
      outcome: 'refused',
      title: 'Not saved',
      detail: 'One file.',
      messages: [
        { type: 'error', title: 'The name is taken.' },
        { type: 'error', detail: 'Choose another.' },
      ],
    });

    expect(error).toHaveBeenCalledTimes(1);
    expect(error).toHaveBeenCalledWith('Not saved', 'The name is taken. Choose another.');
  });

  it("reports a failed request under its problem's title and detail, the failed request where it names no title", () => {
    reportFileRefusal(
      alert,
      texts,
      new HttpErrorResponse({ status: 400, error: { title: 'Not deleted', detail: 'The file is in use.' } }),
      'Changed by someone else',
    );
    expect(error).toHaveBeenLastCalledWith('Not deleted', 'The file is in use.');

    reportFileRefusal(alert, texts, new Error('offline'));
    expect(error).toHaveBeenLastCalledWith('The request failed.', 'offline');
  });

  it('reports under the conflict title a stale version alone, and only where one is given and the server names no title', () => {
    const stale = new HttpErrorResponse({ status: 409, error: {} });

    reportFileRefusal(alert, texts, stale, 'Changed by someone else');
    expect(error).toHaveBeenLastCalledWith('Changed by someone else', undefined);
    reportFileRefusal(alert, texts, { outcome: 'conflict' }, 'Changed by someone else');
    expect(error).toHaveBeenLastCalledWith('Changed by someone else', undefined);

    reportFileRefusal(alert, texts, stale);
    expect(error).toHaveBeenLastCalledWith('The request failed.', undefined);
    reportFileRefusal(alert, texts, stale, null);
    expect(error).toHaveBeenLastCalledWith('The request failed.', undefined);
    reportFileRefusal(alert, texts, { outcome: 'conflict', title: 'Edited meanwhile' }, 'Changed by someone else');
    expect(error).toHaveBeenLastCalledWith('Edited meanwhile', undefined);
    reportFileRefusal(alert, texts, { outcome: 'refused' }, 'Changed by someone else');
    expect(error).toHaveBeenLastCalledWith('The request failed.', undefined);
  });
});

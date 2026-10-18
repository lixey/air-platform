import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, type MockInstance, vi } from 'vitest';
import { AlertService } from '../app/alert/alert.service';
import { errorAlertInterceptor, SKIP_GLOBAL_ERROR } from '../app/alert/error-alert.interceptor';
import type { FileInfoDto, MessageDto } from '../core/generated/contracts';
import type { FileView } from './file-view';
import { type FileGridPlace, type FileInfoChange, type FileInfoSource, fileInfoSourceOf } from './file-viewer';

const service = 'sample-report-file-grid';
const place: FileGridPlace = { service, parentService: 'sample-report-form', parentId: 7 };
const photo: FileView = { key: 12, name: 'site.jpg', size: 3000, contentType: 'image/jpeg', url: '/f/12', thumbUrl: '/f/12t' };
const pending: FileView = { ...photo, key: null };
const change: FileInfoChange = { name: 'Facade', description: 'North side', ver: 3 };

function provideHttp(): void {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(withInterceptors([errorAlertInterceptor])), provideHttpClientTesting()],
  });
}

describe('fileInfoSourceOf', () => {
  let http: HttpTestingController;
  let alert: AlertService;
  let error: MockInstance<AlertService['error']>;
  let warnings: MockInstance<AlertService['warnings']>;
  let saved: ReturnType<typeof vi.fn<() => void>>;
  let isCurrent: boolean;
  const canEditInfo = signal(true);
  const canReadMetadata = signal(true);

  function sourceOf(at: FileGridPlace | null = place, withCapture = true): FileInfoSource {
    return fileInfoSourceOf(TestBed.inject(Injector), {
      place: () => at,
      canEditInfo,
      canReadMetadata,
      conflictTitle: () => 'Changed by someone else',
      ...(withCapture ? { capture: () => () => isCurrent } : {}),
      saved,
    });
  }

  beforeEach(() => {
    provideHttp();
    http = TestBed.inject(HttpTestingController);
    alert = TestBed.inject(AlertService);
    error = vi.spyOn(alert, 'error');
    warnings = vi.spyOn(alert, 'warnings');
    saved = vi.fn<() => void>();
    isCurrent = true;
    canEditInfo.set(true);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it("gives the opener's two rights as they are", () => {
    const source = sourceOf();

    expect(source.canEditInfo).toBe(canEditInfo);
    expect(source.canReadMetadata).toBe(canReadMetadata);
  });

  it('asks get-info with the record and the file as id, the global alert silent', async () => {
    const answer: FileInfoDto = {
      id: 12,
      ver: 3,
      name: 'site.jpg',
      contentType: 'image/jpeg',
      size: 3000,
      createdAt: '2026-09-25T10:00:00Z',
    };

    const info = sourceOf().info(photo);
    const request = http.expectOne(`/api/${service}/get-info`);
    request.flush(answer);

    expect(request.request.body).toEqual({ parentService: 'sample-report-form', parentId: 7, id: 12 });
    expect(request.request.context.get(SKIP_GLOBAL_ERROR)).toBe(true);
    expect(await info).toEqual(answer);
  });

  it('asks file-metadata with the record and the file as id, the global alert silent', async () => {
    const metadata = sourceOf().metadata(photo);
    const request = http.expectOne(`/api/${service}/file-metadata`);
    request.flush({ make: 'Apple' });

    expect(request.request.body).toEqual({ parentService: 'sample-report-form', parentId: 7, id: 12 });
    expect(request.request.context.get(SKIP_GLOBAL_ERROR)).toBe(true);
    expect(await metadata).toEqual({ make: 'Apple' });
  });

  it('leaves a failed read to its caller: it rejects and nothing is reported', async () => {
    const info = sourceOf().info(photo);
    http.expectOne(`/api/${service}/get-info`).flush({ title: 'Gone' }, { status: 500, statusText: 'Server Error' });

    await expect(info).rejects.toMatchObject({ status: 500 });
    expect(error).not.toHaveBeenCalled();
  });

  it('rejects a read where the opener names no place, or of a file without a key, with the sentence naming the method', async () => {
    await expect(sourceOf(null).info(photo)).rejects.toThrow("The file viewer's opener names no file grid to ask 'get-info' of.");
    await expect(sourceOf(null).metadata(photo)).rejects.toThrow("The file viewer's opener names no file grid to ask 'file-metadata' of.");
    await expect(sourceOf().info(pending)).rejects.toThrow("The file viewer's opener names no file grid to ask 'get-info' of.");
    await expect(sourceOf().metadata(pending)).rejects.toThrow("The file viewer's opener names no file grid to ask 'file-metadata' of.");
  });

  it('saves through set-info with the file, its version, the name and the description, the global alert silent', async () => {
    const result = sourceOf().saveInfo(photo, change);
    const request = http.expectOne(`/api/${service}/set-info`);
    request.flush({ count: 1 });

    expect(request.request.body).toEqual({
      parentService: 'sample-report-form',
      parentId: 7,
      selectedIds: [12],
      selectedVersions: [3],
      name: 'Facade',
      description: 'North side',
    });
    expect(request.request.context.get(SKIP_GLOBAL_ERROR)).toBe(true);
    expect(await result).toBe(true);
  });

  it('shows the warnings of a committed answer, calls saved and resolves true', async () => {
    const said: MessageDto[] = [{ type: 'warning', title: 'The name was shortened' }];

    const result = sourceOf().saveInfo(photo, change);
    http.expectOne(`/api/${service}/set-info`).flush({ count: 1, outcome: 'committed', warnings: said });

    expect(await result).toBe(true);
    expect(warnings).toHaveBeenCalledTimes(1);
    expect(warnings).toHaveBeenCalledWith(said);
    expect(error).not.toHaveBeenCalled();
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it("reports a refusing answer with the refusal's title and detail, calls saved and resolves false", async () => {
    const result = sourceOf().saveInfo(photo, change);
    http.expectOne(`/api/${service}/set-info`).flush({
      count: 0,
      outcome: 'refused',
      title: 'Not saved',
      messages: [{ type: 'error', title: 'The name is taken.' }],
    });

    expect(await result).toBe(false);
    expect(error).toHaveBeenCalledTimes(1);
    expect(error).toHaveBeenCalledWith('Not saved', 'The name is taken.');
    expect(warnings).not.toHaveBeenCalled();
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it("reports a failed request with the refusal's title and detail once, calls saved and resolves false", async () => {
    const result = sourceOf().saveInfo(photo, change);
    http
      .expectOne(`/api/${service}/set-info`)
      .flush({ title: 'Not saved', detail: 'The file is locked.' }, { status: 400, statusText: 'Bad Request' });

    expect(await result).toBe(false);
    expect(error).toHaveBeenCalledTimes(1);
    expect(error).toHaveBeenCalledWith('Not saved', 'The file is locked.');
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('reports a failure that says nothing as the failed request', async () => {
    const result = sourceOf().saveInfo(photo, change);
    http.expectOne(`/api/${service}/set-info`).flush(null, { status: 500, statusText: 'Server Error' });

    expect(await result).toBe(false);
    expect(error).toHaveBeenCalledWith('The request failed.', undefined);
  });

  it("reports a stale version under the opener's conflict title, as an answer and as a failure", async () => {
    const answered = sourceOf().saveInfo(photo, change);
    http.expectOne(`/api/${service}/set-info`).flush({ count: 0, outcome: 'conflict' });
    expect(await answered).toBe(false);
    expect(error).toHaveBeenLastCalledWith('Changed by someone else', undefined);

    const failed = sourceOf().saveInfo(photo, change);
    http.expectOne(`/api/${service}/set-info`).flush({}, { status: 409, statusText: 'Conflict' });
    expect(await failed).toBe(false);
    expect(error).toHaveBeenCalledTimes(2);
    expect(error).toHaveBeenLastCalledWith('Changed by someone else', undefined);
    expect(saved).toHaveBeenCalledTimes(2);
  });

  it("drops an answer that comes after the opener's capture went stale: nothing shown, saved not called, false", async () => {
    const result = sourceOf().saveInfo(photo, change);
    isCurrent = false;
    http.expectOne(`/api/${service}/set-info`).flush({ count: 1, warnings: [{ type: 'warning', title: 'Shortened' }] });

    expect(await result).toBe(false);
    expect(warnings).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
    expect(saved).not.toHaveBeenCalled();
  });

  it("drops a failure that comes after the opener's capture went stale: nothing reported, saved not called, false", async () => {
    const result = sourceOf().saveInfo(photo, change);
    isCurrent = false;
    http.expectOne(`/api/${service}/set-info`).flush({ title: 'Not saved' }, { status: 400, statusText: 'Bad Request' });

    expect(await result).toBe(false);
    expect(error).not.toHaveBeenCalled();
    expect(saved).not.toHaveBeenCalled();
  });

  it('holds an answer current where the opener gives no capture', async () => {
    const result = sourceOf(place, false).saveInfo(photo, change);
    http.expectOne(`/api/${service}/set-info`).flush({ count: 1 });

    expect(await result).toBe(true);
    expect(saved).toHaveBeenCalledTimes(1);
  });

  it('posts nothing without a place, a key or canEditInfo, and resolves false', async () => {
    expect(await sourceOf(null).saveInfo(photo, change)).toBe(false);
    expect(await sourceOf().saveInfo(pending, change)).toBe(false);
    canEditInfo.set(false);
    expect(await sourceOf().saveInfo(photo, change)).toBe(false);

    http.expectNone(`/api/${service}/set-info`);
    expect(saved).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
  });
});

import { HttpEventType, HttpHeaders, HttpResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../tests/axe';
import { uploadLimits } from '../../tests/upload-limits';
import { AlertService } from '../app/alert/alert.service';
import { errorAlertInterceptor } from '../app/alert/error-alert.interceptor';
import type { CloseParticipant } from '../core/close-decision';
import { OverlayRef } from '../overlay/overlay-ref';
import { singlePanelLayout } from '../panels/single-panel-layout';
import type { FileUploadOverlayModel } from '../ui-components';
import { FileUploadOverlayComponent, uploadOverlayOf } from './file-upload-overlay.component';

const MIB = 1024 * 1024;
const service = 'sample-report-file-grid';
const target: FileUploadOverlayModel = {
  kind: 'FileUploadOverlay',
  service,
  parentService: 'sample-report-form',
  parentId: 7,
  ...uploadLimits({
    accept: ['image/png', 'image/jpeg'],
    extensions: ['.png', '.jpg', '.jpeg'],
    maxBytes: MIB,
    sourceMaxBytes: MIB,
    images: 'keep',
  }),
  shownFiles: [],
  committedWarningTitle: 'Saved with warnings',
};

const fileOf = (name: string, type: string, size: number): File => new File([new Uint8Array(size)], name, { type });

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i++) {
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
  }
}

describe('FileUploadOverlayComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<FileUploadOverlayComponent>;
  let participants: CloseParticipant[];
  const alerts = { open: vi.fn(), error: vi.fn(), warnings: AlertService.prototype.warnings };
  const ref = {
    isClosed: false,
    name: vi.fn(),
    dismiss: vi.fn(() => Promise.resolve(true)),
    registerCloseParticipant: (participant: CloseParticipant) => {
      participants.push(participant);
      return () => (participants = participants.filter(p => p !== participant));
    },
  };

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const picker = (): HTMLInputElement => host().querySelector('input[type="file"]') as HTMLInputElement;
  const statuses = (): (string | null)[] =>
    [...host().querySelectorAll('[data-upload-item]')].map(item => item.getAttribute('data-status'));
  const statusTexts = (): string[] =>
    [...host().querySelectorAll('[data-upload-status]')].map(status => (status.textContent ?? '').replace(/\s+/g, ' ').trim());
  const uploads = (): TestRequest[] => http.match(`/api/${service}/upload`);
  /** Whether the dialog lets itself be closed now, as its participant answers. */
  const acceptsClose = (): boolean => participants.every(participant => participant({}) === true);

  /** The user drops the files on the zone. */
  async function drop(...files: File[]): Promise<void> {
    const event = new Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: { files } });
    host().querySelector('[data-drop-zone]')?.dispatchEvent(event);
    await settle();
    fixture.detectChanges();
  }

  beforeEach(() => {
    participants = [];
    alerts.open.mockClear();
    alerts.error.mockClear();
    ref.dismiss.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorAlertInterceptor])),
        provideHttpClientTesting(),
        { provide: AlertService, useValue: alerts },
        { provide: OverlayRef, useValue: ref },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FileUploadOverlayComponent);
    fixture.componentRef.setInput('model', target);
    fixture.detectChanges();
  });
  afterEach(() => {
    fixture.destroy();
    http.verify();
    TestBed.resetTestingModule();
  });

  it("names the card by its title and offers the browser's picker for several files of the accepted types", () => {
    const opened = vi.spyOn(picker(), 'click').mockImplementation(() => undefined);

    (host().querySelector('[data-drop-zone] ui-btn') as HTMLElement).dispatchEvent(new MouseEvent('click'));

    expect(opened).toHaveBeenCalledTimes(1);
    expect(picker().multiple).toBe(true);
    expect(picker().getAttribute('accept')).toBe('image/png,image/jpeg,.png,.jpg,.jpeg');
    expect(host().querySelector('[data-files-limits]')?.textContent?.trim()).toBe('PNG, JPG - up to 1 MB');
    expect(ref.name).toHaveBeenCalledWith({ labelledBy: host().querySelector('h2')?.id });
  });

  it('marks a refused file without a request and posts the others one after another, each alone with the parent', async () => {
    await drop(
      fileOf('setup.exe', 'application/x-msdownload', 100),
      fileOf('logo.png', 'image/png', 100),
      fileOf('photo.jpg', 'image/jpeg', 200),
    );

    expect(statuses()).toEqual(['refused', 'uploading', 'waiting']);
    expect(statusTexts()[0]).toBe('The file is refused. Files of this type are not accepted here; accepted: PNG, JPG.');
    const [first] = uploads();
    const body = first.request.body as FormData;
    expect(body.get('parentService')).toBe('sample-report-form');
    expect(body.get('parentId')).toBe('7');
    expect((body.getAll('file') as File[]).map(file => file.name)).toEqual(['logo.png']);
    expect(first.request.reportProgress).toBe(true);

    first.flush({ count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();

    expect(statuses()).toEqual(['refused', 'uploaded', 'uploading']);
    const [second] = uploads();
    expect(((second.request.body as FormData).get('file') as File).name).toBe('photo.jpg');
    second.flush({ count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();

    expect(statuses()).toEqual(['refused', 'uploaded', 'uploaded']);
    expect(statusTexts().slice(1)).toEqual(['Uploaded', 'Uploaded']);
    expect(alerts.error).not.toHaveBeenCalled();
  });

  it('refuses a file larger than the limit before posting it', async () => {
    await drop(fileOf('big.png', 'image/png', 2 * MIB));

    expect(statuses()).toEqual(['refused']);
    expect(statusTexts()).toEqual(['The file is refused. The file is larger than 1 MB.']);
    expect(uploads()).toEqual([]);
  });

  it('shows the progress of the file posting', async () => {
    await drop(fileOf('logo.png', 'image/png', 100));
    const [request] = uploads();

    request.event({ type: HttpEventType.UploadProgress, loaded: 30, total: 100 });
    fixture.detectChanges();

    const bar = host().querySelector('[role="progressbar"]');
    expect(bar?.getAttribute('aria-valuenow')).toBe('30');
    expect(
      host()
        .querySelector(`#${bar?.getAttribute('aria-labelledby')}`)
        ?.textContent?.trim(),
    ).toBe('logo.png');
    expect(statusTexts()).toEqual(['Uploading…']);

    request.event(new HttpResponse({ body: { count: 1, outcome: 'committed' }, headers: new HttpHeaders(), status: 200 }));
    await settle();
    fixture.detectChanges();

    expect(host().querySelector('[role="progressbar"]')).toBeNull();
    expect(statuses()).toEqual(['uploaded']);
  });

  it("marks a file the server refuses failed with the server's reason and goes on with the next", async () => {
    await drop(fileOf('fake.png', 'image/png', 100), fileOf('logo.png', 'image/png', 100));
    uploads()[0].flush(
      {
        title: 'The file is refused.',
        status: 400,
        outcome: 'refused',
        messages: [{ type: 'error', code: 'file.content', path: 'file', title: 'The file content is not image/png.' }],
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    fixture.detectChanges();

    expect(statuses()).toEqual(['failed', 'uploading']);
    expect(statusTexts()[0]).toBe('Not uploaded The file content is not image/png.');
    expect(alerts.error).not.toHaveBeenCalled();

    uploads()[0].flush({ count: 1, outcome: 'committed' });
    await settle();
  });

  it('shows the warnings of a committed upload under the title the opener gave', async () => {
    await drop(fileOf('logo.png', 'image/png', 100));
    uploads()[0].flush({ count: 1, outcome: 'committed', warnings: [{ type: 'warning', code: 'x' }] });
    await settle();

    expect(alerts.open).toHaveBeenCalledWith([{ type: 'warning', code: 'x', title: 'Saved with warnings' }]);
  });

  it('stays open while a file is waiting or posting, and closes on its button once none is', async () => {
    await drop(fileOf('logo.png', 'image/png', 100));

    expect(acceptsClose()).toBe(false);
    expect(host().querySelector('footer ui-btn')?.getAttribute('aria-busy')).toBe('true');

    uploads()[0].flush({ count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();
    (host().querySelector('footer ui-btn') as HTMLElement).dispatchEvent(new MouseEvent('click'));

    expect(acceptsClose()).toBe(true);
    expect(ref.dismiss).toHaveBeenCalledTimes(1);
  });

  it('cancels a posting file and a waiting one by their Cancel, and posts the files after them', async () => {
    await drop(fileOf('a.png', 'image/png', 100), fileOf('b.png', 'image/png', 100), fileOf('c.png', 'image/png', 100));
    const [posting] = uploads();
    const cancels = (): HTMLElement[] => [...host().querySelectorAll<HTMLElement>('[data-upload-cancel]')];

    expect(cancels().map(cancel => cancel.getAttribute('aria-label'))).toEqual([
      'Cancel the upload of a.png',
      'Cancel the upload of b.png',
      'Cancel the upload of c.png',
    ]);
    cancels()[0].dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();
    cancels()[0].dispatchEvent(new MouseEvent('click'));
    await settle();
    fixture.detectChanges();

    expect(posting.cancelled).toBe(true);
    expect(statuses()).toEqual(['uploading']);
    const [next] = uploads();
    expect(((next.request.body as FormData).get('file') as File).name).toBe('c.png');
    next.flush({ count: 1, outcome: 'committed', id: 3 });
    await settle();
    fixture.detectChanges();
    expect(cancels()).toEqual([]);
  });

  it('refuses the files beyond the room the grid showed at the opening as the record being full', async () => {
    fixture.componentRef.setInput('model', {
      ...target,
      maxFiles: 3,
      shownFiles: [
        { id: 1, size: 10 },
        { id: 2, size: 10 },
      ],
    });
    await drop(fileOf('a.png', 'image/png', 100), fileOf('b.png', 'image/png', 100));

    expect(statuses()).toEqual(['uploading', 'refused']);
    expect(statusTexts()[1]).toBe('The file is refused. The record has no room for this file.');
    uploads()[0].flush({ count: 1, outcome: 'committed', id: 3 });
    await settle();
  });

  it('cancels the post in flight when it goes away', async () => {
    await drop(fileOf('logo.png', 'image/png', 100));
    const [request] = uploads();

    fixture.destroy();

    expect(request.cancelled).toBe(true);
  });

  it('passes axe', async () => {
    await drop(fileOf('setup.exe', 'application/x-msdownload', 100), fileOf('logo.png', 'image/png', 100));
    const [request] = uploads();
    request.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 100 });
    fixture.detectChanges();

    await expectAccessible(host());

    request.flush({ count: 1, outcome: 'committed' });
    await settle();
  });
});

describe('uploadOverlayOf', () => {
  it('opens the upload dialog as a modal card a click beside it leaves open, its one panel holding the target', () => {
    const dialog = { ...target, shownFiles: [{ id: 3, size: 100 }] };
    const { kind: _, ...opened } = dialog;

    expect(uploadOverlayOf(opened)).toEqual({
      key: 'file.upload.sample-report-file-grid',
      presentation: 'dialog',
      closesOnOutside: false,
      layout: singlePanelLayout('file.upload', dialog),
    });
  });
});

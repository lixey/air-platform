import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { inject, Injector } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../../tests/axe';
import { boxesOf } from '../../../tests/boxes';
import { formElement, formModel } from '../../../tests/form-model';
import { uploadLimits } from '../../../tests/upload-limits';
import { userProfile } from '../../../tests/user-profile';
import { AlertService } from '../../app/alert/alert.service';
import { AuthService } from '../../app/auth/auth.service';
import type { CloseApproval } from '../../core/close-decision';
import * as download from '../../core/download';
import { provideDynamicComponents } from '../../core/dynamic-component/provide-dynamic-components';
import type { FileUploadLimitsDto, FormFilesModel, FormModel } from '../../core/generated/contracts';
import { FILE_VIEWER_SOURCE, type FileViewerSource } from '../../file/file-viewer';
import { ConfirmationService } from '../../overlay/confirmation.service';
import { OverlayRef } from '../../overlay/overlay-ref';
import { OverlayService } from '../../overlay/overlay.service';
import { PanelsContext } from '../../panels/panels-context';
import { ScreenLifecycle } from '../../screen/screen-lifecycle';
import { ScreenMutations } from '../../screen/screen-mutations';
import { UI_COMPONENTS } from '../../ui-components';
import { FORM_TAB } from '../form-panel-values';
import { FormComponent } from '../form.component';
import { FormService } from '../form.service';
import { FormFilesComponent } from './form-files.component';

const service = 'sample-folder-file-grid';
const files: FormFilesModel = {
  kind: 'FormFiles',
  key: 'files',
  label: 'Files',
  service,
  parentService: 'sample-folder-form',
  parentId: 7,
  look: 'tiles',
  limits: uploadLimits(),
  canUpload: true,
  canDelete: true,
  canSetCover: false,
  canReadMetadata: false,
};

/** The files element with its limits changed as given. */
const limitedTo = (limits: Partial<FileUploadLimitsDto>): FormFilesModel => ({ ...files, limits: { ...files.limits, ...limits } });
const rows = [
  { id: 1, ver: 3, name: 'logo.png', contentType: 'image/png', size: 100, url: '/files/1?sig=a', thumbUrl: '/files/1t?sig=c' },
  { id: 2, ver: 1, name: 'plan.pdf', contentType: 'application/pdf', size: 2000, url: '/files/2?sig=b' },
];

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i++) {
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
  }
}

const fileOf = (name: string, type: string, size: number): File => new File([new Uint8Array(size)], name, { type });

describe('FormFilesComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<FormFilesComponent>;
  const alerts = { open: vi.fn(), error: vi.fn(), warnings: AlertService.prototype.warnings };
  const confirm = vi.fn((_options: unknown, _signal?: AbortSignal) => Promise.resolve(true));
  const saved = vi.spyOn(download, 'saveFile');
  /** Whether the element stands in an overlay, which a test that puts it in one says before it configures again. */
  let isInOverlay = false;
  let lifecycle: Pick<ScreenLifecycle, 'registerOwner'> | null = null;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const names = (): string[] =>
    [...host().querySelectorAll('[data-file] [data-file-open]')].map(open => open.getAttribute('aria-label') ?? '');
  const statuses = (): (string | null)[] => [...host().querySelectorAll('[data-file]')].map(file => file.getAttribute('data-status'));
  const zone = (): HTMLElement | null => host().querySelector('[data-files-zone]');
  const buttonNamed = (name: string): HTMLElement | null =>
    [...host().querySelectorAll<HTMLElement>('ui-btn, button')].find(
      button => button.getAttribute('title') === name || button.textContent?.trim() === name,
    ) ?? null;
  const uploads = () => http.match(`/api/${service}/upload`);

  /** The page of files the element asks for, answered with `answer`. */
  async function answerPage(answer: readonly object[] = rows): Promise<void> {
    http
      .expectOne(`/api/${service}/get-data`)
      .flush({ data: { rows: answer, page: 1, pageSize: 300, hasMore: false, total: answer.length } });
    await settle();
    fixture.detectChanges();
  }

  function mount(model: FormFilesModel = files): void {
    fixture = TestBed.createComponent(FormFilesComponent);
    fixture.componentRef.setInput('model', model);
    fixture.detectChanges();
  }

  /** The user drops the files on the element. */
  async function drop(...dropped: File[]): Promise<void> {
    const event = new Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'dataTransfer', { value: { files: dropped } });
    zone()?.dispatchEvent(event);
    await settle();
    fixture.detectChanges();
  }

  /** The testing module; the overlay and the routed screen are read once, when the form service is made. */
  function configure(): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        FormService,
        { provide: AlertService, useValue: alerts },
        { provide: ConfirmationService, useValue: { confirm } },
        {
          provide: OverlayRef,
          useFactory: () =>
            isInOverlay ? new OverlayRef({ key: 'test.form', presentation: 'dialog' }, null, inject(Injector), 'work') : null,
        },
        { provide: ScreenLifecycle, useFactory: () => lifecycle },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const form = TestBed.inject(FormService);
    form.setModel(formModel());
    form.setData({ id: 7, ver: 1, value: {} });
  }

  /** Configures the module again, for a test that changed what the element stands in. */
  function reconfigure(): void {
    TestBed.resetTestingModule();
    configure();
  }

  beforeEach(() => {
    alerts.open.mockClear();
    alerts.error.mockClear();
    confirm.mockClear();
    saved.mockReset().mockImplementation(() => undefined);
    isInOverlay = false;
    lifecycle = null;
    configure();
  });
  afterEach(() => {
    fixture.destroy();
    http.verify();
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  it("asks one page of the file grid's data under the record and draws each file as a tile", async () => {
    mount();

    const request = http.expectOne(`/api/${service}/get-data`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, isDataOnly: true, page: 1 });
    request.flush({ data: { rows, page: 1, pageSize: 300, hasMore: false, total: 2 } });
    await settle();
    fixture.detectChanges();

    expect(host().querySelector('h3')?.textContent?.trim()).toBe('Files');
    expect(host().querySelector('ul')?.getAttribute('role')).toBe('list');
    expect(names()).toEqual(['Open logo.png', 'Open plan.pdf']);
    const [logo, plan] = [...host().querySelectorAll('[data-file]')];
    expect(logo.querySelector('img')?.getAttribute('src')).toBe('/files/1t?sig=c');
    expect(logo.querySelector('[data-file-name] [aria-hidden="true"]')?.textContent?.trim()).toBe('logo');
    expect(logo.querySelector('ui-file-tile > div')?.getAttribute('title')).toBe('logo.png\n100 bytes');
    expect(plan.querySelector('img')).toBeNull();
    expect(plan.querySelector('[data-file-open]')?.textContent).toContain('PDF');
    expect(plan.querySelector('[data-file-download]')?.getAttribute('href')).toBe('/files/2?sig=b&download');
    expect(plan.querySelector('[data-file-download]')?.getAttribute('download')).toBe('plan.pdf');
  });

  it('retries a page that failed', async () => {
    mount();
    http.expectOne(`/api/${service}/get-data`).flush({ title: 'Down' }, { status: 500, statusText: 'Server Error' });
    await settle();
    fixture.detectChanges();
    expect(host().querySelector('[role="alert"]')?.textContent?.trim()).toBe('The request failed.');
    expect(names()).toEqual([]);

    buttonNamed('Try again')?.dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();
    const asked = http.expectOne(`/api/${service}/get-data`);
    expect(asked.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, isDataOnly: true, page: 1 });
    asked.flush({ data: { rows, page: 1, pageSize: 300, hasMore: false, total: 2 } });
    await settle();
    fixture.detectChanges();

    expect(host().querySelector('[role="alert"]')).toBeNull();
    expect(names()).toEqual(['Open logo.png', 'Open plan.pdf']);
  });

  it('drops a page that arrives after the record changed', async () => {
    mount();
    const late = http.expectOne(`/api/${service}/get-data`);

    fixture.componentRef.setInput('model', { ...files, parentId: 8 });
    fixture.detectChanges();
    await settle();

    expect(late.cancelled).toBe(true);
    const asked = http.expectOne(`/api/${service}/get-data`);
    expect(asked.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 8, isDataOnly: true, page: 1 });
    asked.flush({ data: { rows: [rows[1]], page: 1, pageSize: 300, hasMore: false, total: 1 } });
    await settle();
    fixture.detectChanges();

    expect(names()).toEqual(['Open plan.pdf']);
  });

  it('draws one line per file in the list look', async () => {
    mount({ ...files, look: 'list' });
    await answerPage();

    expect(host().querySelector('ul')?.className).toContain('flex-col');
    expect(names()).toEqual(['Open logo.png', 'Open plan.pdf']);
    const logo = host().querySelector('[data-file]') as HTMLElement;
    expect(logo.querySelector('[data-file-name]')?.textContent?.trim()).toBe('logo.png');
    expect(logo.querySelector('[data-file-open]')?.textContent).toContain('100 bytes');
    expect(logo.querySelector('ui-file-tile > div')?.getAttribute('title')).toBeNull();
    expect(logo.querySelector('[data-file-actions]')?.children).toHaveLength(3);
  });

  it('names a tile on two lines by its name without the extension, its full name and size its title, its actions a joined grey ghost row on its foot', async () => {
    mount({ ...files, canSetCover: true });
    await answerPage([
      {
        id: 5,
        ver: 1,
        name: 'catalog-retro.webp',
        contentType: 'image/webp',
        size: 2.2 * 1024 * 1024,
        url: '/files/5?sig=g',
        isCover: true,
      },
    ]);

    const tile = host().querySelector('[data-file]') as HTMLElement;
    const name = tile.querySelector('[data-file-name]') as HTMLElement;
    expect(name.querySelector('[aria-hidden="true"]')?.textContent?.trim()).toBe('catalog-retro');
    expect(name.querySelector('.sr-only')?.textContent?.trim()).toBe('catalog-retro.webp');
    expect(name.className).toContain('line-clamp-2');
    expect(name.className).toContain('h-[2lh]');
    expect(name.className).toContain('break-words');
    expect(name.className).not.toContain('truncate');
    expect(tile.querySelector('[data-file-open]')?.textContent).not.toContain('MB');
    expect(tile.querySelector('ui-file-tile > div')?.getAttribute('title')).toBe('catalog-retro.webp\n2.2 MB');
    expect(names()).toEqual(['Open catalog-retro.webp']);

    const block = tile.querySelector('[data-file-actions]') as HTMLElement;
    expect([...block.children].map(action => action.getAttribute('title'))).toEqual([
      'Move catalog-retro.webp',
      'Cover',
      'Download',
      'Delete catalog-retro.webp',
    ]);
    expect(block.querySelector('[data-reorder-handle] [icon="grip-vertical"]')).not.toBeNull();
    // Flush on the tile's edges: no gap, margin, box, tint or border of its own but the hairline above it
    expect(block.className).toContain('hue-primary');
    expect(block.classList).toContain('border-t');
    expect(block.classList).toContain('border-line');
    expect(block.className).not.toMatch(/(^|\s)(gap-|m-|p-|rounded-|bg-|hue-blue)/);
    for (const action of block.children) {
      expect(action.className).toContain('flex-1');
      expect(action.className).toContain('text-(--btn-ink)');
      expect(action.className).toContain('first:rounded-bl-');
      expect(action.className).toContain('last:rounded-br-');
      expect(action.className).toContain('focus-visible:-outline-offset-2');
      expect(action.className).not.toMatch(/(^|\s)(rounded-lg|bg-\(--btn-ground\))(\s|$)/);
    }
    // The cover's pin, pressed, says so quietly: its icon in the checked role, no solid fill
    const pin = block.querySelector('[data-file-cover]') as HTMLElement;
    expect(pin.getAttribute('aria-pressed')).toBe('true');
    expect(pin.className).toContain('text-checked!');
    expect(pin.className).not.toContain('bg-(--hue)');
    await expectAccessible(host());
  });

  it("draws a video's first frame as its tile's picture, and a video without one as the badge of its extension", async () => {
    mount();
    await answerPage([
      { id: 3, ver: 1, name: 'clip.mp4', contentType: 'video/mp4', size: 3000, url: '/files/3?sig=d', thumbUrl: '/files/3t?sig=e' },
      { id: 4, ver: 1, name: 'raw.mp4', contentType: 'video/mp4', size: 4000, url: '/files/4?sig=f' },
    ]);

    const [clip, raw] = [...host().querySelectorAll('[data-file]')];
    expect(clip.querySelector('img')?.getAttribute('src')).toBe('/files/3t?sig=e');
    expect(raw.querySelector('img')).toBeNull();
    expect(raw.querySelector('[data-file-open]')?.textContent).toContain('MP4');
    expect(raw.querySelector('[data-file-download]')?.getAttribute('href')).toBe('/files/4?sig=f&download');
  });

  it('says there are no files yet on an empty page', async () => {
    mount();
    await answerPage([]);

    expect(host().querySelector('[data-files-empty]')?.textContent?.trim()).toBe('No files yet.');
    expect(buttonNamed('Delete all')).toBeNull();
  });

  it('asks nothing for a new record and shows the upload it allows disabled with the hint to save first', () => {
    mount({ ...files, parentId: null });

    http.expectNone(`/api/${service}/get-data`);
    const choose = buttonNamed('Choose files');
    const hint = host().querySelector('[data-files-hint]');
    expect(hint?.textContent?.trim()).toBe('Save the record to add its files.');
    expect(choose?.getAttribute('aria-disabled')).toBe('true');
    expect(choose?.getAttribute('aria-describedby')).toBe(hint?.id);
    expect(zone()).toBeNull();
  });

  it('shows no upload for a new record the model allows none on', () => {
    mount({ ...files, parentId: null, canUpload: false, canDelete: false });

    http.expectNone(`/api/${service}/get-data`);
    expect(buttonNamed('Choose files')).toBeNull();
    expect(host().querySelector('[data-files-hint]')?.textContent?.trim()).toBe('Save the record to add its files.');
  });

  it('offers a reader the files to open and download, Download all, and no zone, pick, paste, delete or Delete all', async () => {
    mount({ ...files, canUpload: false, canDelete: false });
    await answerPage();

    expect([...host().querySelectorAll('ui-btn')].map(button => button.textContent?.trim())).toEqual(['Download all']);
    expect(host().querySelector('[data-files-count]')).toBeNull();
    expect(names()).toEqual(['Open logo.png', 'Open plan.pdf']);
    expect(zone()?.hasAttribute('tabindex')).toBe(false);
    expect(host().querySelector('input[type="file"]')).toBeNull();
    expect(host().querySelector('[data-files-limits]')).toBeNull();

    const event = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'clipboardData', { value: { files: [fileOf('image.png', 'image/png', 10)] } });
    zone()?.dispatchEvent(event);
    await settle();
    expect(event.defaultPrevented).toBe(false);
    expect(uploads()).toEqual([]);
  });

  it('shows the limits before any pick and limits the picker to the accepted types and extensions', async () => {
    mount(
      limitedTo({
        accept: ['image/png', 'image/jpeg', 'application/pdf'],
        extensions: ['.png', '.jpg', '.jpeg', '.pdf'],
        maxBytes: 10 * 1024 * 1024,
      }),
    );
    await answerPage();

    const limits = host().querySelector('[data-files-limits]');
    const count = host().querySelector('[data-files-count]');
    expect(limits?.textContent?.trim()).toBe('PNG, JPG, PDF - up to 10 MB');
    expect(count?.textContent?.trim()).toBe('2 of 100');
    const picker = host().querySelector('input[type="file"]') as HTMLInputElement;
    expect(picker.multiple).toBe(true);
    expect(picker.getAttribute('accept')).toBe('image/png,image/jpeg,application/pdf,.png,.jpg,.jpeg,.pdf');
    expect(buttonNamed('Choose files')?.getAttribute('aria-describedby')).toBe(`${limits?.id} ${count?.id}`);
    expect(zone()?.getAttribute('aria-describedby')).toContain(`${limits?.id} ${count?.id}`);
    expect(buttonNamed('Take photo')).toBeNull();
  });

  it('makes the element the zone: it takes focus, and Enter or Space on it opens the picker', async () => {
    mount();
    await answerPage();
    const picker = host().querySelector('input[type="file"]') as HTMLInputElement;
    const opened = vi.spyOn(picker, 'click').mockImplementation(() => undefined);

    expect(zone()?.getAttribute('tabindex')).toBe('0');
    expect(zone()?.getAttribute('role')).toBe('group');
    expect(zone()?.getAttribute('aria-labelledby')).toBe(host().querySelector('h3')?.id);
    zone()?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    zone()?.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    host()
      .querySelector('[data-file-open]')
      ?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    buttonNamed('Choose files')?.dispatchEvent(new MouseEvent('click'));

    expect(opened).toHaveBeenCalledTimes(3);
  });

  it('offers Take photo, asking the camera for the accepted raster images, on a coarse pointer', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === '(pointer: coarse)' }));
    mount();
    await answerPage();

    const camera = host().querySelector('input[capture]') as HTMLInputElement;
    expect(buttonNamed('Take photo')).not.toBeNull();
    expect(camera.getAttribute('capture')).toBe('environment');
    expect(camera.getAttribute('accept')).toBe('image/png');
    expect(buttonNamed('Take video')).toBeNull();
  });

  it('offers Take video, asking the camera to record the accepted videos, on a coarse pointer', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === '(pointer: coarse)' }));
    mount(limitedTo({ accept: ['image/png', 'video/mp4', 'video/quicktime'], extensions: ['.png', '.mp4', '.mov'] }));
    await answerPage();

    const video = host().querySelector('input[data-files-video]') as HTMLInputElement;
    const opened = vi.spyOn(video, 'click').mockImplementation(() => undefined);
    expect(buttonNamed('Take photo')).not.toBeNull();
    expect(video.getAttribute('capture')).toBe('environment');
    expect(video.getAttribute('accept')).toBe('video/mp4,video/quicktime');
    buttonNamed('Take video')?.dispatchEvent(new MouseEvent('click'));
    expect(opened).toHaveBeenCalledTimes(1);
  });

  it('shows a record holding maxFiles files full: no zone, pick, paste or camera, while Delete all and Download all stay', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: query === '(pointer: coarse)' }));
    mount(limitedTo({ maxFiles: 2 }));
    await answerPage();

    expect(host().querySelector('[data-files-count]')?.textContent?.trim()).toBe('2 of 2: no room for more files');
    expect(host().querySelector('[data-files-limits]')).toBeNull();
    expect(buttonNamed('Choose files')).toBeNull();
    expect(buttonNamed('Take photo')).toBeNull();
    expect(host().querySelector('input[type="file"]')).toBeNull();
    expect(zone()?.hasAttribute('tabindex')).toBe(false);
    expect(host().querySelector('[data-files-drop]')).toBeNull();
    expect(buttonNamed('Delete all')).not.toBeNull();
    expect(buttonNamed('Download all')).not.toBeNull();

    const event = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'clipboardData', { value: { files: [fileOf('image.png', 'image/png', 10)] } });
    zone()?.dispatchEvent(event);
    await drop(fileOf('b.png', 'image/png', 10));
    expect(event.defaultPrevented).toBe(false);
    expect(uploads()).toEqual([]);
    await expectAccessible(host());
  });

  it('shows a record holding maxTotalBytes full', async () => {
    mount(limitedTo({ maxTotalBytes: 2100 }));
    await answerPage();

    expect(host().querySelector('[data-files-count]')?.textContent?.trim()).toBe('2 of 100: no room for more files');
    expect(buttonNamed('Choose files')).toBeNull();
  });

  it('counts the files uploaded since the page was asked until the page asked again shows them', async () => {
    mount(limitedTo({ maxFiles: 5 }));
    await answerPage();
    const count = (): string | undefined => host().querySelector('[data-files-count]')?.textContent?.trim();

    await drop(fileOf('a.png', 'image/png', 100), fileOf('b.pdf', 'application/pdf', 200));
    uploads()[0].flush({ id: 3, count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();
    expect(count()).toBe('3 of 5');
    uploads()[0].flush({ id: 4, count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();
    expect(count()).toBe('4 of 5');

    // The page is asked again: the two uploaded files still take the record's room
    await drop(fileOf('c.pdf', 'application/pdf', 10), fileOf('d.pdf', 'application/pdf', 10));
    expect(statuses()).toEqual(['uploaded', 'uploaded', 'uploading', 'refused', null, null]);
    uploads()[0].flush({ id: 5, count: 1, outcome: 'committed' });
    await settle();
    const added = [
      { id: 5, ver: 1, name: 'c.pdf', contentType: 'application/pdf', size: 10, url: '/files/5' },
      { id: 3, ver: 1, name: 'a.png', contentType: 'image/png', size: 100, url: '/files/3' },
      { id: 4, ver: 1, name: 'b.pdf', contentType: 'application/pdf', size: 200, url: '/files/4' },
    ];
    await answerPage([...added, ...rows]);

    expect(count()).toBe('5 of 5: no room for more files');
    expect(statuses()).toEqual(['refused', null, null, null, null, null]);
  });

  it('counts a file uploaded without its key while the page is asked again, and not once the page answered', async () => {
    mount(limitedTo({ maxFiles: 5 }));
    await answerPage();
    const count = (): string | undefined => host().querySelector('[data-files-count]')?.textContent?.trim();
    expect(count()).toBe('2 of 5');

    await drop(fileOf('c.pdf', 'application/pdf', 10));
    uploads()[0].flush({ count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();
    // The queue has no post left and the page is asked again: the file the server named no key for still counts
    expect(count()).toBe('3 of 5');

    await answerPage([{ id: 5, ver: 1, name: 'c.pdf', contentType: 'application/pdf', size: 10, url: '/files/5' }, ...rows]);
    expect(count()).toBe('3 of 5');
    expect(statuses()).toEqual([null, null, null]);
  });

  it('shows each dropped file at once as a tile with its progress, posts them in turn, and reloads once none is left', async () => {
    mount();
    await answerPage();

    await drop(fileOf('a.png', 'image/png', 100), fileOf('b.pdf', 'application/pdf', 200));

    expect(statuses()).toEqual(['uploading', 'waiting', null, null]);
    expect(host().querySelector('[role="progressbar"]')).not.toBeNull();
    const [first] = uploads();
    expect((first.request.body as FormData).get('parentId')).toBe('7');
    expect((first.request.body as FormData).get('afterId')).toBeNull();
    expect(((first.request.body as FormData).get('file') as File).name).toBe('a.png');
    first.flush({ id: 3, count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();

    expect(statuses()).toEqual(['uploaded', 'uploading', null, null]);
    http.expectNone(`/api/${service}/get-data`);
    const [second] = uploads();
    expect((second.request.body as FormData).get('afterId')).toBe('3');
    second.flush({ id: 4, count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();

    const added = [
      { id: 3, ver: 1, name: 'a.png', contentType: 'image/png', size: 100, url: '/files/3' },
      { id: 4, ver: 1, name: 'b.pdf', contentType: 'application/pdf', size: 200, url: '/files/4' },
    ];
    await answerPage([...added, ...rows]);
    expect(names()).toEqual(['Open a.png', 'Open b.pdf', 'Open logo.png', 'Open plan.pdf']);
    expect(statuses()).toEqual([null, null, null, null]);
  });

  it("takes no drag that starts on the page, a file's picture dropped back on the element among them", async () => {
    mount();
    await answerPage();
    const picture = host().querySelector('[data-file] img') ?? host().querySelector('[data-file-open]');
    const transfer = {
      data: new Map<string, string>(),
      setData(type: string, value: string) {
        this.data.set(type, value);
      },
    };
    const start = new Event('dragstart', { bubbles: true });
    Object.defineProperty(start, 'dataTransfer', { value: transfer });
    picture?.dispatchEvent(start);

    const over = new Event('dragover', { bubbles: true, cancelable: true });
    Object.defineProperty(over, 'dataTransfer', { value: { types: [...transfer.data.keys(), 'Files'] } });
    zone()?.dispatchEvent(over);
    const dropped = new Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(dropped, 'dataTransfer', {
      value: { types: [...transfer.data.keys(), 'Files'], files: [fileOf('logo.png', 'image/png', 100)] },
    });
    zone()?.dispatchEvent(dropped);
    await settle();
    fixture.detectChanges();

    expect(over.defaultPrevented).toBe(false);
    expect(dropped.defaultPrevented).toBe(false);
    expect(statuses()).toEqual([null, null]);
    expect(uploads()).toHaveLength(0);
  });

  it('keeps a refused file with its reason and Remove, and a failed post with Try again', async () => {
    mount();
    await answerPage();

    await drop(
      fileOf('setup.exe', 'application/x-msdownload', 100),
      fileOf('big.png', 'image/png', 2 * 1024 * 1024),
      fileOf('plan.pdf', 'application/pdf', 100),
    );
    uploads()[0].flush(
      {
        title: 'The file is refused.',
        status: 400,
        outcome: 'refused',
        messages: [{ type: 'error', code: 'file.content', title: 'Not a PDF.' }],
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    fixture.detectChanges();
    await answerPage();

    expect(statuses()).toEqual(['refused', 'refused', 'failed', null, null]);
    const texts = [...host().querySelectorAll('[data-upload-status]')].map(status => status.textContent?.replace(/\s+/g, ' ').trim());
    expect(texts).toEqual([
      'The file is refused. Files of this type are not accepted here; accepted: PNG, PDF.',
      'The file is refused. The file is larger than 1 MB.',
      'Not uploaded Not a PDF.',
    ]);
    expect(host().querySelectorAll('[data-file] ui-btn[title="Remove"]')).toHaveLength(3);
    expect(host().querySelectorAll('[data-file] ui-btn[title="Try again"]')).toHaveLength(1);

    (host().querySelector('[data-file][data-status="refused"] ui-btn[title="Remove"]') as HTMLElement).dispatchEvent(
      new MouseEvent('click'),
    );
    fixture.detectChanges();
    expect(statuses()).toEqual(['refused', 'failed', null, null]);
  });

  it('takes a file pasted while it holds focus, named by the moment of the paste', async () => {
    mount();
    await answerPage();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 8, 27, 14, 5, 9));
    try {
      const event = new Event('paste', { bubbles: true, cancelable: true });
      Object.defineProperty(event, 'clipboardData', { value: { files: [fileOf('image.png', 'image/png', 10)] } });
      host().querySelector('[data-file-open]')?.dispatchEvent(event);
      await settle();
      fixture.detectChanges();
    } finally {
      vi.useRealTimers();
    }

    const [request] = uploads();
    expect(((request.request.body as FormData).get('file') as File).name).toBe('pasted-20260927-140509.png');
    request.flush({ count: 1, outcome: 'committed' });
    await settle();
    await answerPage();
  });

  it('names a pasted file and the archive by the moment on the clock of a Kyiv profile under a New York browser', async () => {
    TestBed.inject(AuthService).setUser(userProfile(1, {}, 'Europe/Kyiv'));
    mount();
    await answerPage();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 8, 27, 20, 5, 9));
    try {
      const event = new Event('paste', { bubbles: true, cancelable: true });
      Object.defineProperty(event, 'clipboardData', { value: { files: [fileOf('image.png', 'image/png', 10)] } });
      host().querySelector('[data-file-open]')?.dispatchEvent(event);
      await settle();
      fixture.detectChanges();
      const [request] = uploads();
      expect(((request.request.body as FormData).get('file') as File).name).toBe('pasted-20260928-030509.png');
      request.flush({ count: 1, outcome: 'committed' });
      await settle();
      await answerPage();

      buttonNamed('Download all')?.dispatchEvent(new MouseEvent('click'));
      const archive = new Blob([new Uint8Array([0x50, 0x4b, 3, 4])], { type: 'application/zip' });
      http
        .expectOne(`/api/${service}/download-all`)
        .flush(archive, { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''Folder_7.zip" } });
      await settle();

      expect(saved).toHaveBeenCalledWith({ blob: archive, name: 'Folder_7_260928030509.zip' }, expect.anything());
    } finally {
      vi.useRealTimers();
    }
  });

  /** Opens the viewer from the file at `index` and answers what the overlay was opened with. */
  function openViewer(index: number): { model: unknown; source: FileViewerSource; ref: OverlayRef } {
    const open = vi.spyOn(TestBed.inject(OverlayService), 'open');
    (host().querySelectorAll('[data-file-open]')[index] as HTMLElement).dispatchEvent(new MouseEvent('click'));
    expect(open).toHaveBeenCalledTimes(1);
    const [model, anchor, injector] = open.mock.calls[0];
    expect(anchor).toBeNull();
    return {
      model: model.layout?.root.children?.[0]?.model,
      source: (injector as NonNullable<typeof injector>).get(FILE_VIEWER_SOURCE),
      ref: open.mock.results[0].value as OverlayRef,
    };
  }

  it('opens the file viewer at a file over the files of the element', async () => {
    mount();
    await answerPage();

    const { model, source, ref } = openViewer(1);

    expect(model).toEqual({ kind: 'FileViewerOverlay', key: 2 });
    expect(source.files().map(file => file.name)).toEqual(['logo.png', 'plan.pdf']);
    expect(source.canDelete()).toBe(true);
    source.refresh();
    await settle();
    await answerPage();
    await ref.dismiss();
  });

  it("deletes a file from the viewer after the user confirms, through the file grid's delete", async () => {
    mount();
    await answerPage();
    const { source, ref } = openViewer(0);

    const deleted = source.delete(source.files()[1]);
    await settle();
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'Delete the file plan.pdf?', color: 'red' }));
    const request = http.expectOne(`/api/${service}/delete`);
    expect(request.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, selectedIds: [2], selectedVersions: [1] });
    request.flush({ deleted: 1, outcome: 'committed' });

    expect(await deleted).toBe(true);
    await settle();
    await answerPage([rows[0]]);
    expect(source.files().map(file => file.name)).toEqual(['logo.png']);
    await ref.dismiss();
  });

  it('offers no delete in the viewer to a reader, and a declined delete deletes nothing', async () => {
    mount({ ...files, canDelete: false });
    await answerPage();
    const { source, ref } = openViewer(0);

    expect(source.canDelete()).toBe(false);
    expect(await source.delete(source.files()[0])).toBe(false);
    http.expectNone(`/api/${service}/delete`);
    await ref.dismiss();
  });

  it('gives the focus to the first file left once the viewer closes, when the file that opened it is gone', async () => {
    mount();
    await answerPage();
    const opener = host().querySelectorAll<HTMLElement>('[data-file-open]')[1];
    opener.focus();
    const { source, ref } = openViewer(1);
    opener.blur();

    // The file that opened the viewer is deleted from it
    source.refresh();
    await settle();
    await answerPage([rows[0]]);
    await ref.dismiss();
    await settle();

    expect(host().ownerDocument.activeElement).toBe(host().querySelector('[data-file-open]'));
  });

  it("makes a file the record's cover by its pin, and clears the cover by the cover's pin", async () => {
    mount({ ...files, canSetCover: true });
    await answerPage([{ ...rows[0], isCover: true }, rows[1]]);

    const pins = [...host().querySelectorAll<HTMLElement>('[data-file-cover]')];
    expect(pins.map(pin => pin.getAttribute('aria-pressed'))).toEqual(['true', 'false']);
    expect(pins.map(pin => pin.getAttribute('aria-label'))).toEqual(['Cover', 'Cover']);
    expect(pins.map(pin => pin.getAttribute('title'))).toEqual(['Cover', 'Make cover']);

    pins[1].dispatchEvent(new MouseEvent('click'));
    await settle();
    const set = http.expectOne(`/api/${service}/set-cover`);
    expect(set.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, selectedIds: [2], selectedVersions: [1] });
    set.flush({ count: 2, outcome: 'committed' });
    await settle();
    await answerPage([rows[0], { ...rows[1], isCover: true }]);
    expect(host().querySelectorAll('[data-file-cover]')[1].getAttribute('aria-pressed')).toBe('true');

    (host().querySelectorAll('[data-file-cover]')[1] as HTMLElement).dispatchEvent(new MouseEvent('click'));
    await settle();
    const clear = http.expectOne(`/api/${service}/set-cover`);
    expect(clear.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, selectedIds: [] });
    clear.flush({ count: 1, outcome: 'committed' });
    await settle();
    await answerPage();
  });

  it("reports a refused pin with the server's reason and asks the page again", async () => {
    mount({ ...files, canSetCover: true });
    await answerPage();

    (host().querySelector('[data-file-cover]') as HTMLElement).dispatchEvent(new MouseEvent('click'));
    await settle();
    http
      .expectOne(`/api/${service}/set-cover`)
      .flush(
        { title: 'Not changed', outcome: 'refused', messages: [{ type: 'error', code: 'x', title: 'The file is gone.' }] },
        { status: 404, statusText: 'Not Found' },
      );
    await settle();

    expect(alerts.error).toHaveBeenCalledWith('Not changed', 'The file is gone.');
    await answerPage();
  });

  it('offers no pin where the model offers no cover', async () => {
    mount();
    await answerPage([{ ...rows[0], isCover: true }]);

    expect(host().querySelector('[data-file-cover]')).toBeNull();
  });

  it('deletes a file after the user confirms, with its version and the record, and asks the page again', async () => {
    mount();
    await answerPage();

    buttonNamed('Delete plan.pdf')?.dispatchEvent(new MouseEvent('click'));
    await settle();

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'Delete the file plan.pdf?', color: 'red' }));
    const request = http.expectOne(`/api/${service}/delete`);
    expect(request.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, selectedIds: [2], selectedVersions: [1] });
    request.flush({ deleted: 1, outcome: 'committed' });
    await settle();

    await answerPage([rows[0]]);
    expect(names()).toEqual(['Open logo.png']);
    expect(alerts.error).not.toHaveBeenCalled();
  });

  it('asks to delete the focused file on the Delete key', async () => {
    mount();
    await answerPage();

    host()
      .querySelectorAll('[data-file-open]')[1]
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', bubbles: true }));
    await settle();

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'Delete the file plan.pdf?' }));
    http.expectOne(`/api/${service}/delete`).flush({ deleted: 1, outcome: 'committed' });
    await settle();
    await answerPage([rows[0]]);
  });

  it('deletes every file in one request after a confirmation naming their count', async () => {
    mount();
    await answerPage();

    buttonNamed('Delete all')?.dispatchEvent(new MouseEvent('click'));
    await settle();

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'Delete all 2 files?', color: 'red', typedAnswer: '2' }));
    const request = http.expectOne(`/api/${service}/delete`);
    expect(request.request.body).toEqual({
      parentService: 'sample-folder-form',
      parentId: 7,
      selectedIds: [1, 2],
      selectedVersions: [3, 1],
    });
    request.flush({ deleted: 2, outcome: 'committed' });
    await settle();

    await answerPage([]);
    expect(host().querySelector('[data-files-empty]')).not.toBeNull();
  });

  it('deletes nothing when the user declines', async () => {
    confirm.mockResolvedValueOnce(false);
    mount();
    await answerPage();

    buttonNamed('Delete logo.png')?.dispatchEvent(new MouseEvent('click'));
    await settle();

    http.expectNone(`/api/${service}/delete`);
  });

  it("reports a refused delete with the server's reason", async () => {
    mount();
    await answerPage();

    buttonNamed('Delete logo.png')?.dispatchEvent(new MouseEvent('click'));
    await settle();
    http
      .expectOne(`/api/${service}/delete`)
      .flush(
        { title: 'Not deleted', outcome: 'refused', messages: [{ type: 'error', code: 'x', title: 'The file is in use.' }] },
        { status: 400, statusText: 'Bad Request' },
      );
    await settle();

    expect(alerts.error).toHaveBeenCalledWith('Not deleted', 'The file is in use.');
  });

  it('cancels a file on its way from its tile: a waiting one leaves the queue, a posting one is aborted', async () => {
    mount();
    await answerPage();
    await drop(fileOf('a.png', 'image/png', 100), fileOf('b.pdf', 'application/pdf', 200));
    const [posting] = uploads();
    const cancels = (): HTMLElement[] => [...host().querySelectorAll<HTMLElement>('[data-file] [data-upload-cancel]')];

    expect(cancels().map(cancel => cancel.getAttribute('aria-label'))).toEqual([
      'Cancel the upload of a.png',
      'Cancel the upload of b.pdf',
    ]);
    cancels()[1].dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();
    expect(statuses()).toEqual(['uploading', null, null]);

    cancels()[0].dispatchEvent(new MouseEvent('click'));
    await settle();
    fixture.detectChanges();
    expect(posting.cancelled).toBe(true);
    expect(statuses()).toEqual([null, null]);
    http.match(`/api/${service}/get-data`).forEach(request => request.flush({ data: { rows, page: 1, pageSize: 300, hasMore: false } }));
  });

  it("saves every file of the record from the file grid's download-all under its name and the local moment", async () => {
    mount();
    await answerPage();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 8, 28, 15, 33, 12));
    try {
      buttonNamed('Download all')?.dispatchEvent(new MouseEvent('click'));
      const request = http.expectOne(`/api/${service}/download-all`);
      expect(request.request.method).toBe('POST');
      expect(request.request.responseType).toBe('blob');
      expect(request.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7 });
      const archive = new Blob([new Uint8Array([0x50, 0x4b, 3, 4])], { type: 'application/zip' });
      request.flush(archive, { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''Folder_7.zip" } });
      await settle();

      expect(saved).toHaveBeenCalledWith({ blob: archive, name: 'Folder_7_260928153312.zip' }, expect.anything());
      expect(alerts.error).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it('saves Download all as files.zip with the moment when the answer names no file', async () => {
    mount();
    await answerPage();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 0, 2, 3, 4, 5));
    try {
      buttonNamed('Download all')?.dispatchEvent(new MouseEvent('click'));
      http.expectOne(`/api/${service}/download-all`).flush(new Blob([new Uint8Array([0x50, 0x4b, 3, 4])]));
      await settle();

      expect(saved).toHaveBeenCalledWith(expect.objectContaining({ name: 'files_260102030405.zip' }), expect.anything());
    } finally {
      vi.useRealTimers();
    }
  });

  it('reports a refused Download all with the reason of the server', async () => {
    mount();
    await answerPage();

    buttonNamed('Download all')?.dispatchEvent(new MouseEvent('click'));
    const problem = { title: 'Not downloaded', outcome: 'refused', messages: [{ type: 'error', code: 'file.limit', title: 'Too large.' }] };
    http
      .expectOne(`/api/${service}/download-all`)
      .flush(new Blob([JSON.stringify(problem)], { type: 'application/problem+json' }), { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(saved).not.toHaveBeenCalled();
    expect(alerts.error).toHaveBeenCalledWith('Not downloaded', 'Too large.');
  });

  it('closes its overlay at once while no file is on its way', async () => {
    isInOverlay = true;
    reconfigure();
    mount();
    await answerPage();

    expect(await TestBed.inject(OverlayRef).close()).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('asks before its overlay closes while files are on their way, and cancels them once the user leaves', async () => {
    isInOverlay = true;
    reconfigure();
    const ref = TestBed.inject(OverlayRef);
    mount();
    await answerPage();
    await drop(fileOf('a.png', 'image/png', 100), fileOf('b.pdf', 'application/pdf', 200));
    const [posting] = uploads();

    confirm.mockResolvedValueOnce(false);
    expect(await ref.close()).toBe(false);
    expect(confirm).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'Files are still uploading. Leave and cancel them?', confirmLabel: 'Leave', color: 'red' }),
      expect.any(AbortSignal),
    );
    expect(ref.isClosed).toBe(false);
    expect(posting.cancelled).toBe(false);

    expect(await ref.close()).toBe(true);
    await settle();
    fixture.detectChanges();
    expect(ref.isClosed).toBe(true);
    expect(posting.cancelled).toBe(true);
    expect(statuses()).toEqual([null, null]);
    http.match(`/api/${service}/get-data`).forEach(request => request.flush({ data: { rows, page: 1, pageSize: 300, hasMore: false } }));
  });

  it('holds its routed screen and the page while a file is on its way; a reset of the screen cancels the files', async () => {
    const registered: { owner: Parameters<ScreenLifecycle['registerOwner']>[0] | null } = { owner: null };
    lifecycle = {
      registerOwner: owner => {
        registered.owner = owner;
        return () => (registered.owner = null);
      },
    };
    reconfigure();
    mount();
    await answerPage();
    const leave = (): Promise<boolean | CloseApproval> =>
      Promise.resolve(registered.owner?.closeParticipant?.({}) ?? false) as Promise<boolean | CloseApproval>;

    expect(registered.owner?.needsProtection()).toBe(false);
    expect(await leave()).toBe(true);
    await drop(fileOf('a.pdf', 'application/pdf', 100));
    const [posting] = uploads();

    expect(registered.owner?.needsProtection()).toBe(true);
    confirm.mockResolvedValueOnce(false);
    expect(await leave()).toBe(false);
    expect(posting.cancelled).toBe(false);

    registered.owner?.reset();
    await settle();
    expect(posting.cancelled).toBe(true);
    expect(registered.owner?.needsProtection()).toBe(false);
    http.match(`/api/${service}/get-data`).forEach(request => request.flush({ data: { rows, page: 1, pageSize: 300, hasMore: false } }));
    fixture.destroy();
    expect(registered.owner).toBeNull();
  });

  /** Five files of the record, `a.png` to `e.png`, at version 2. */
  const five = ['a', 'b', 'c', 'd', 'e'].map((name, index) => ({
    id: index + 1,
    ver: 2,
    name: `${name}.png`,
    contentType: 'image/png',
    size: 100,
    url: `/files/${index + 1}?sig=a`,
  }));
  const handleOf = (key: number): HTMLElement => host().querySelector(`[data-reorder-handle="${key}"]`) as HTMLElement;
  const reorderSaid = (): string => host().querySelector('[data-files-reorder-status]')?.textContent?.trim() ?? '';
  const fileNames = (): string[] => names().map(name => name.replace('Open ', ''));

  function key(name: string, target: HTMLElement): void {
    target.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  it("moves a file by the keyboard, announcing its places, and posts the file grid's move before the file it now precedes", async () => {
    mount();
    await answerPage(five);
    const handle = handleOf(3);
    expect(handle.getAttribute('aria-label')).toBe('Move c.png');
    expect(host().querySelector(`#${handle.getAttribute('aria-describedby')}`)?.textContent).toContain('Space takes the file');
    expect(host().querySelector('[data-files-reorder-status]')?.getAttribute('aria-live')).toBe('polite');

    key(' ', handle);
    key('ArrowLeft', handleOf(3));
    expect(reorderSaid()).toBe('c.png, position 2 of 5');
    key('ArrowLeft', handleOf(3));
    expect(reorderSaid()).toBe('c.png, position 1 of 5');
    key(' ', handleOf(3));
    fixture.detectChanges();

    const request = http.expectOne(`/api/${service}/move`);
    expect(request.request.body).toEqual({
      parentService: 'sample-folder-form',
      parentId: 7,
      selectedIds: [3],
      selectedVersions: [2],
      beforeId: 1,
    });
    expect(fileNames()).toEqual(['c.png', 'a.png', 'b.png', 'd.png', 'e.png']);
    // No handle moves another file while the move posts
    expect(host().querySelector('[data-reorder-handle]')).toBeNull();
    request.flush({ count: 1, outcome: 'committed' });
    await settle();
    fixture.detectChanges();
    expect(fileNames()).toEqual(['c.png', 'a.png', 'b.png', 'd.png', 'e.png']);
    await answerPage([five[2], five[0], five[1], five[3], five[4]]);
    expect(fileNames()).toEqual(['c.png', 'a.png', 'b.png', 'd.png', 'e.png']);
    expect(handleOf(1)).not.toBeNull();
  });

  it('drags a file by a finger past the next file and names no file to stand before when it stands last', async () => {
    mount();
    await answerPage(five.slice(0, 2));
    const cells = (): HTMLElement[] => [...host().querySelectorAll<HTMLElement>('[data-reorder-item]')];
    const layOut = (): void =>
      cells().forEach((cell, index) => {
        cell.getBoundingClientRect = () => ({ left: index * 100, right: index * 100 + 99, top: 0, bottom: 99 }) as DOMRect;
      });
    const pointer = (type: string, target: Element, x: number): void => {
      const event = new MouseEvent(type, { bubbles: true, cancelable: true, clientX: x, clientY: 50 });
      Object.defineProperties(event, { pointerId: { value: 1 }, pointerType: { value: 'touch' } });
      target.dispatchEvent(event);
      fixture.detectChanges();
    };
    layOut();
    const list = host().querySelector('ul') as HTMLElement;

    pointer('pointerdown', handleOf(1), 50);
    pointer('pointermove', list, 150);
    expect(fileNames()).toEqual(['b.png', 'a.png']);
    pointer('pointerup', list, 150);

    const request = http.expectOne(`/api/${service}/move`);
    expect(request.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, selectedIds: [1], selectedVersions: [2] });
    request.flush({ count: 1, outcome: 'committed' });
    await settle();
    await answerPage([five[1], five[0]]);
  });

  it('puts a file back and reports a move that fails, then asks the page again', async () => {
    mount();
    await answerPage(five.slice(0, 3));

    key(' ', handleOf(2));
    key('ArrowRight', handleOf(2));
    key(' ', handleOf(2));
    expect(fileNames()).toEqual(['a.png', 'c.png', 'b.png']);
    http
      .expectOne(`/api/${service}/move`)
      .flush({ title: 'The record changed', outcome: 'conflict' }, { status: 409, statusText: 'Conflict' });
    await settle();
    fixture.detectChanges();

    expect(fileNames()).toEqual(['a.png', 'b.png', 'c.png']);
    expect(alerts.error).toHaveBeenCalledWith('The record changed', undefined);
    await answerPage(five.slice(0, 3));
  });

  it('offers no handle to a reader, nor while files post', async () => {
    mount({ ...files, canUpload: false, canDelete: false });
    await answerPage(five);
    expect(host().querySelector('[data-reorder-handle]')).toBeNull();
    expect(host().querySelector('[data-files-reorder-status]')).toBeNull();
    fixture.destroy();
    http.verify();

    mount();
    await answerPage(five.slice(0, 2));
    expect(handleOf(1)).not.toBeNull();
    await drop(fileOf('f.png', 'image/png', 10));
    expect(host().querySelector('[data-reorder-handle]')).toBeNull();
    const [upload] = uploads();
    upload.flush({ count: 1, id: 6, outcome: 'committed' });
    await settle();
    await answerPage(five.slice(0, 3));
    expect(handleOf(1)).not.toBeNull();
  });

  it("gives the viewer the file grid's Info: fields where the upload is offered, saved with the version, the page asked again", async () => {
    mount({ ...files, canReadMetadata: true });
    await answerPage();
    const { source, ref } = openViewer(0);
    const logo = source.files()[0];

    expect(source.canEditInfo()).toBe(true);
    expect(source.canReadMetadata()).toBe(true);
    const info = source.info(logo);
    const asked = http.expectOne(`/api/${service}/get-info`);
    expect(asked.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, id: 1 });
    asked.flush({ id: 1, ver: 3, name: 'logo.png', contentType: 'image/png', size: 100, createdAt: '2026-09-25T10:00:00Z' });
    expect((await info).ver).toBe(3);

    const metadata = source.metadata(logo);
    const metadataAsked = http.expectOne(`/api/${service}/file-metadata`);
    expect(metadataAsked.request.body).toEqual({ parentService: 'sample-folder-form', parentId: 7, id: 1 });
    metadataAsked.flush(null);
    expect(await metadata).toBeNull();

    const saved = source.saveInfo(logo, { name: 'Facade', description: 'North side, morning', ver: 3 });
    const save = http.expectOne(`/api/${service}/set-info`);
    expect(save.request.body).toEqual({
      parentService: 'sample-folder-form',
      parentId: 7,
      selectedIds: [1],
      selectedVersions: [3],
      name: 'Facade',
      description: 'North side, morning',
    });
    save.flush({ count: 1, outcome: 'committed' });
    expect(await saved).toBe(true);
    await settle();
    await answerPage([{ ...rows[0], name: 'Facade.png' }, rows[1]]);
    expect(source.files()[0].name).toBe('Facade.png');
    expect(names()).toEqual(['Open Facade.png', 'Open plan.pdf']);

    const stale = source.saveInfo(source.files()[0], { name: 'Front', description: null, ver: 3 });
    http.expectOne(`/api/${service}/set-info`).flush({ outcome: 'conflict' }, { status: 409, statusText: 'Conflict' });
    expect(await stale).toBe(false);
    expect(alerts.error).toHaveBeenCalledWith('The record changed', undefined);
    await settle();
    await answerPage();
    await ref.dismiss();
  });

  it("gives a reader's viewer Info as text and no metadata without its right", async () => {
    mount({ ...files, canUpload: false, canDelete: false });
    await answerPage();
    const { source, ref } = openViewer(0);

    expect(source.canEditInfo()).toBe(false);
    expect(source.canReadMetadata()).toBe(false);
    expect(await source.saveInfo(source.files()[0], { name: 'x', description: null, ver: 1 })).toBe(false);
    http.expectNone(`/api/${service}/set-info`);
    await ref.dismiss();
  });

  it('passes axe with the handles of its files, while one is carried', async () => {
    mount();
    await answerPage(five.slice(0, 3));
    key(' ', handleOf(2));

    await expectAccessible(host());
  });

  it('passes axe with its tiles and a file on its way', async () => {
    mount();
    await answerPage();
    await drop(fileOf('setup.exe', 'application/x-msdownload', 100), fileOf('a.png', 'image/png', 100));

    await expectAccessible(host());

    uploads()[0].flush({ count: 1, outcome: 'committed' });
    await settle();
    await answerPage();
  });

  it('passes axe with the pins of the cover', async () => {
    mount({ ...files, canSetCover: true });
    await answerPage([{ ...rows[0], isCover: true }, rows[1]]);

    await expectAccessible(host());
  });

  it('passes axe in the list look for a reader', async () => {
    mount({ ...files, look: 'list', canUpload: false, canDelete: false });
    await answerPage();

    await expectAccessible(host());
  });

  it('lays out the bar, the zone with its files and the limits: every right in tiles and in a list, a reader, a new record', async () => {
    const count = (): number => host().querySelectorAll('*').length;
    /** The boxes of the element's section, the classes of each `div` in order: a binding keeps no order of its own. */
    const laidOut = (): string[] =>
      boxesOf(host().querySelector('section') ?? host()).map(text =>
        text.replace(/div((?:\.[^.{]+)+)\{/g, (_, classes: string) => `div.${classes.slice(1).split('.').sort().join('.')}{`),
      );
    /** The classes of the list of the files, in order. */
    const list = (): string[] => [...(host().querySelector('ul')?.classList ?? [])].sort();
    const everyRight: FormFilesModel = { ...files, canSetCover: true, canReadMetadata: true };
    const bar = (...inner: string[]): string => `div.flex.flex-wrap.gap-(--ui-panel-gap).items-center.min-h-9{${inner.join(', ')}}`;
    const all = (...buttons: string[]): string => `div.flex.gap-(--ui-panel-gap).items-center.ml-auto{${buttons.join(', ')}}`;
    const intake =
      'div.bg-field.border.border-field-edge.flex.flex-col.focus-within:border-field-focus.gap-2.hover:border-field-hover' +
      '.min-w-0.outline-none.p-2.rounded-lg{ul, p}';
    const withEveryRight = [
      bar('h3', 'ui-btn', all('ui-btn', 'ui-btn')),
      intake,
      'p',
      'p',
      'div.flex.flex-wrap.gap-x-3.text-muted.text-xs{p, p}',
      'input',
    ];

    mount(everyRight);
    await answerPage();
    expect([...host().classList]).toEqual(['block', 'min-w-0']);
    expect(boxesOf(host())).toEqual(['section']);
    expect(laidOut()).toEqual(withEveryRight);
    expect(list()).toEqual(['gap-2', 'grid', 'grid-cols-[repeat(auto-fill,minmax(8rem,1fr))]', 'list-none', 'm-0', 'p-0']);
    expect(boxesOf(host().querySelector('ul') ?? host())).toEqual(['li', 'li']);
    expect(boxesOf(host().querySelector('li') ?? host())).toEqual(['ui-file-tile']);
    const inTiles = count();
    fixture.destroy();
    reconfigure();

    mount({ ...everyRight, look: 'list' });
    await answerPage();
    expect(laidOut()).toEqual(withEveryRight);
    expect(list()).toEqual(['divide-line', 'divide-y', 'flex', 'flex-col', 'list-none', 'm-0', 'p-0']);
    expect(boxesOf(host().querySelector('ul') ?? host())).toEqual(['li', 'li']);
    const inList = count();
    fixture.destroy();
    reconfigure();

    mount({ ...files, canUpload: false, canDelete: false });
    await answerPage();
    expect(laidOut()).toEqual([bar('h3', all('ui-btn')), 'div.block.min-w-0{ul}']);
    const forReader = count();
    fixture.destroy();
    reconfigure();

    mount({ ...everyRight, parentId: null });
    expect(laidOut()).toEqual([bar('h3', 'ui-btn'), 'p']);

    expect({ inTiles, inList, forReader, forNew: count() }).toEqual({ inTiles: 106, inList: 104, forReader: 41, forNew: 9 });
  });
});

describe('The files in a form', () => {
  let fixture: ComponentFixture<FormComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FormComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideDynamicComponents(UI_COMPONENTS),
        PanelsContext,
        { provide: AlertService, useValue: { open: vi.fn(), error: vi.fn(), warnings: AlertService.prototype.warnings } },
        { provide: ScreenMutations, useValue: new ScreenMutations() },
      ],
    });
    fixture = TestBed.createComponent(FormComponent);
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).match(() => true);
    TestBed.resetTestingModule();
  });

  it('stand after the elements, shown with the tab they name', async () => {
    const model: FormModel = {
      ...formModel([formElement('name'), formElement('note', { tab: 'details' })]),
      tabs: [{ key: 'details', label: 'Details' }],
      files: [{ ...files, tab: 'details' }],
    };
    fixture.componentRef.setInput('model', model);
    fixture.componentRef.setInput('data', { id: 7, ver: 1, value: { name: 'Folder', note: '' } });
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();

    const body = (fixture.nativeElement as HTMLElement).querySelector('[data-form-body] .grid') as HTMLElement;
    const wrapper = body.querySelector('[data-form-files]') as HTMLElement;
    expect(body.lastElementChild).toBe(wrapper);
    expect(wrapper.querySelector('ui-form-files')).not.toBeNull();
    expect(wrapper.hidden).toBe(true);

    fixture.debugElement.injector.get(PanelsContext).of(FORM_TAB).set('details');
    fixture.detectChanges();

    expect(wrapper.hidden).toBe(false);
  });
});

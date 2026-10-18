import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../tests/axe';
import { boxesOf } from '../../tests/boxes';
import { uploadLimits } from '../../tests/upload-limits';
import type { FilesLook } from '../core/generated/contracts';
import { SEGMENTED_GHOST } from '../theme/css-classes';
import { FileTileComponent } from './file-tile.component';
import type { FileUpload, FileUploadStatus } from './file-upload-queue';
import type { FileView } from './file-view';

const photo: FileView = { key: 12, name: 'site.jpg', size: 3000, contentType: 'image/jpeg', url: '/f/12', thumbUrl: '/f/12t' };
const act: FileView = { key: 14, name: 'act.pdf', size: 5000, contentType: 'application/pdf', url: '/f/14', thumbUrl: null };
const pending: FileView = { key: null, name: 'plan.pdf', size: 2000, contentType: 'application/pdf', url: null, thumbUrl: null };

const uploadOf = (status: FileUploadStatus, extra: Partial<FileUpload> = {}): FileUpload => ({
  id: 1,
  file: new File([new Uint8Array(2000)], 'plan.pdf', { type: 'application/pdf' }),
  target: { service: 'sample-report-file-grid', parentId: 7, ...uploadLimits() },
  batch: 1,
  status,
  progress: 0,
  ...extra,
});

/** The classes of an element or a class string, in order: the binding keeps no order of its own. */
const classesOf = (from: Element | string | null | undefined): string[] =>
  (typeof from === 'string' ? from.split(' ') : [...(from?.classList ?? [])]).sort();

/** A `div` as `boxesOf` writes it, its classes in order: the classes and, in braces, its own boxes. */
const box = (classes: string, ...inner: string[]): string => `div.${classesOf(classes).join('.')}{${inner.join(', ')}}`;

/** The boxes of `parent` with the classes of each `div` in order, as `box` writes them. */
const laidOut = (parent: Element): string[] =>
  boxesOf(parent).map(text =>
    text.replace(/div((?:\.[^.{]+)+)\{/g, (_, classes: string) => `div.${classes.slice(1).split('.').sort().join('.')}{`),
  );

const FRAME = {
  tiles: 'flex flex-col h-full min-w-0 rounded-lg border border-line bg-surface overflow-hidden',
  list: 'flex items-center gap-2 min-w-0 min-h-11 px-1',
} satisfies Record<FilesLook, string>;
const MOVING_FRAME = {
  tiles: 'flex flex-col h-full min-w-0 rounded-lg border border-checked bg-surface overflow-hidden shadow-lg',
  list: 'flex items-center gap-2 min-w-0 min-h-11 px-1 rounded-lg border border-checked bg-surface shadow-lg',
} satisfies Record<FilesLook, string>;
const BLOCK = {
  tiles: `${SEGMENTED_GHOST} mt-auto border-t border-line`,
  list: `${SEGMENTED_GHOST} flex-none`,
} satisfies Record<FilesLook, string>;
const CAPTION_ROW = {
  tiles: 'flex flex-col min-w-0',
  list: 'flex flex-1 items-center gap-2 min-w-0 px-1',
} satisfies Record<FilesLook, string>;
const PICTURE = {
  tiles: 'flex items-center justify-center aspect-4/3 w-full bg-surface-2 overflow-hidden',
  list: 'flex flex-none items-center justify-center size-9 rounded-md bg-surface-2 overflow-hidden',
} satisfies Record<FilesLook, string>;
const STATUS = {
  tiles: 'flex flex-col gap-1 min-w-0 px-2',
  list: 'flex flex-col gap-1 min-w-0 px-2 flex-1',
} satisfies Record<FilesLook, string>;
const UPLOAD_ACTIONS = {
  tiles: 'flex items-center gap-1 mt-auto p-1',
  list: 'flex flex-none items-center gap-1',
} satisfies Record<FilesLook, string>;
const PROGRESS = box('h-1 rounded-full bg-line overflow-hidden', box('h-full bg-checked'));

describe('FileTileComponent', () => {
  let fixture: ComponentFixture<FileTileComponent>;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const one = (selector: string): HTMLElement => host().querySelector(selector) as HTMLElement;
  const count = (): number => host().querySelectorAll('*').length;
  const frame = (): HTMLElement => host().firstElementChild as HTMLElement;

  function mount(inputs: Record<string, unknown>): void {
    fixture = TestBed.createComponent(FileTileComponent);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  /** Every right of a file of the page: its handle, its pin and its delete. */
  const every = { movable: true, coverable: true, deletable: true, moveHint: 'hint' };

  afterEach(() => {
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('draws a file of the page as a tile: the picture, the name without its extension, the title with the size', () => {
    mount({ file: photo });

    expect([...host().classList]).toEqual(['block', 'min-w-0']);
    expect(frame().getAttribute('title')).toBe('site.jpg\n2.9 kB');
    expect(frame().hasAttribute('data-status')).toBe(false);
    const open = one('[data-file-open]');
    expect(open.getAttribute('aria-label')).toBe('Open site.jpg');
    expect(open.querySelector('img')?.getAttribute('src')).toBe('/f/12t');
    expect(open.querySelector('img')?.getAttribute('alt')).toBe('');
    const name = one('[data-file-name]');
    expect(name.querySelector('[aria-hidden="true"]')?.textContent).toBe('site');
    expect(name.querySelector('.sr-only')?.textContent).toBe('site.jpg');
    expect(host().textContent).not.toContain('2.9 kB');
  });

  it('draws a file of the page as a line of the list: the name with its extension and the size beside it, no title', () => {
    mount({ file: act, look: 'list' });

    expect(frame().hasAttribute('title')).toBe(false);
    const name = one('[data-file-name]');
    expect(name.textContent).toBe('act.pdf');
    expect(name.getAttribute('title')).toBe('act.pdf');
    expect(name.nextElementSibling?.textContent).toBe('4.9 kB');
    expect(one('[data-file-open]').querySelector('img')).toBeNull();
    expect(one('[data-file-open] [aria-hidden="true"]').textContent).toBe('PDF');
  });

  it('falls back to the badge when the picture fails to load', () => {
    mount({ file: photo });

    one('[data-file-open] img').dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(host().querySelector('img')).toBeNull();
    expect(one('[data-file-open] [aria-hidden="true"]').textContent).toBe('JPG');
  });

  it('draws the row of actions by the inputs: the handle, the pin, Download and Delete, in that order', () => {
    mount({ file: photo, ...every });

    const actions = [...one('[data-file-actions]').children];
    expect(actions.map(action => action.getAttribute('aria-label'))).toEqual(['Move site.jpg', 'Cover', 'Download', 'Delete site.jpg']);
    const [handle, pin, download, remove] = actions;
    expect(handle.getAttribute('data-reorder-handle')).toBe('12');
    expect(handle.getAttribute('aria-describedby')).toBe('hint');
    expect(pin.getAttribute('aria-pressed')).toBe('false');
    expect(pin.getAttribute('title')).toBe('Make cover');
    expect(pin.getAttribute('aria-describedby')).toBe(one('[data-file-name]').id);
    expect(download.getAttribute('href')).toBe('/f/12?download');
    expect(download.getAttribute('download')).toBe('site.jpg');
    expect(remove.hasAttribute('aria-busy')).toBe(false);
  });

  it('draws only the actions the file offers, and no row for a file that offers none', () => {
    mount({ file: photo });
    expect([...one('[data-file-actions]').children].map(action => action.getAttribute('aria-label'))).toEqual(['Download']);

    fixture.componentRef.setInput('file', { ...photo, url: null });
    fixture.detectChanges();
    expect(host().querySelector('[data-file-actions]')).toBeNull();

    fixture.componentRef.setInput('file', { ...photo, key: null, url: null });
    fixture.componentRef.setInput('movable', true);
    fixture.detectChanges();
    expect(host().querySelector('[data-file-actions]')).toBeNull();
  });

  it("draws the pin pressed on the record's cover, and the pin and Delete busy while their requests post", () => {
    mount({ file: photo, ...every, isCover: true });
    const pin = one('[data-file-cover]');
    const remove = one('[data-file-delete]');
    expect(pin.getAttribute('aria-pressed')).toBe('true');
    expect(pin.getAttribute('title')).toBe('Cover');
    expect(pin.classList).toContain('text-checked!');
    expect(pin.hasAttribute('aria-busy')).toBe(false);

    fixture.componentRef.setInput('settingCover', true);
    fixture.componentRef.setInput('deleting', true);
    fixture.detectChanges();

    for (const busy of [pin, remove]) {
      expect(busy.getAttribute('aria-busy')).toBe('true');
      expect(busy.getAttribute('aria-disabled')).toBe('true');
      expect(busy.classList).toContain('opacity-50');
      expect(busy.classList).toContain('cursor-not-allowed!');
    }
  });

  it('draws a file being moved lifted, in both looks', () => {
    mount({ file: photo, moving: true });
    expect(classesOf(frame())).toEqual(classesOf(MOVING_FRAME.tiles));

    fixture.componentRef.setInput('look', 'list');
    fixture.detectChanges();
    expect(classesOf(frame())).toEqual(classesOf(MOVING_FRAME.list));

    fixture.componentRef.setInput('moving', false);
    fixture.detectChanges();
    expect(classesOf(frame())).toEqual(classesOf(FRAME.list));
  });

  it('emits opened by its button, deleteRequested by Delete and by the Delete key, coverRequested by the pin', () => {
    mount({ file: photo, ...every });
    const opened = vi.fn();
    const deleteRequested = vi.fn();
    const coverRequested = vi.fn();
    fixture.componentInstance.opened.subscribe(opened);
    fixture.componentInstance.deleteRequested.subscribe(deleteRequested);
    fixture.componentInstance.coverRequested.subscribe(coverRequested);

    one('[data-file-open]').click();
    expect(opened).toHaveBeenCalledTimes(1);

    one('[data-file-delete]').click();
    expect(deleteRequested).toHaveBeenCalledTimes(1);
    const key = new KeyboardEvent('keydown', { key: 'Delete', bubbles: true, cancelable: true });
    one('[data-file-open]').dispatchEvent(key);
    expect(deleteRequested).toHaveBeenCalledTimes(2);
    expect(key.defaultPrevented).toBe(true);

    one('[data-file-cover]').click();
    expect(coverRequested).toHaveBeenCalledTimes(1);
    expect(opened).toHaveBeenCalledTimes(1);
  });

  it('emits no delete while one is posting or where the file offers none, and no pin while one is posting', () => {
    mount({ file: photo, ...every, deleting: true, settingCover: true });
    const deleteRequested = vi.fn();
    const coverRequested = vi.fn();
    fixture.componentInstance.deleteRequested.subscribe(deleteRequested);
    fixture.componentInstance.coverRequested.subscribe(coverRequested);
    const press = (): KeyboardEvent => {
      const key = new KeyboardEvent('keydown', { key: 'Delete', bubbles: true, cancelable: true });
      one('[data-file-open]').dispatchEvent(key);
      return key;
    };

    one('[data-file-delete]').click();
    expect(press().defaultPrevented).toBe(false);
    one('[data-file-cover]').click();

    fixture.componentRef.setInput('deleting', false);
    fixture.componentRef.setInput('deletable', false);
    fixture.detectChanges();
    expect(host().querySelector('[data-file-delete]')).toBeNull();
    expect(press().defaultPrevented).toBe(false);

    expect(deleteRequested).not.toHaveBeenCalled();
    expect(coverRequested).not.toHaveBeenCalled();
  });

  it('draws a file on its way with its status in a live region and no button to open it, in both looks', () => {
    mount({ file: pending, upload: uploadOf('waiting') });

    expect(frame().getAttribute('data-status')).toBe('waiting');
    expect(host().querySelector('[data-file-open]')).toBeNull();
    const status = one('[data-upload-status]');
    expect(status.getAttribute('role')).toBe('status');
    expect(status.textContent?.trim()).toBe('Waiting');
    expect(status.classList).toContain('text-muted');
    expect(host().querySelector('[role="progressbar"]')).toBeNull();
    expect(host().querySelector('ui-btn')).toBeNull();
    expect(one('[data-file-name] [aria-hidden="true"]').textContent).toBe('plan');
    expect(classesOf(frame().firstElementChild)).toEqual(classesOf(CAPTION_ROW.tiles));

    fixture.componentRef.setInput('look', 'list');
    fixture.detectChanges();
    expect(one('[data-file-name]').textContent).toBe('plan.pdf');
    expect(classesOf(frame().firstElementChild)).toEqual(classesOf(CAPTION_ROW.list));
  });

  it('shows the progress of a posting file, named by the file, and Cancel where it is cancelable', () => {
    mount({ file: pending, upload: uploadOf('uploading', { progress: 40 }), cancelable: true });
    const cancelRequested = vi.fn();
    fixture.componentInstance.cancelRequested.subscribe(cancelRequested);

    const bar = one('[role="progressbar"]');
    expect(bar.getAttribute('aria-valuenow')).toBe('40');
    expect(bar.getAttribute('aria-labelledby')).toBe(one('[data-file-name]').id);
    expect((bar.firstElementChild as HTMLElement).style.width).toBe('40%');
    expect(one('[data-upload-status]').textContent?.trim()).toBe('Uploading…');
    const cancel = one('[data-upload-cancel]');
    expect(cancel.getAttribute('aria-label')).toBe('Cancel the upload of plan.pdf');

    cancel.click();
    expect(cancelRequested).toHaveBeenCalledTimes(1);
  });

  it('offers Try again and Remove for a failed file with its reason, Remove alone for a refused one', () => {
    mount({ file: pending, upload: uploadOf('failed', { detail: 'The record is full.' }), cancelable: true });
    const retryRequested = vi.fn();
    const removeRequested = vi.fn();
    fixture.componentInstance.retryRequested.subscribe(retryRequested);
    fixture.componentInstance.removeRequested.subscribe(removeRequested);
    const buttons = (): HTMLElement[] => [...host().querySelectorAll<HTMLElement>('ui-btn')];

    const status = one('[data-upload-status]');
    expect(status.textContent?.replace(/\s+/g, ' ').trim()).toBe('Not uploaded The record is full.');
    expect(status.classList).toContain('hue-red');
    expect(buttons().map(button => button.getAttribute('aria-label') ?? button.getAttribute('title'))).toEqual(['Try again', 'Remove']);
    expect(buttons().map(button => button.getAttribute('aria-describedby'))).toEqual(Array(2).fill(one('[data-file-name]').id));
    buttons()[0].click();
    buttons()[1].click();
    expect(retryRequested).toHaveBeenCalledTimes(1);
    expect(removeRequested).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('upload', uploadOf('refused'));
    fixture.detectChanges();
    expect(frame().getAttribute('data-status')).toBe('refused');
    expect(one('[data-upload-status]').textContent?.trim()).toBe('The file is refused.');
    expect(buttons()).toHaveLength(1);
    buttons()[0].click();
    expect(removeRequested).toHaveBeenCalledTimes(2);
    expect(retryRequested).toHaveBeenCalledTimes(1);
  });

  it('lays out a file of the page: the open button, then the row of its actions, in both looks', () => {
    mount({ file: photo, ...every });
    expect(laidOut(host())).toEqual([box(FRAME.tiles, 'button', box(BLOCK.tiles, 'button', 'button', 'a', 'button'))]);
    expect(count()).toBe(35);

    fixture.componentRef.setInput('look', 'list');
    fixture.detectChanges();
    expect(laidOut(host())).toEqual([box(FRAME.list, 'button', box(BLOCK.list, 'button', 'button', 'a', 'button'))]);
    expect(count()).toBe(34);

    fixture.componentRef.setInput('moving', true);
    fixture.detectChanges();
    expect(laidOut(host())).toEqual([box(MOVING_FRAME.list, 'button', box(BLOCK.list, 'button', 'button', 'a', 'button'))]);
    expect(count()).toBe(34);
  });

  it('lays out a file of the page that offers no action as its open button alone', () => {
    mount({ file: { ...act, url: null } });

    expect(laidOut(host())).toEqual([box(FRAME.tiles, 'button')]);
    expect(count()).toBe(8);
  });

  it('lays out a file on its way: the picture with the caption, the status, then its actions, in both looks', () => {
    mount({ file: pending, upload: uploadOf('uploading', { progress: 40 }), cancelable: true });
    expect(laidOut(host())).toEqual([
      box(
        FRAME.tiles,
        box(CAPTION_ROW.tiles, box(PICTURE.tiles, 'span'), 'span'),
        box(STATUS.tiles, PROGRESS, 'p'),
        box(UPLOAD_ACTIONS.tiles, 'ui-btn'),
      ),
    ]);
    const posting = count();

    fixture.componentRef.setInput('look', 'list');
    fixture.detectChanges();
    expect(laidOut(host())).toEqual([
      box(
        FRAME.list,
        box(CAPTION_ROW.list, box(PICTURE.list, 'span'), 'span'),
        box(STATUS.list, PROGRESS, 'p'),
        box(UPLOAD_ACTIONS.list, 'ui-btn'),
      ),
    ]);
    expect([posting, count()]).toEqual([18, 17]);

    fixture.componentRef.setInput('upload', uploadOf('failed', { detail: 'The record is full.' }));
    fixture.detectChanges();
    expect(laidOut(host())).toEqual([
      box(
        FRAME.list,
        box(CAPTION_ROW.list, box(PICTURE.list, 'span'), 'span'),
        box(STATUS.list, 'p'),
        box(UPLOAD_ACTIONS.list, 'ui-btn', 'ui-btn'),
      ),
    ]);
    expect(count()).toBe(20);

    fixture.componentRef.setInput('upload', uploadOf('uploaded', { progress: 100 }));
    fixture.detectChanges();
    expect(laidOut(host())).toEqual([box(FRAME.list, box(CAPTION_ROW.list, box(PICTURE.list, 'span'), 'span'), box(STATUS.list, 'p'))]);
    expect(count()).toBe(9);
  });

  it('passes axe on a file of the page with every action, in both looks', async () => {
    mount({ file: photo, ...every, isCover: true });
    const hint = document.createElement('p');
    hint.id = 'hint';
    hint.textContent = 'Arrows move the file.';
    host().append(hint);
    await expectAccessible(host());

    fixture.componentRef.setInput('look', 'list');
    fixture.detectChanges();
    await expectAccessible(host());
  });

  it('passes axe on a file on its way, posting and failed', async () => {
    mount({ file: pending, upload: uploadOf('uploading', { progress: 40 }), cancelable: true });
    await expectAccessible(host());

    fixture.componentRef.setInput('upload', uploadOf('failed', { detail: 'The record is full.' }));
    fixture.detectChanges();
    await expectAccessible(host());
  });
});

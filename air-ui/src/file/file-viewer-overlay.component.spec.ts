import { signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../tests/axe';
import { boxesOf } from '../../tests/boxes';
import { userProfile } from '../../tests/user-profile';
import { AuthService } from '../app/auth/auth.service';
import { SLATE_GHOST_ICON_BTN_CLASSES, SLATE_SOFT_BTN_CLASSES } from '../btn/btn.component';
import type { FileInfoDto, FileMetadataDto, OverlayDto } from '../core/generated/contracts';
import { OverlayRef } from '../overlay/overlay-ref';
import type { FileView } from './file-view';
import { FILE_VIEWER_OVERLAY, FILE_VIEWER_SOURCE, type FileInfoChange, fileViewerOf, type FileViewerSource } from './file-viewer';
import { FileViewerOverlayComponent } from './file-viewer-overlay.component';

/** The classes of an element or a class string, in order: the binding keeps no order of its own. */
const classesOf = (from: Element | string | null | undefined): string[] =>
  (typeof from === 'string' ? from.split(' ') : [...(from?.classList ?? [])]).sort();

const image = (key: number, name: string, extra: Partial<FileView> = {}): FileView => ({
  key,
  name,
  size: 1000 * key,
  contentType: 'image/png',
  url: `/files/${key}?sig=a`,
  thumbUrl: `/files/${key}t?sig=a`,
  ...extra,
});

const pdf: FileView = { key: 4, name: 'act.pdf', size: 20000, contentType: 'application/pdf', url: '/files/4?sig=a', thumbUrl: null };

const fiveFiles: readonly FileView[] = [image(1, 'a.png'), image(2, 'b.png'), image(3, 'c.png'), pdf, image(5, 'e.png')];

/** What `get-info` answers of a file: its row's facts, a photo's size in pixels, its uploader and a description. */
const infoOf = (file: FileView): FileInfoDto => ({
  id: file.key ?? 0,
  ver: 3,
  name: file.name,
  contentType: file.contentType,
  size: file.size ?? 0,
  width: 4032,
  height: 3024,
  createdAt: '2026-09-25T10:00:00Z',
  uploadedBy: 'Olena',
  description: 'North side, morning',
});

const clip = image(6, 'clip.mp4', { contentType: 'video/mp4' });
const withClip: readonly FileView[] = [image(1, 'a.png'), clip];

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i++) {
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
  }
}

describe('FileViewerOverlayComponent', () => {
  let fixture: ComponentFixture<FileViewerOverlayComponent>;
  let source: FileViewerSource & { files: ReturnType<typeof signal<readonly FileView[]>> };
  let deleteFile: ReturnType<typeof vi.fn<(file: FileView) => Promise<boolean>>>;
  let refresh: ReturnType<typeof vi.fn<() => void>>;
  let info: ReturnType<typeof vi.fn<(file: FileView) => Promise<FileInfoDto>>>;
  let saveInfo: ReturnType<typeof vi.fn<(file: FileView, change: FileInfoChange) => Promise<boolean>>>;
  let metadata: ReturnType<typeof vi.fn<(file: FileView) => Promise<FileMetadataDto | null>>>;
  const ref = { name: vi.fn(), dismiss: vi.fn(() => Promise.resolve(true)) };

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const counter = (): string => host().querySelector('[data-file-counter]')?.textContent?.trim() ?? '';
  const shownName = (): string => host().querySelector('[data-file-name]')?.textContent?.trim() ?? '';
  const button = (marker: string): HTMLElement => host().querySelector(`[${marker}]`) as HTMLElement;

  function mount(
    key: number,
    files: readonly FileView[] = fiveFiles,
    canDelete = false,
    { canEditInfo = false, canReadMetadata = false, timeZone = null as string | null } = {},
  ): void {
    source = {
      files: signal(files),
      canDelete: signal(canDelete),
      isRefreshing: signal(false),
      delete: deleteFile,
      refresh,
      canEditInfo: signal(canEditInfo),
      canReadMetadata: signal(canReadMetadata),
      info,
      saveInfo,
      metadata,
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: FILE_VIEWER_SOURCE, useValue: source },
        { provide: OverlayRef, useValue: ref },
      ],
    });
    if (timeZone) {
      TestBed.inject(AuthService).setUser(userProfile(1, {}, timeZone));
    }
    fixture = TestBed.createComponent(FileViewerOverlayComponent);
    fixture.componentRef.setInput('model', { kind: 'FileViewerOverlay', key });
    fixture.detectChanges();
  }

  function press(key: string): void {
    button('data-file-stage').dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  beforeEach(() => {
    deleteFile = vi.fn<(file: FileView) => Promise<boolean>>(() => Promise.resolve(true));
    refresh = vi.fn<() => void>();
    info = vi.fn<(file: FileView) => Promise<FileInfoDto>>(file => Promise.resolve(infoOf(file)));
    saveInfo = vi.fn<(file: FileView, change: FileInfoChange) => Promise<boolean>>(() => Promise.resolve(true));
    metadata = vi.fn<(file: FileView) => Promise<FileMetadataDto | null>>(() => Promise.resolve(null));
    ref.name.mockClear();
    ref.dismiss.mockClear();
  });
  afterEach(() => {
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it('is a modal dialog over the whole window whose one panel fills the card', () => {
    mount(2);
    const model: OverlayDto = fileViewerOf(2);

    expect(model.key).toBe(FILE_VIEWER_OVERLAY);
    expect(model.presentation).toBe('dialog');
    expect(model.width).toBe('1fr');
    expect(model.height).toBe('1fr');
    expect(model.layout?.root.children?.[0]).toEqual({ key: 'body', model: { kind: 'FileViewerOverlay', key: 2 } });
  });

  it('opens at the file it is given, named with its size and its place in a polite live region', () => {
    mount(2);

    expect(ref.name).toHaveBeenCalledWith({ label: 'Files' });
    expect(shownName()).toBe('b.png');
    expect(host().querySelector('[data-file-size]')?.textContent).toContain('2');
    expect(counter()).toBe('2 / 5');
    const live = button('data-file-counter');
    expect(live.getAttribute('role')).toBe('status');
    expect(live.getAttribute('aria-live')).toBe('polite');
    const img = host().querySelector('[data-file-image]');
    expect(img?.getAttribute('src')).toBe('/files/2?sig=a');
    expect(img?.getAttribute('alt')).toBe('b.png');
  });

  it('walks the files by Right, End, Home and Left, and stops at either end', () => {
    mount(2);

    press('ArrowRight');
    expect(counter()).toBe('3 / 5');
    press('End');
    expect(counter()).toBe('5 / 5');
    expect(shownName()).toBe('e.png');
    press('ArrowRight');
    expect(counter()).toBe('5 / 5');
    press('Home');
    expect(counter()).toBe('1 / 5');
    press('ArrowLeft');
    expect(counter()).toBe('1 / 5');
    expect(button('data-file-previous').getAttribute('aria-disabled')).toBe('true');
  });

  it("moves the focus with the shown file's thumbnail while the focus is in the strip, each the strip's one Tab stop", async () => {
    mount(2);
    const thumbs = (): HTMLElement[] => [...host().querySelectorAll<HTMLElement>('[data-file-thumb]')];
    const inStrip = async (key: string, expected: number): Promise<void> => {
      (document.activeElement as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
      await settle();
      expect(document.activeElement).toBe(thumbs()[expected]);
      expect(thumbs().filter(thumb => thumb.tabIndex === 0)).toEqual([thumbs()[expected]]);
    };

    thumbs()[1].focus();
    await inStrip('ArrowRight', 2);
    await inStrip('End', 4);
    await inStrip('Home', 0);
  });

  it('keeps the focus on the stage as the files are walked, the thumbnail of the shown file the Tab stop', async () => {
    mount(2);
    const stage = button('data-file-stage');
    stage.focus();

    press('ArrowRight');
    await settle();

    expect(counter()).toBe('3 / 5');
    expect(document.activeElement).toBe(stage);
    const thumbs = [...host().querySelectorAll<HTMLElement>('[data-file-thumb]')];
    expect(thumbs.filter(thumb => thumb.tabIndex === 0)).toEqual([thumbs[2]]);
  });

  it('shows the previous and the next file by its buttons and any file by its thumbnail in the strip', () => {
    mount(2);

    button('data-file-next').click();
    fixture.detectChanges();
    expect(counter()).toBe('3 / 5');
    button('data-file-previous').click();
    fixture.detectChanges();
    expect(counter()).toBe('2 / 5');

    const thumbs = [...host().querySelectorAll<HTMLElement>('[data-file-thumb]')];
    expect(thumbs.map(thumb => thumb.getAttribute('aria-label'))).toEqual([
      'Open a.png',
      'Open b.png',
      'Open c.png',
      'Open act.pdf',
      'Open e.png',
    ]);
    expect(thumbs[1].getAttribute('aria-current')).toBe('true');
    expect(thumbs[1].tabIndex).toBe(0);
    expect(thumbs[0].tabIndex).toBe(-1);
    expect(thumbs[3].querySelector('img')).toBeNull();
    expect(thumbs[3].textContent?.trim()).toBe('PDF');

    thumbs[4].click();
    fixture.detectChanges();
    expect(counter()).toBe('5 / 5');
    expect(thumbs[4].getAttribute('aria-current')).toBe('true');
  });

  it('shows a PDF as its card with Open and Download, and draws no document inside it', () => {
    mount(4);

    const card = button('data-file-card');
    expect(card.textContent).toContain('PDF');
    const open = card.querySelector('[data-file-open-tab]');
    expect(open?.getAttribute('href')).toBe('/files/4?sig=a');
    expect(open?.getAttribute('target')).toBe('_blank');
    expect(open?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(open?.textContent).toContain('Open');
    expect(classesOf(open)).toEqual(classesOf(SLATE_SOFT_BTN_CLASSES));
    expect(classesOf(card.querySelector('[data-file-card-download]'))).toEqual(classesOf(SLATE_SOFT_BTN_CLASSES));
    expect(card.querySelector('[data-file-card-download]')?.getAttribute('download')).toBe('act.pdf');
    expect(card.querySelector('[data-file-card-download]')?.getAttribute('href')).toBe('/files/4?sig=a&download');
    expect(host().querySelector('iframe, object, embed, [data-file-image]')).toBeNull();
    expect(host().querySelector('[data-file-fit]')).toBeNull();
  });

  it('fits an image to the stage, or shows it at its natural size', () => {
    mount(1);
    const img = (): Element | null => host().querySelector('[data-file-image]');

    expect(button('data-file-fit').getAttribute('aria-pressed')).toBe('true');
    expect(img()?.classList).toContain('object-contain');

    button('data-file-actual').click();
    fixture.detectChanges();

    expect(button('data-file-actual').getAttribute('aria-pressed')).toBe('true');
    expect(button('data-file-fit').getAttribute('aria-pressed')).toBe('false');
    expect(img()?.classList).toContain('max-w-none');
  });

  it('downloads the shown file under its name by its link with the query download', () => {
    mount(3);

    const download = button('data-file-download');
    expect(download.getAttribute('href')).toBe('/files/3?sig=a&download');
    expect(download.getAttribute('download')).toBe('c.png');
    expect(download.getAttribute('aria-label')).toBe('Download');
    expect(classesOf(download)).toEqual(classesOf(SLATE_GHOST_ICON_BTN_CLASSES));
  });

  it('offers no delete where the opener offers none', () => {
    mount(3);

    expect(host().querySelector('[data-file-delete]')).toBeNull();
  });

  it('spaces the counter and the buttons one unit apart as a nav, Delete in the neutral colour', () => {
    mount(3, fiveFiles, true);

    const bar = host().querySelector('[data-file-bar]');
    expect(bar?.classList).toContain('gap-(--ui-panel-gap)');
    for (const name of ['data-file-counter', 'data-file-download', 'data-file-delete', 'data-file-info', 'data-file-close']) {
      expect(bar?.querySelector(`[${name}]`), name).not.toBeNull();
    }
    expect(button('data-file-delete').classList).toContain('hue-slate');
    expect(button('data-file-delete').classList).not.toContain('hue-red');
  });

  it('deletes the shown file through its opener, then shows the next one', async () => {
    mount(3, fiveFiles, true);

    button('data-file-delete').click();
    await settle();
    fixture.detectChanges();

    expect(deleteFile).toHaveBeenCalledWith(fiveFiles[2]);
    expect(counter()).toBe('3 / 4');
    expect(shownName()).toBe('act.pdf');

    // The opener's page says so too: nothing moves
    source.files.set(fiveFiles.filter(file => file.key !== 3));
    fixture.detectChanges();
    expect(counter()).toBe('3 / 4');
  });

  it('shows the file before the last one deleted, keeps a file the user did not delete, and closes when none is left', async () => {
    mount(2, [image(1, 'a.png'), image(2, 'b.png')], true);

    button('data-file-delete').click();
    await settle();
    fixture.detectChanges();
    expect(shownName()).toBe('a.png');
    expect(counter()).toBe('1 / 1');

    deleteFile.mockResolvedValueOnce(false);
    button('data-file-delete').click();
    await settle();
    fixture.detectChanges();
    expect(shownName()).toBe('a.png');
    expect(ref.dismiss).not.toHaveBeenCalled();

    button('data-file-delete').click();
    await settle();
    expect(ref.dismiss).toHaveBeenCalled();
  });

  it('asks the page again once when an image fails to load, and shows the fresh link', () => {
    mount(1);

    host().querySelector('[data-file-image]')?.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(refresh).toHaveBeenCalledTimes(1);

    source.files.set(fiveFiles.map(file => (file.key === 1 ? { ...file, url: '/files/1?sig=fresh' } : file)));
    fixture.detectChanges();
    expect(host().querySelector('[data-file-image]')?.getAttribute('src')).toBe('/files/1?sig=fresh');

    host().querySelector('[data-file-image]')?.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(host().querySelector('[data-file-image]')).toBeNull();
    expect(host().querySelector('[data-file-failed]')?.textContent?.trim()).toBe('The file could not be shown.');
    expect(button('data-file-card').querySelector('[data-file-open-tab]')?.getAttribute('href')).toBe('/files/1?sig=fresh');
  });

  it("plays a video in the browser's own player, its thumbnail the poster, requesting nothing before it is played", () => {
    mount(6, withClip);

    const video = host().querySelector<HTMLVideoElement>('[data-file-video]');
    expect(video?.getAttribute('src')).toBe('/files/6?sig=a');
    expect(video?.getAttribute('poster')).toBe('/files/6t?sig=a');
    expect(video?.getAttribute('preload')).toBe('none');
    expect(video?.hasAttribute('controls')).toBe(true);
    expect(video?.hasAttribute('playsinline')).toBe(true);
    expect(video?.autoplay).toBe(false);
    expect(video?.getAttribute('aria-label')).toBe('clip.mp4');
    expect(host().querySelector('[data-file-image], [data-file-card], [data-file-fit]')).toBeNull();
    expect(button('data-file-download').getAttribute('href')).toBe('/files/6?sig=a&download');
  });

  it("draws a video without a thumbnail with no poster, and the strip's badge", () => {
    mount(6, [image(1, 'a.png'), { ...clip, thumbUrl: null }]);

    expect(host().querySelector('[data-file-video]')?.hasAttribute('poster')).toBe(false);
    expect(host().querySelectorAll('[data-file-thumb]')[1].textContent?.trim()).toBe('MP4');
  });

  it('asks the page again once when a video fails, plays on from the moment it stopped, and shows its card after a second failure', () => {
    mount(6, withClip);
    const video = (): HTMLVideoElement | null => host().querySelector<HTMLVideoElement>('[data-file-video]');

    const stale = video();
    if (stale) {
      stale.currentTime = 200;
    }
    stale?.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(video()).toBeNull();

    source.files.set(withClip.map(file => (file.key === 6 ? { ...file, url: '/files/6?sig=fresh' } : file)));
    fixture.detectChanges();
    const fresh = video();
    expect(fresh?.getAttribute('src')).toBe('/files/6?sig=fresh');
    expect(fresh?.autoplay).toBe(true);
    fresh?.dispatchEvent(new Event('loadedmetadata'));
    expect(fresh?.currentTime).toBe(200);
    fresh?.dispatchEvent(new Event('playing'));
    fixture.detectChanges();
    expect(fresh?.autoplay).toBe(false);

    fresh?.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(video()).toBeNull();
    const card = button('data-file-card');
    expect(card.querySelector('[data-file-failed]')?.textContent?.trim()).toBe('The file could not be shown.');
    expect(card.querySelector('[data-file-card-download]')?.getAttribute('href')).toBe('/files/6?sig=fresh&download');
  });

  it('closes by its Close button', () => {
    mount(1);

    button('data-file-close').click();

    expect(ref.dismiss).toHaveBeenCalled();
  });

  /** Opens Info and lets its requests answer. */
  async function openInfo(): Promise<HTMLElement> {
    button('data-file-info').click();
    await settle();
    fixture.detectChanges();
    return host().querySelector('[data-file-info-panel]') as HTMLElement;
  }

  const factsOf = (panel: ParentNode): Record<string, string> =>
    Object.fromEntries(
      [...panel.querySelectorAll('dt')].map(term => [term.textContent?.trim() ?? '', term.nextElementSibling?.textContent?.trim() ?? '']),
    );

  it("shows a reader's Info as text, without fields or metadata, and keeps it open as the files are walked", async () => {
    mount(2);
    expect(host().querySelector('[data-file-info-panel]')).toBeNull();
    expect(button('data-file-info').getAttribute('aria-pressed')).toBe('false');

    const panel = await openInfo();

    expect(info).toHaveBeenCalledWith(fiveFiles[1]);
    expect(button('data-file-info').getAttribute('aria-pressed')).toBe('true');
    expect(panel.getAttribute('aria-label')).toBe('Info');
    expect(panel.querySelector('input, textarea')).toBeNull();
    expect(panel.querySelector('[data-file-metadata-toggle]')).toBeNull();
    const facts = factsOf(panel);
    expect(facts['Name']).toBe('b.png');
    expect(facts['Description']).toBe('North side, morning');
    expect(facts['Type']).toBe('image/png');
    expect(facts['Size in pixels']).toBe('4,032 × 3,024');
    expect(facts['Uploaded by']).toBe('Olena');
    expect(facts['Added']).toContain('2026');
    expect(metadata).not.toHaveBeenCalled();

    press('ArrowRight');
    await settle();
    fixture.detectChanges();
    expect(info).toHaveBeenLastCalledWith(fiveFiles[2]);
    expect(factsOf(host().querySelector('[data-file-info-panel]') as HTMLElement)['Name']).toBe('c.png');
  });

  /** A text with the narrow space some ICU data writes before AM and PM as a plain one. */
  const spaced = (text: string): string => text.replace(/\s/g, ' ');

  it("shows the moment a file was added on the clock of a Kyiv profile under a New York browser, a camera's time as written", async () => {
    info.mockImplementation(file => Promise.resolve({ ...infoOf(file), createdAt: '2026-09-26T22:30:00Z' }));
    metadata.mockResolvedValueOnce({ takenAt: '2026-05-01T14:30:05' });
    mount(1, fiveFiles, false, { canReadMetadata: true, timeZone: 'Europe/Kyiv' });
    const panel = await openInfo();

    expect(spaced(factsOf(panel)['Added'])).toBe('Sep 27, 2026, 1:30 AM');

    (panel.querySelector('[data-file-metadata-toggle]') as HTMLElement).click();
    await settle();
    fixture.detectChanges();
    expect(spaced(factsOf(panel.querySelector('[data-file-metadata]') as HTMLElement)['Taken'])).toBe('May 1, 2026, 2:30 PM');
  });

  it("shows the moment a file was added on the browser's clock before sign-in", async () => {
    info.mockImplementation(file => Promise.resolve({ ...infoOf(file), createdAt: '2026-09-26T22:30:00Z' }));
    mount(1);

    expect(spaced(factsOf(await openInfo())['Added'])).toBe('Sep 26, 2026, 6:30 PM');
  });

  it('saves the name without its extension and the description with the version read, then reads Info again', async () => {
    mount(1, [image(1, 'IMG_0042.webp', { contentType: 'image/webp' }), image(2, 'b.png')], false, { canEditInfo: true });
    const panel = await openInfo();
    const name = panel.querySelector<HTMLInputElement>('input') as HTMLInputElement;
    const description = panel.querySelector<HTMLTextAreaElement>('textarea') as HTMLTextAreaElement;
    const save = panel.querySelector('[data-file-info-save]') as HTMLElement;

    expect(name.value).toBe('IMG_0042');
    expect(panel.querySelector('[data-file-info-extension]')?.textContent?.trim()).toBe('.webp');
    expect(description.value).toBe('North side, morning');
    expect(factsOf(panel)['Name']).toBeUndefined();
    expect(save.getAttribute('aria-disabled')).toBe('true');

    // Arrows inside the fields move the caret, not the viewer
    name.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    expect(counter()).toBe('1 / 2');
    name.value = 'Facade';
    name.dispatchEvent(new Event('input'));
    description.value = 'North side, morning, rain';
    description.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(save.getAttribute('aria-disabled')).not.toBe('true');
    save.click();
    await settle();

    expect(saveInfo).toHaveBeenCalledWith(expect.objectContaining({ key: 1 }), {
      name: 'Facade',
      description: 'North side, morning, rain',
      ver: 3,
    });
    expect(info).toHaveBeenCalledTimes(2);
  });

  it('saves a description of spaces as none, and Enter in the name saves', async () => {
    mount(1, [image(1, 'a.png')], false, { canEditInfo: true });
    const panel = await openInfo();
    const name = panel.querySelector('input') as HTMLInputElement;
    const description = panel.querySelector('textarea') as HTMLTextAreaElement;
    description.value = '   ';
    description.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    name.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await settle();

    expect(saveInfo).toHaveBeenCalledWith(expect.objectContaining({ key: 1 }), { name: 'a', description: null, ver: 3 });
  });

  it("asks the metadata only when its section opens, shows the camera and the place's coordinates, and says when there is none", async () => {
    metadata.mockResolvedValueOnce({
      make: 'Apple',
      model: 'Apple iPhone 15',
      fNumber: 1.8,
      iso: 80,
      latitude: 50.4501,
      longitude: 30.5234,
      altitude: 179.5,
    });
    mount(1, fiveFiles, false, { canReadMetadata: true });
    const panel = await openInfo();
    const toggle = panel.querySelector('[data-file-metadata-toggle]') as HTMLElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(metadata).not.toHaveBeenCalled();

    toggle.click();
    await settle();
    fixture.detectChanges();

    expect(metadata).toHaveBeenCalledWith(fiveFiles[0]);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const section = panel.querySelector('[data-file-metadata]') as HTMLElement;
    const facts = factsOf(section);
    expect(facts['Camera']).toBe('Apple iPhone 15');
    expect(facts['Aperture']).toBe('f/1.8');
    expect(facts['ISO']).toBe('80');
    expect(facts['Place']).toBe('50.450100, 30.523400');
    expect(facts['Altitude']).toContain('179.5');
    await expectAccessible(host());

    press('ArrowRight');
    await settle();
    fixture.detectChanges();
    expect(metadata).toHaveBeenLastCalledWith(fiveFiles[1]);
    expect(host().querySelector('[data-file-metadata-none]')?.textContent?.trim()).toBe('No metadata.');
  });

  it('shows a failed Info with Try again', async () => {
    info.mockRejectedValueOnce(new Error('offline'));
    mount(1);
    const panel = await openInfo();

    expect(panel.querySelector('[role="alert"]')?.textContent?.trim()).toBe('The request failed.');
    (panel.querySelector('ui-btn') as HTMLElement).click();
    await settle();
    fixture.detectChanges();
    expect(factsOf(host().querySelector('[data-file-info-panel]') as HTMLElement)['Name']).toBe('a.png');
  });

  it('passes axe with Info open, as fields and as text', async () => {
    mount(1, fiveFiles, false, { canEditInfo: true, canReadMetadata: true });
    await openInfo();
    await expectAccessible(host());
  });

  it('passes axe on an image with its delete', async () => {
    mount(2, fiveFiles, true);

    await expectAccessible(host());
  });

  it('passes axe on a video', async () => {
    mount(6, withClip);

    await expectAccessible(host());
  });

  it('passes axe on a PDF', async () => {
    mount(4);

    await expectAccessible(host());
  });

  it('lays out the header, the stage with its file beside Info, and the strip: an image, a video, a card and Info open', async () => {
    const count = (): number => host().querySelectorAll('*').length;
    const row = (stage: string, info = ''): string =>
      'div.flex.flex-col.flex-1.min-h-0.min-w-0.@4xl/panel:flex-row{' +
      'div.relative.flex.flex-1.min-h-0.min-w-0.bg-surface-2{' +
      `div.flex.flex-1.min-h-0.min-w-0.overflow-auto.outline-none{${stage}}, ui-btn, ui-btn}${info}}`;
    const card =
      'div.m-auto.flex.flex-col.items-center.gap-3.p-6.rounded-lg.border.border-line.bg-surface{' +
      'span, div.flex.flex-wrap.justify-center.gap-2{a, a}}';

    mount(2);
    expect([...host().classList]).toEqual(['flex', 'flex-col', 'h-full', 'min-h-0', 'min-w-0']);
    expect(boxesOf(host())).toEqual(['header', row('img'), 'ul']);
    expect(boxesOf(button('data-file-bar'))).toEqual([
      'p',
      'div.flex.shrink-0.items-center.gap-(--ui-panel-gap){ui-btn, ui-btn}',
      'a',
      'ui-btn',
      'ui-btn',
    ]);
    const atImage = count();

    press('End');
    press('ArrowLeft');
    expect(boxesOf(host())).toEqual(['header', row(card), 'ul']);
    expect(boxesOf(button('data-file-bar'))).toEqual(['p', 'a', 'ui-btn', 'ui-btn']);
    const atCard = count();

    await openInfo();
    expect(boxesOf(host())).toEqual(['header', row(card, ', section'), 'ul']);
    expect(boxesOf(button('data-file-info-panel'))).toEqual(['ui-file-info-panel']);
    const withInfo = count();
    fixture.destroy();
    TestBed.resetTestingModule();

    mount(6, withClip);
    expect(boxesOf(host())).toEqual(['header', row('video'), 'ul']);
    expect(boxesOf(button('data-file-bar'))).toEqual(['p', 'a', 'ui-btn', 'ui-btn']);

    expect({ atImage, atCard, withInfo, atVideo: count() }).toEqual({ atImage: 58, atCard: 62, withInfo: 87, atVideo: 40 });
  });
});

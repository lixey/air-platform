import { signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectAccessible } from '../../tests/axe';
import { boxesOf } from '../../tests/boxes';
import type { FileInfoDto, FileMetadataDto } from '../core/generated/contracts';
import { FileInfoPanelComponent } from './file-info-panel.component';
import type { FileView } from './file-view';
import { FILE_VIEWER_SOURCE, type FileInfoChange, type FileViewerSource } from './file-viewer';

const photo: FileView = { key: 12, name: 'site.jpg', size: 3000, contentType: 'image/jpeg', url: '/f/12', thumbUrl: '/f/12t' };
const wall: FileView = { key: 13, name: 'wall.png', size: 4000, contentType: 'image/png', url: '/f/13', thumbUrl: null };

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

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i++) {
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
  }
}

describe('FileInfoPanelComponent', () => {
  let fixture: ComponentFixture<FileInfoPanelComponent>;
  let info: ReturnType<typeof vi.fn<(file: FileView) => Promise<FileInfoDto>>>;
  let saveInfo: ReturnType<typeof vi.fn<(file: FileView, change: FileInfoChange) => Promise<boolean>>>;
  let metadata: ReturnType<typeof vi.fn<(file: FileView) => Promise<FileMetadataDto | null>>>;

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const one = (selector: string): HTMLElement => host().querySelector(selector) as HTMLElement;
  const count = (): number => host().querySelectorAll('*').length;
  const factsOf = (from: ParentNode): Record<string, string> =>
    Object.fromEntries(
      [...from.querySelectorAll('dt')].map(term => [term.textContent?.trim() ?? '', term.nextElementSibling?.textContent?.trim() ?? '']),
    );

  /** Mounts the panel at `file` and lets its Info answer. */
  async function mount(file: FileView, { canEditInfo = false, canReadMetadata = false } = {}): Promise<void> {
    const source: FileViewerSource = {
      files: signal([photo, wall]),
      canDelete: signal(false),
      isRefreshing: signal(false),
      delete: () => Promise.resolve(false),
      refresh: () => undefined,
      canEditInfo: signal(canEditInfo),
      canReadMetadata: signal(canReadMetadata),
      info,
      saveInfo,
      metadata,
    };
    TestBed.configureTestingModule({ providers: [{ provide: FILE_VIEWER_SOURCE, useValue: source }] });
    fixture = TestBed.createComponent(FileInfoPanelComponent);
    fixture.componentRef.setInput('file', file);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
  }

  /** Types `text` into a field of the panel. */
  function type(field: HTMLInputElement | HTMLTextAreaElement, text: string): void {
    field.value = text;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  beforeEach(() => {
    info = vi.fn<(file: FileView) => Promise<FileInfoDto>>(file => Promise.resolve(infoOf(file)));
    saveInfo = vi.fn<(file: FileView, change: FileInfoChange) => Promise<boolean>>(() => Promise.resolve(true));
    metadata = vi.fn<(file: FileView) => Promise<FileMetadataDto | null>>(() => Promise.resolve({ make: 'Apple', model: 'iPhone 15' }));
  });
  afterEach(() => {
    fixture.destroy();
    TestBed.resetTestingModule();
  });

  it("shows a reader's Info as text: the name, the description and the facts, with no field and no metadata", async () => {
    await mount(photo);

    expect(info).toHaveBeenCalledTimes(1);
    expect(info).toHaveBeenCalledWith(photo);
    expect(one('h3').textContent).toBe('Info');
    expect(host().querySelector('input, textarea, [data-file-info-form], [data-file-metadata-toggle]')).toBeNull();
    const facts = factsOf(host());
    expect(Object.keys(facts)).toEqual(['Name', 'Description', 'Type', 'Size', 'Size in pixels', 'Added', 'Uploaded by']);
    expect(facts['Name']).toBe('site.jpg');
    expect(facts['Description']).toBe('North side, morning');
    expect(facts['Type']).toBe('image/jpeg');
    expect(facts['Size']).toBe('2.9 kB');
    expect(facts['Size in pixels']).toBe('4,032 × 3,024');
    expect(facts['Uploaded by']).toBe('Olena');
  });

  it('leaves out the facts the Info does not say', async () => {
    info.mockResolvedValueOnce({
      id: 12,
      ver: 1,
      name: 'site.jpg',
      contentType: 'image/jpeg',
      size: 3000,
      createdAt: '2026-09-25T10:00:00Z',
    });
    await mount(photo);

    expect(Object.keys(factsOf(host()))).toEqual(['Name', 'Type', 'Size', 'Added']);
  });

  it('shows the name without its extension and the description as fields where the opener offers the upload', async () => {
    await mount(photo, { canEditInfo: true });

    const name = one('input') as HTMLInputElement;
    const description = one('textarea') as HTMLTextAreaElement;
    expect(name.value).toBe('site');
    expect(one('[data-file-info-extension]').textContent).toBe('.jpg');
    expect(description.value).toBe('North side, morning');
    expect(description.getAttribute('maxlength')).toBe('2000');
    expect(one(`label[for="${name.id}"]`).textContent).toBe('Name');
    expect(one(`label[for="${description.id}"]`).textContent).toBe('Description');
    expect(Object.keys(factsOf(host()))).toEqual(['Type', 'Size', 'Size in pixels', 'Added', 'Uploaded by']);
  });

  it('saves nothing while the fields say what the Info says, or name nothing', async () => {
    await mount(photo, { canEditInfo: true });
    const name = one('input') as HTMLInputElement;
    const save = one('[data-file-info-save]');

    expect(save.getAttribute('aria-disabled')).toBe('true');
    save.click();
    name.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    type(name, '   ');
    expect(save.getAttribute('aria-disabled')).toBe('true');
    name.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await settle();

    expect(saveInfo).not.toHaveBeenCalled();
  });

  it('saves the name alone where it alone changed, with the version read, then reads the Info again', async () => {
    await mount(photo, { canEditInfo: true });

    type(one('input') as HTMLInputElement, ' Facade ');
    expect(one('[data-file-info-save]').getAttribute('aria-disabled')).not.toBe('true');
    one('[data-file-info-save]').click();
    await settle();

    expect(saveInfo).toHaveBeenCalledTimes(1);
    expect(saveInfo).toHaveBeenCalledWith(photo, { name: 'Facade', description: 'North side, morning', ver: 3 });
    expect(info).toHaveBeenCalledTimes(2);
  });

  it('saves a changed description, one of spaces as none, by Enter in the name', async () => {
    await mount(photo, { canEditInfo: true });

    type(one('textarea') as HTMLTextAreaElement, '   ');
    const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    one('input').dispatchEvent(enter);
    await settle();

    expect(enter.defaultPrevented).toBe(true);
    expect(saveInfo).toHaveBeenCalledWith(photo, { name: 'site', description: null, ver: 3 });
  });

  it('does not read the Info again after a save that did not commit', async () => {
    saveInfo.mockResolvedValueOnce(false);
    await mount(photo, { canEditInfo: true });

    type(one('input') as HTMLInputElement, 'Facade');
    one('[data-file-info-save]').click();
    await settle();
    fixture.detectChanges();

    expect(saveInfo).toHaveBeenCalledTimes(1);
    expect(info).toHaveBeenCalledTimes(1);
    expect((one('input') as HTMLInputElement).value).toBe('Facade');
    expect(host().hasAttribute('aria-busy')).toBe(false);
  });

  it('asks the metadata when its section opens, and for another file while it is open', async () => {
    await mount(photo, { canReadMetadata: true });
    const toggle = one('[data-file-metadata-toggle]');

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe(one('[data-file-metadata]').id);
    expect(one('[data-file-metadata]').hidden).toBe(true);
    expect(metadata).not.toHaveBeenCalled();

    toggle.click();
    await settle();
    fixture.detectChanges();
    expect(metadata).toHaveBeenCalledTimes(1);
    expect(metadata).toHaveBeenCalledWith(photo);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(one('[data-file-metadata]').hidden).toBe(false);
    expect(factsOf(one('[data-file-metadata]'))).toEqual({ Camera: 'Apple iPhone 15' });

    fixture.componentRef.setInput('file', wall);
    await settle();
    fixture.detectChanges();
    expect(info).toHaveBeenLastCalledWith(wall);
    expect(metadata).toHaveBeenCalledTimes(2);
    expect(metadata).toHaveBeenLastCalledWith(wall);
  });

  it('offers no metadata without the right to read it', async () => {
    await mount(photo, { canEditInfo: true });

    expect(host().querySelector('[data-file-metadata-toggle], [data-file-metadata]')).toBeNull();
    expect(metadata).not.toHaveBeenCalled();
  });

  it('says so for a file without metadata, and shows a failed metadata with Try again', async () => {
    metadata.mockResolvedValueOnce(null);
    await mount(photo, { canReadMetadata: true });
    one('[data-file-metadata-toggle]').click();
    await settle();
    fixture.detectChanges();
    expect(one('[data-file-metadata-none]').textContent).toBe('No metadata.');

    metadata.mockRejectedValueOnce(new Error('offline'));
    fixture.componentRef.setInput('file', wall);
    await settle();
    fixture.detectChanges();
    expect(one('[data-file-metadata] [role="alert"]').textContent).toBe('The request failed.');

    one('[data-file-metadata] ui-btn').click();
    await settle();
    fixture.detectChanges();
    expect(factsOf(one('[data-file-metadata]'))).toEqual({ Camera: 'Apple iPhone 15' });
  });

  it('shows the Info being read, and a failed one with Try again', async () => {
    info.mockRejectedValueOnce(new Error('offline'));
    const reading = mount(photo);
    expect(one('p').textContent).toBe('Loading…');
    expect(host().getAttribute('aria-busy')).toBe('true');
    expect(boxesOf(host())).toEqual(['h3', 'p']);
    await reading;

    expect(host().hasAttribute('aria-busy')).toBe(false);
    expect(one('[role="alert"]').textContent).toBe('The request failed.');
    expect(boxesOf(host())).toEqual(['h3', 'div.flex.items-center.gap-2{p, ui-btn}']);

    one('ui-btn').click();
    await settle();
    fixture.detectChanges();
    expect(factsOf(host())['Name']).toBe('site.jpg');
  });

  it('lays out the Info as text: the title and the list of facts', async () => {
    await mount(photo);

    expect([...host().classList]).toEqual(['flex', 'flex-col', 'gap-3', 'min-w-0', 'p-3']);
    expect(boxesOf(host())).toEqual(['h3', 'dl']);
    expect(boxesOf(one('dl'))).toEqual(Array(7).fill('div.flex.flex-col.min-w-0{dt, dd}'));
    expect(count()).toBe(23);
  });

  it('lays out the Info as fields: the title, the form, the facts and the metadata, closed and open', async () => {
    await mount(photo, { canEditInfo: true, canReadMetadata: true });
    const form =
      'div.flex.flex-col.gap-3{' +
      'div.flex.flex-col.gap-1{label, div.flex.items-center.gap-1.min-w-0{ui-text-box, span}}, ' +
      'div.flex.flex-col.gap-1{label, ui-text-box}, ' +
      'div.flex.justify-end{ui-btn}}';

    expect(boxesOf(host())).toEqual(['h3', form, 'dl', 'div.flex.flex-col.gap-2.border-t.border-line.pt-2{button, div{}}']);
    expect(boxesOf(one('dl'))).toEqual(Array(5).fill('div.flex.flex-col.min-w-0{dt, dd}'));
    const closed = count();

    one('[data-file-metadata-toggle]').click();
    await settle();
    fixture.detectChanges();
    expect(boxesOf(host())).toEqual(['h3', form, 'dl', 'div.flex.flex-col.gap-2.border-t.border-line.pt-2{button, div{dl}}']);
    expect([closed, count()]).toEqual([38, 42]);
  });

  it('passes axe as text and as fields with the metadata open', async () => {
    await mount(photo);
    await expectAccessible(host());
    fixture.destroy();
    TestBed.resetTestingModule();

    await mount(photo, { canEditInfo: true, canReadMetadata: true });
    one('[data-file-metadata-toggle]').click();
    await settle();
    fixture.detectChanges();
    await expectAccessible(host());
  });
});

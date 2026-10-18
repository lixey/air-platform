import { describe, expect, it } from 'vitest';
import { uploadLimits } from '../../../tests/upload-limits';
import type { FormFilesModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import type { FileUpload, FileUploadStatus, FileUploadTarget } from '../../file/file-upload-queue';
import {
  fileActionRequestOf,
  fileItemsOf,
  fileRowsOf,
  filesAcceptsOf,
  filesCountText,
  filesHeldOf,
  filesLimitsText,
  filesTargetOf,
  filesZoneClasses,
  inPlacedOrder,
  isFilesFull,
  uploadsShownOf,
} from './form-files';

const model: FormFilesModel = {
  kind: 'FormFiles',
  key: 'files',
  service: 'folder-files',
  parentService: 'folder-form',
  parentId: 7,
  look: 'tiles',
  limits: uploadLimits({ maxFiles: 5 }),
  canUpload: true,
  canDelete: true,
  canSetCover: false,
  canReadMetadata: false,
};

const rows = fileRowsOf({
  rows: [
    { id: 1, ver: 3, name: 'logo.png', contentType: 'image/png', size: 100 },
    { name: 'no-key.pdf', size: 5 },
    { id: 2, ver: 1, name: 'plan.pdf', contentType: 'application/pdf', size: 2000 },
  ],
});

const target: FileUploadTarget = { ...model.limits, service: model.service, parentId: 7 };

function upload(id: number, status: FileUploadStatus, fileId?: number | null, under: FileUploadTarget = target): FileUpload {
  return { id, file: new File([new Uint8Array(10 * id)], `f${id}.pdf`), target: under, batch: 1, status, progress: 0, fileId };
}

const texts = new TextsService();

describe('the files element', () => {
  it('takes the rows of a page that carry a key, each with its file', () => {
    expect(rows.map(({ file }) => [file.key, file.name, file.size])).toEqual([
      [1, 'logo.png', 100],
      [2, 'plan.pdf', 2000],
    ]);
    expect(fileRowsOf(null)).toEqual([]);
  });

  it('shows the rows in the order a move placed them, a row it does not name last', () => {
    expect(inPlacedOrder(rows, [2, 1]).map(({ file }) => file.key)).toEqual([2, 1]);
    expect(inPlacedOrder(rows, [2]).map(({ file }) => file.key)).toEqual([2, 1]);
    expect(inPlacedOrder(rows, null)).toBe(rows);
  });

  it('draws the files on their way first, then the rows, in the order of the handles while one is carried', () => {
    const items = fileItemsOf([upload(9, 'uploading')], rows, [
      { key: 2, name: 'plan.pdf' },
      { key: 1, name: 'logo.png' },
    ]);
    expect(items.map(item => item.id)).toEqual(['upload-9', 'row-2', 'row-1']);
    expect(items[0]).toMatchObject({ row: null, file: { key: null, name: 'f9.pdf' } });
    expect(items[1].upload).toBeNull();
    expect(fileItemsOf([], rows).map(item => item.id)).toEqual(['row-1', 'row-2']);
  });

  it("shows the queue's files under the record until the page shows them", () => {
    const elsewhere = upload(4, 'waiting', undefined, { ...target, parentId: 8 });
    const queue = [upload(1, 'failed'), upload(2, 'uploaded', 2), upload(3, 'uploaded', 3), upload(5, 'uploaded', null), elsewhere];

    expect(uploadsShownOf(queue, target, rows, true).map(item => item.id)).toEqual([1, 3, 5]);
    expect(uploadsShownOf(queue, target, rows, false).map(item => item.id)).toEqual([1]);
    expect(uploadsShownOf(queue, null, rows, true)).toEqual([]);
  });

  it('holds the rows and the files uploaded since, one without its key only while the page is asked again', () => {
    const queue = [upload(1, 'uploaded', 2), upload(3, 'uploaded', 9), upload(5, 'uploaded', null), upload(6, 'uploading')];

    expect(filesHeldOf(rows, queue, model, true)).toEqual({ files: 4, bytes: 2100 + 30 + 50 });
    expect(filesHeldOf(rows, queue, model, false)).toEqual({ files: 3, bytes: 2100 + 30 });
    expect(filesHeldOf(rows, queue, null, true)).toEqual({ files: 2, bytes: 2100 });
  });

  it('is full at maxFiles files or maxTotalBytes, and never without either', () => {
    expect(isFilesFull({ maxFiles: 5, maxTotalBytes: 0 }, { files: 5, bytes: 0 })).toBe(true);
    expect(isFilesFull({ maxFiles: 5, maxTotalBytes: 0 }, { files: 4, bytes: 1e9 })).toBe(false);
    expect(isFilesFull({ maxFiles: 0, maxTotalBytes: 100 }, { files: 1, bytes: 100 })).toBe(true);
    expect(isFilesFull({ maxFiles: 0, maxTotalBytes: 0 }, { files: 1e3, bytes: 1e9 })).toBe(false);
  });

  it('counts against maxFiles, naming the record full, and says no room only when there is no maxFiles', () => {
    expect(filesCountText(texts, { maxFiles: 5 }, { files: 2, bytes: 0 }, false)).toBe('2 of 5');
    expect(filesCountText(texts, { maxFiles: 5 }, { files: 5, bytes: 0 }, true)).toBe('5 of 5: no room for more files');
    expect(filesCountText(texts, { maxFiles: 0 }, { files: 5, bytes: 0 }, true)).toBe('The record has no room for this file.');
    expect(filesCountText(texts, { maxFiles: 0 }, { files: 5, bytes: 0 }, false)).toBe('');
  });

  it('names the accepted types and the largest file in its limits', () => {
    expect(filesLimitsText(texts, model.limits, 'en')).toBe('PNG, PDF - up to 1 MB');
  });

  it("sends the files to the file grid under a saved record, within the model's limits", () => {
    expect(filesTargetOf(model, 'Saved with warnings')).toEqual({
      ...model.limits,
      service: 'folder-files',
      parentService: 'folder-form',
      parentId: 7,
      committedWarningTitle: 'Saved with warnings',
    });
    expect(filesTargetOf({ ...model, parentId: null })).toBeNull();
    expect(filesTargetOf({ ...model, canUpload: false })).toBeNull();
    expect(filesTargetOf(null)).toBeNull();
  });

  it('gives each picker what it takes, and nothing without limits', () => {
    expect(filesAcceptsOf(uploadLimits({ accept: ['image/png', 'video/mp4'], extensions: ['.png', '.mp4'] }))).toEqual({
      picker: 'image/png,video/mp4,.png,.mp4',
      camera: 'image/png',
      video: 'video/mp4',
    });
    expect(filesAcceptsOf(undefined)).toEqual({ picker: '', camera: '', video: '' });
  });

  it('draws the zone while it takes files, its edge blue under a drag', () => {
    expect(filesZoneClasses(true, false)).toContain('border-field-edge');
    expect(filesZoneClasses(true, true)).toContain('border-field-hover');
    expect(filesZoneClasses(false, true)).toBe('block min-w-0');
  });

  it('names the record and the files with their versions in a file action, the record alone for none', () => {
    expect(
      fileActionRequestOf(
        model,
        'delete',
        rows.map(({ row }) => row),
      ),
    ).toEqual({
      service: 'folder-files',
      body: { parentService: 'folder-form', parentId: 7, selectedIds: [1, 2], selectedVersions: [3, 1] },
    });
    expect(fileActionRequestOf(model, 'download-all', null)).toEqual({
      service: 'folder-files',
      body: { parentService: 'folder-form', parentId: 7 },
    });
    expect(fileActionRequestOf({ ...model, parentId: null }, 'set-cover', [])).toBeNull();
  });
});

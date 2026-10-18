import { describe, expect, it } from 'vitest';
import { uploadLimits } from '../../tests/upload-limits';
import type { FileUploadLimitsDto } from '../core/generated/contracts';
import {
  acceptedNamesOf,
  cameraAcceptOf,
  pastedFilesOf,
  pickerAcceptOf,
  takenMaxBytesOf,
  uploadFormOf,
  uploadRefusalOf,
  videoAcceptOf,
} from './file-upload-call';

const MIB = 1024 * 1024;
const limits = uploadLimits({
  accept: ['image/png', 'image/jpeg', 'application/pdf'],
  extensions: ['.png', '.jpg', '.jpeg', '.pdf'],
  maxBytes: MIB,
  sourceMaxBytes: MIB,
  images: 'keep',
});

const fileOf = (name: string, type: string, size: number): File => new File([new Uint8Array(size)], name, { type });

describe('The upload request', () => {
  it('refuses above the largest size, outside the accepted types and names, comparing types as the server does', () => {
    expect(uploadRefusalOf({ name: 'plan.pdf', size: MIB, type: 'application/pdf' }, limits)).toBeNull();
    expect(uploadRefusalOf({ name: 'plan.pdf', size: MIB + 1, type: 'application/pdf' }, limits)).toBe('size');
    expect(uploadRefusalOf({ name: 'PLAN.PDF', size: 10, type: 'Application/PDF; charset=binary' }, limits)).toBeNull();
    expect(uploadRefusalOf({ name: 'logo.svg', size: 10, type: 'image/svg+xml' }, limits)).toBe('type');
    expect(uploadRefusalOf({ name: 'plan.pdf', size: 10, type: '' }, limits)).toBe('type');
    expect(uploadRefusalOf({ name: 'plan.exe', size: 10, type: 'application/pdf' }, limits)).toBe('type');
    expect(uploadRefusalOf({ name: 'plan', size: 10, type: 'application/pdf' }, { ...limits, extensions: [] })).toBeNull();
  });

  it('takes an image its rule makes smaller up to the source bound, and any other file up to the largest size posted', () => {
    const optimized: FileUploadLimitsDto = { ...limits, sourceMaxBytes: 4 * MIB, images: 'optimize' };
    const lossless: FileUploadLimitsDto = { ...optimized, images: 'lossless' };

    expect(takenMaxBytesOf('image/jpeg', optimized)).toBe(4 * MIB);
    expect(takenMaxBytesOf('application/pdf', optimized)).toBe(MIB);
    expect(takenMaxBytesOf('image/jpeg', lossless)).toBe(MIB);
    expect(takenMaxBytesOf('image/png', lossless)).toBe(4 * MIB);
    expect(takenMaxBytesOf('image/jpeg', { ...optimized, images: 'keep' })).toBe(MIB);
    expect(uploadRefusalOf({ name: 'photo.jpg', size: 3 * MIB, type: 'image/jpeg' }, optimized)).toBeNull();
    expect(uploadRefusalOf({ name: 'photo.jpg', size: 4 * MIB + 1, type: 'image/jpeg' }, optimized)).toBe('size');
    expect(uploadRefusalOf({ name: 'plan.pdf', size: 3 * MIB, type: 'application/pdf' }, optimized)).toBe('size');
  });

  it('offers the picker the types and their extensions, the camera the raster images and the videos apart', () => {
    const phone = {
      accept: ['image/jpeg', 'video/mp4', 'video/quicktime', 'application/pdf'],
      extensions: ['.jpg', '.mp4', '.mov', '.pdf'],
    };

    expect(pickerAcceptOf(limits)).toBe('image/png,image/jpeg,application/pdf,.png,.jpg,.jpeg,.pdf');
    expect(pickerAcceptOf(phone)).toBe('image/jpeg,video/mp4,video/quicktime,application/pdf,.jpg,.mp4,.mov,.pdf');
    expect(cameraAcceptOf(limits)).toBe('image/png,image/jpeg');
    expect(cameraAcceptOf({ ...limits, accept: ['application/pdf'] })).toBe('');
    expect(videoAcceptOf(phone)).toBe('video/mp4,video/quicktime');
    expect(videoAcceptOf(limits)).toBe('');
  });

  it('names each accepted type once by its extension, or by the type where the limits carry no extension', () => {
    expect(acceptedNamesOf(limits)).toBe('PNG, JPG, PDF');
    expect(acceptedNamesOf({ ...limits, extensions: [] })).toBe('image/png, image/jpeg, application/pdf');
  });

  it('names a pasted file of an accepted type by the moment of the paste and leaves any other as it came', () => {
    const at = new Date(2026, 8, 27, 14, 5, 9);
    const screenshot = fileOf('image.png', 'image/png', 10);
    const photo = fileOf('blob', 'image/jpeg', 10);
    const setup = fileOf('setup.exe', 'application/x-msdownload', 10);

    const [pastedScreenshot, pastedPhoto, pastedSetup] = pastedFilesOf([screenshot, photo, setup], limits, at, 'America/New_York');

    expect(pastedScreenshot.name).toBe('pasted-20260927-140509.png');
    expect(pastedScreenshot.type).toBe('image/png');
    expect(pastedScreenshot.size).toBe(10);
    expect(pastedPhoto.name).toBe('pasted-20260927-140509.jpg');
    expect(pastedSetup).toBe(setup);
  });

  it('names a pasted file by the moment on the clock of a Kyiv profile under a New York browser', () => {
    const at = new Date('2026-09-28T00:05:09Z');

    const [pasted] = pastedFilesOf([fileOf('image.png', 'image/png', 10)], limits, at, 'Europe/Kyiv');

    expect(pasted.name).toBe('pasted-20260928-030509.png');
    expect(pasted.lastModified).toBe(at.getTime());
  });

  it('holds the parent, the file, and the thumbnail the browser made', () => {
    const file = fileOf('plan.pdf', 'application/pdf', 1);
    const thumbnail = new Blob([new Uint8Array(4)], { type: 'image/webp' });

    expect([...uploadFormOf({}, file).keys()]).toEqual(['file']);
    expect([...uploadFormOf({ parentService: 'sample-report-form', parentId: 7 }, file).entries()].slice(0, 2)).toEqual([
      ['parentService', 'sample-report-form'],
      ['parentId', '7'],
    ]);
    const form = uploadFormOf({}, file, thumbnail);
    expect([...form.keys()]).toEqual(['file', 'thumbnail']);
    expect((form.get('thumbnail') as File).name).toBe('thumbnail.webp');
    expect((form.get('thumbnail') as File).type).toBe('image/webp');
  });

  it("holds the image's metadata as JSON and the file the new one follows, each only when there is one", () => {
    const file = fileOf('photo.jpg', 'image/jpeg', 1);

    const form = uploadFormOf({}, file, null, { metadata: { make: 'Apple', latitude: 50.4501 }, afterId: 12 });

    expect([...form.keys()]).toEqual(['file', 'metadata', 'afterId']);
    expect(JSON.parse(form.get('metadata') as string)).toEqual({ make: 'Apple', latitude: 50.4501 });
    expect(form.get('afterId')).toBe('12');
    expect([...uploadFormOf({}, file, null, { metadata: null, afterId: null }).keys()]).toEqual(['file']);
  });
});

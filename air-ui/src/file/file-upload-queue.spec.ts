import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, type TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { uploadLimits } from '../../tests/upload-limits';
import { AlertService } from '../app/alert/alert.service';
import { errorAlertInterceptor } from '../app/alert/error-alert.interceptor';
import { credentialsInterceptor } from '../app/auth/credentials.interceptor';
import { FileUploadQueue, type FileUploadTarget, type ShownFile } from './file-upload-queue';

const MIB = 1024 * 1024;
const service = 'sample-report-file-grid';
const target: FileUploadTarget = {
  service,
  parentService: 'sample-report-form',
  parentId: 7,
  ...uploadLimits({
    accept: ['image/png', 'image/jpeg', 'image/webp', 'application/pdf'],
    extensions: ['.png', '.jpg', '.jpeg', '.webp', '.pdf'],
    maxBytes: MIB,
    sourceMaxBytes: 4 * MIB,
    maxFiles: 0,
    images: 'keep',
    imageMaxSide: 2048,
  }),
};

const fileOf = (name: string, type: string, size: number): File => new File([new Uint8Array(size)], name, { type });

const u32 = (value: number): number[] => [(value >>> 24) & 0xff, (value >>> 16) & 0xff, (value >>> 8) & 0xff, value & 0xff];
const textOf = (text: string): number[] => [...text].map(char => char.charCodeAt(0));

/** A big-endian TIFF whose IFD0 holds one tag, `Make`, naming the camera's maker. */
function makeTiff(make: string): number[] {
  const value = [...textOf(make), 0];
  return [...textOf('MM'), 0, 42, ...u32(8), 0, 1, 0x01, 0x0f, 0, 2, ...u32(value.length), ...u32(26), ...u32(0), ...value];
}

/** A PNG chunk; its CRC is left zero, which the reader never checks. */
const chunkOf = (type: string, data: number[]): number[] => [...u32(data.length), ...textOf(type), ...data, ...u32(0)];

/** A PNG of the size given, with an eXIf chunk of the TIFF given, followed by `padding` bytes of its data. */
function pngOf(name: string, width: number, height: number, tiff: number[] | null = null, padding = 0): File {
  const head = [
    0x89,
    0x50,
    0x4e,
    0x47,
    0x0d,
    0x0a,
    0x1a,
    0x0a,
    ...chunkOf('IHDR', [...u32(width), ...u32(height), 8, 6, 0, 0, 0]),
    ...(tiff ? chunkOf('eXIf', tiff) : []),
  ];
  return new File([new Uint8Array(head), new Uint8Array(padding), new Uint8Array(chunkOf('IEND', []))], name, { type: 'image/png' });
}

/** A browser that decodes any image at the size given and encodes a WebP of `encodedBytes`. */
function webpBrowser(width: number, height: number, encodedBytes: number): void {
  const bitmap = { width, height, close: () => undefined };
  vi.stubGlobal(
    'createImageBitmap',
    vi.fn((_source: unknown, options?: ImageBitmapOptions) =>
      Promise.resolve(options?.resizeWidth ? { width: options.resizeWidth, height: options.resizeHeight, close: () => undefined } : bitmap),
    ),
  );
  vi.stubGlobal(
    'OffscreenCanvas',
    class {
      constructor(
        readonly width: number,
        readonly height: number,
      ) {}
      getContext() {
        return { fillRect: () => undefined, setTransform: () => undefined, drawImage: () => undefined };
      }
      convertToBlob({ type }: { type: string }) {
        const size = type === 'image/webp' && this.width > 320 ? encodedBytes : 1000;
        return Promise.resolve(new Blob([new Uint8Array(size)], { type: 'image/webp' }));
      }
    },
  );
}

async function settle(): Promise<void> {
  for (let i = 0; i < 8; i++) {
    TestBed.tick();
    await new Promise(resolve => setTimeout(resolve));
  }
}

describe('FileUploadQueue', () => {
  let http: HttpTestingController;
  let queue: FileUploadQueue;
  const alerts = { open: vi.fn(), error: vi.fn(), warnings: AlertService.prototype.warnings };

  const uploads = (): TestRequest[] => http.match(`/api/${service}/upload`);
  const statuses = (): string[] => queue.uploads().map(upload => upload.status);
  const details = (): (string | undefined)[] => queue.uploads().map(upload => upload.detail);
  const bodyOf = (request: TestRequest): FormData => request.request.body as FormData;
  const afterIdOf = (request: TestRequest): FormDataEntryValue | null => bodyOf(request).get('afterId');
  const nameOf = (request: TestRequest): string => (bodyOf(request).get('file') as File).name;

  /** The one request under way, answered with the key the server gave the file. */
  async function answer(id: number): Promise<TestRequest> {
    const [request] = uploads();
    request.flush({ count: 1, outcome: 'committed', id });
    await settle();
    return request;
  }

  beforeEach(() => {
    alerts.error.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorAlertInterceptor, credentialsInterceptor])),
        provideHttpClientTesting(),
        { provide: AlertService, useValue: alerts },
        FileUploadQueue,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    queue = TestBed.inject(FileUploadQueue);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
    vi.unstubAllGlobals();
  });

  describe('ui/grid: An upload travels with its key', () => {
    it("An upload's retry sends its key again", async () => {
      const keyOf = (request: TestRequest): string | null => request.request.headers.get('Idempotency-Key');
      queue.add([fileOf('a.pdf', 'application/pdf', 10), fileOf('b.pdf', 'application/pdf', 10)], target);
      await settle();

      const [failed] = uploads();
      failed.error(new ProgressEvent('error'));
      await settle();
      const next = await answer(12);
      queue.retry(queue.uploads()[0].id);
      await settle();
      const retried = await answer(11);

      expect(nameOf(retried)).toBe('a.pdf');
      expect(keyOf(failed)).toBeTruthy();
      expect(keyOf(retried), 'the retry posts under the key of the failed post').toBe(keyOf(failed));
      expect(keyOf(next), 'the next file posts under a key of its own').not.toBe(keyOf(failed));
      expect(keyOf(next)).toBeTruthy();
    });
  });

  it('places a batch first in its order, each file after the one before it, a retried file after its own predecessor', async () => {
    queue.add(
      [fileOf('a.pdf', 'application/pdf', 10), fileOf('b.pdf', 'application/pdf', 10), fileOf('c.pdf', 'application/pdf', 10)],
      target,
    );
    await settle();

    const first = await answer(11);
    expect(nameOf(first)).toBe('a.pdf');
    expect(bodyOf(first).has('afterId')).toBe(false);

    const [second] = uploads();
    expect(afterIdOf(second)).toBe('11');
    second.error(new ProgressEvent('error'));
    await settle();

    const third = await answer(13);
    expect(nameOf(third)).toBe('c.pdf');
    expect(afterIdOf(third)).toBe('11');
    expect(statuses()).toEqual(['uploaded', 'failed', 'uploaded']);

    queue.retry(queue.uploads()[1].id);
    await settle();
    const retried = await answer(12);
    expect(nameOf(retried)).toBe('b.pdf');
    expect(afterIdOf(retried)).toBe('11');
    expect(queue.uploads().map(upload => upload.fileId)).toEqual([11, 12, 13]);

    queue.add([fileOf('d.pdf', 'application/pdf', 10)], target);
    await settle();
    expect(bodyOf(await answer(14)).has('afterId')).toBe(false);
  });

  it('posts from a batch as many files as its record has room for, in order, and refuses the rest as full', async () => {
    const shown: ShownFile[] = Array.from({ length: 48 }, (_, index) => ({ id: index + 1, size: 100 }));
    queue.registerShownFiles(() => shown);

    queue.add(
      ['1', '2', '3', '4', '5'].map(name => fileOf(`${name}.pdf`, 'application/pdf', 10)),
      { ...target, maxFiles: 50 },
    );
    await settle();

    expect(statuses()).toEqual(['uploading', 'waiting', 'refused', 'refused', 'refused']);
    expect(details().slice(2)).toEqual(Array(3).fill('The record has no room for this file.'));
    expect(bodyOf(await answer(101)).has('afterId')).toBe(false);
    expect(afterIdOf(await answer(102))).toBe('101');
    expect(uploads()).toEqual([]);

    // The owner shows the two by now: they are counted once, so one more file fits 51
    shown.push({ id: 101, size: 10 }, { id: 102, size: 10 });
    queue.add([fileOf('6.pdf', 'application/pdf', 10), fileOf('7.pdf', 'application/pdf', 10)], { ...target, maxFiles: 51 });
    await settle();
    expect(statuses().slice(5)).toEqual(['uploading', 'refused']);
    await answer(103);
  });

  it('cuts a batch by the bytes its record has left, refusing the rest of it once one file does not fit', async () => {
    queue.registerShownFiles(() => [{ id: 1, size: 900 }]);

    queue.add([fileOf('a.pdf', 'application/pdf', 50), fileOf('b.pdf', 'application/pdf', 60), fileOf('c.pdf', 'application/pdf', 10)], {
      ...target,
      maxTotalBytes: 1000,
    });
    await settle();
    await answer(2);

    expect(statuses()).toEqual(['uploaded', 'refused', 'refused']);
    expect(details().slice(1)).toEqual(Array(2).fill('The record has no room for this file.'));
    expect(uploads()).toEqual([]);
  });

  it('cancels a posting file by aborting its request and a waiting one by removing it; the files after them post', async () => {
    queue.add(
      [fileOf('a.pdf', 'application/pdf', 10), fileOf('b.pdf', 'application/pdf', 10), fileOf('c.pdf', 'application/pdf', 10)],
      target,
    );
    await settle();
    const [posting] = uploads();
    const [a, b] = queue.uploads();

    queue.cancel(a.id);
    queue.cancel(b.id);
    await settle();

    expect(posting.cancelled).toBe(true);
    expect(queue.uploads().map(upload => upload.file.name)).toEqual(['c.pdf']);
    const last = await answer(5);
    expect(nameOf(last)).toBe('c.pdf');
    expect(bodyOf(last).has('afterId')).toBe(false);
    expect(queue.isPosting()).toBe(false);
  });

  it('cancels every waiting and posting file at once, as a confirmed leave does', async () => {
    queue.add([fileOf('a.pdf', 'application/pdf', 10), fileOf('b.pdf', 'application/pdf', 10)], target);
    await settle();
    const [posting] = uploads();

    expect(queue.isPosting()).toBe(true);
    queue.cancelAll();
    await settle();

    expect(posting.cancelled).toBe(true);
    expect(queue.uploads()).toEqual([]);
    expect(queue.isPosting()).toBe(false);
  });

  it('shows a file preparing while its facts are read and its image treated, uploading once it posts, and cancels it there', async () => {
    queue.add([pngOf('a.png', 64, 48), pngOf('b.png', 64, 48)], target);

    expect(statuses()).toEqual(['preparing', 'waiting']);
    expect(queue.isPosting()).toBe(true);
    queue.cancel(queue.uploads()[0].id);
    await settle();

    expect(statuses()).toEqual(['uploading']);
    const request = await answer(12);
    expect(nameOf(request)).toBe('b.png');
    expect(statuses()).toEqual(['uploaded']);
  });

  it('A photo too large in pixels', async () => {
    expect(target.maxPixels).toBe(40_000_000);
    queue.add([pngOf('panorama.png', 12000, 4000), pngOf('photo.png', 6000, 4000)], target);
    await settle();

    expect(statuses()).toEqual(['refused', 'uploading']);
    expect(details()[0]).toBe('The image is larger than 40 megapixels.');
    expect(nameOf(await answer(1))).toBe('photo.png');
  });

  it("refuses an image over the target's maxPixels, whatever it is, naming that bound", async () => {
    queue.add([pngOf('photo.png', 6000, 4000), pngOf('small.png', 4000, 3000)], { ...target, maxPixels: 12_500_000 });
    await settle();

    expect(statuses()).toEqual(['refused', 'uploading']);
    expect(details()[0]).toBe('The image is larger than 12.5 megapixels.');
    expect(nameOf(await answer(1))).toBe('small.png');
  });

  it("posts an image's metadata beside it, as picked under keep", async () => {
    queue.add([pngOf('photo.png', 640, 480, makeTiff('Apple'))], target);
    await settle();

    const request = await answer(1);
    expect(JSON.parse(bodyOf(request).get('metadata') as string)).toEqual({ make: 'Apple' });
    expect((bodyOf(request).get('file') as File).type).toBe('image/png');
  });

  it('takes a picked image up to the source bound and posts what it becomes, with its metadata, refused above the stored bound', async () => {
    webpBrowser(4000, 3000, 200_000);
    const optimized: FileUploadTarget = { ...target, images: 'optimize' };

    queue.add(
      [
        pngOf('scan.png', 4000, 3000, makeTiff('Canon'), 3 * MIB),
        fileOf('plan.pdf', 'application/pdf', 3 * MIB),
        fileOf('huge.jpg', 'image/jpeg', 5 * MIB),
      ],
      optimized,
    );
    await settle();

    expect(details().slice(1)).toEqual(['The file is larger than 1 MB.', 'The file is larger than 4 MB.']);
    const request = await answer(1);
    const posted = bodyOf(request).get('file') as File;
    expect([posted.name, posted.type, posted.size]).toEqual(['scan.webp', 'image/webp', 200_000]);
    expect(JSON.parse(bodyOf(request).get('metadata') as string)).toEqual({ make: 'Canon' });

    webpBrowser(4000, 3000, 2 * MIB);
    queue.add([pngOf('poster.png', 4000, 3000, null, 3 * MIB)], optimized);
    await settle();

    expect(statuses().at(-1)).toBe('refused');
    expect(details().at(-1)).toBe('The file is larger than 1 MB.');
    expect(uploads()).toEqual([]);
  });
});

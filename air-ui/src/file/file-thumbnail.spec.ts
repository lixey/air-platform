import { afterEach, describe, expect, it, vi } from 'vitest';
import { uploadLimits } from '../../tests/upload-limits';
import { FIRST_FRAME_TIMEOUT_MS, thumbnailOf } from './file-thumbnail';

/** The largest thumbnail the server takes, as an upload's limits carry it. */
const MAX_BYTES = uploadLimits().thumbnailMaxBytes;

/** A bitmap of the size given, which records that it was closed. */
function bitmapOf(width: number, height: number): ImageBitmap & { isClosed: boolean } {
  const bitmap = { width, height, isClosed: false, close: () => (bitmap.isClosed = true) };
  return bitmap as unknown as ImageBitmap & { isClosed: boolean };
}

/**
 * A browser whose canvas encodes `encodes` and answers, for each quality asked, a blob of the size `sizeOf` says; any
 * other type it answers as PNG, as a browser that cannot encode it does.
 */
function browser(encodes: readonly string[], sizeOf: (quality: number) => number, resizes = true) {
  const drawn: { width: number; height: number }[] = [];
  const asked: { type: string; quality: number }[] = [];
  const decoded = bitmapOf(1600, 1200);
  // The file decodes to 1600 by 1200; a bitmap of the bitmap is resized, or a copy where the browser ignores the size
  const createImageBitmap = vi.fn((source: unknown, options?: ImageBitmapOptions) => {
    if (source !== decoded) {
      return Promise.resolve(decoded);
    }
    return Promise.resolve(resizes ? bitmapOf(options?.resizeWidth ?? 0, options?.resizeHeight ?? 0) : bitmapOf(1600, 1200));
  });
  class OffscreenCanvas {
    constructor(
      readonly width: number,
      readonly height: number,
    ) {}
    getContext() {
      return {
        fillStyle: '',
        imageSmoothingQuality: 'low',
        fillRect: vi.fn(),
        drawImage: (_image: unknown, _x: number, _y: number, width: number, height: number) => drawn.push({ width, height }),
      };
    }
    convertToBlob({ type, quality }: { type: string; quality: number }) {
      asked.push({ type, quality });
      const answered = encodes.includes(type) ? type : 'image/png';
      return Promise.resolve(new Blob([new Uint8Array(sizeOf(quality))], { type: answered }));
    }
  }
  vi.stubGlobal('createImageBitmap', createImageBitmap);
  vi.stubGlobal('OffscreenCanvas', OffscreenCanvas);
  return { drawn, asked, decoded, createImageBitmap };
}

const photo = new Blob([new Uint8Array(10)], { type: 'image/jpeg' });
const clip = new Blob([new Uint8Array(10)], { type: 'video/mp4' });

/**
 * The detached `<video>` elements a thumbnail makes, each with a picture of the size given, and the object URLs of the
 * files they load, which record their revoking.
 */
function videos(width = 1920, height = 1080) {
  const created: HTMLVideoElement[] = [];
  const create = document.createElement.bind(document);
  vi.spyOn(document, 'createElement').mockImplementation((tag: string, options?: ElementCreationOptions) => {
    const element = create(tag, options);
    if (element instanceof HTMLVideoElement) {
      Object.defineProperty(element, 'videoWidth', { value: width });
      Object.defineProperty(element, 'videoHeight', { value: height });
      element.load = vi.fn();
      created.push(element);
    }
    return element;
  });
  const revokeObjectURL = vi.fn();
  vi.stubGlobal('URL', { createObjectURL: () => 'blob:clip', revokeObjectURL });
  return { created, revokeObjectURL };
}

/** Lets the thumbnail's awaits run up to its next wait for the video. */
const turn = (): Promise<void> => new Promise(resolve => setTimeout(resolve));

describe('thumbnailOf', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('makes none of a file that is no raster image, or in a browser that draws no bitmap', async () => {
    expect(await thumbnailOf(new Blob(['%PDF'], { type: 'application/pdf' }), MAX_BYTES)).toBeNull();
    expect(await thumbnailOf(new Blob(['<svg/>'], { type: 'image/svg+xml' }), MAX_BYTES)).toBeNull();
    expect(await thumbnailOf(photo, MAX_BYTES)).toBeNull();
  });

  it('draws the image at most 320 pixels on its longer side and encodes it as WebP', async () => {
    const { drawn, asked, decoded, createImageBitmap } = browser(['image/webp', 'image/jpeg'], () => 30_000);

    const thumbnail = await thumbnailOf(photo, MAX_BYTES);

    expect(thumbnail?.type).toBe('image/webp');
    expect(createImageBitmap).toHaveBeenLastCalledWith(decoded, { resizeWidth: 320, resizeHeight: 240, resizeQuality: 'high' });
    expect(drawn).toEqual([{ width: 320, height: 240 }]);
    expect(asked).toEqual([{ type: 'image/webp', quality: 0.8 }]);
    expect(decoded.isClosed).toBe(true);
  });

  it('lowers the quality until the thumbnail fits, and takes JPEG where the browser cannot encode WebP', async () => {
    const { asked } = browser(['image/jpeg'], quality => (quality > 0.6 ? MAX_BYTES + 1 : MAX_BYTES));

    const thumbnail = await thumbnailOf(photo, MAX_BYTES);

    expect(thumbnail?.type).toBe('image/jpeg');
    expect(thumbnail?.size).toBe(MAX_BYTES);
    expect(asked).toEqual([
      { type: 'image/webp', quality: 0.8 },
      { type: 'image/jpeg', quality: 0.8 },
      { type: 'image/jpeg', quality: 0.65 },
      { type: 'image/jpeg', quality: 0.5 },
    ]);
  });

  it("holds the thumbnail to the bound it is given, the upload's thumbnailMaxBytes", async () => {
    const { asked } = browser(['image/webp'], quality => (quality > 0.5 ? 20_001 : 20_000));

    const thumbnail = await thumbnailOf(photo, 20_000);

    expect(thumbnail?.size).toBe(20_000);
    expect(asked).toEqual([
      { type: 'image/webp', quality: 0.8 },
      { type: 'image/webp', quality: 0.65 },
      { type: 'image/webp', quality: 0.5 },
    ]);
  });

  it('resizes on the canvas where the browser ignores the size asked, and sends none when no quality fits', async () => {
    const { drawn } = browser(['image/webp'], () => MAX_BYTES + 1, false);

    expect(await thumbnailOf(photo, MAX_BYTES)).toBeNull();
    expect(drawn[0]).toEqual({ width: 320, height: 240 });
  });

  it("draws an MP4's first frame from a detached muted video that loads its metadata alone, and lets the file go", async () => {
    const { drawn, asked, decoded, createImageBitmap } = browser(['image/webp'], () => 30_000);
    const { created, revokeObjectURL } = videos();

    const thumbnail = thumbnailOf(clip, MAX_BYTES);
    const [video] = created;
    expect(video.muted).toBe(true);
    expect(video.playsInline).toBe(true);
    expect(video.preload).toBe('metadata');
    expect(video.isConnected).toBe(false);
    expect(video.getAttribute('src')).toBe('blob:clip');
    video.dispatchEvent(new Event('loadedmetadata'));
    await turn();
    // Seeked to its start: a seek the browser answers with the first frame
    expect(video.currentTime).toBeGreaterThan(0);
    expect(video.currentTime).toBeLessThan(0.01);
    video.dispatchEvent(new Event('seeked'));

    expect((await thumbnail)?.type).toBe('image/webp');
    expect(createImageBitmap).toHaveBeenCalledWith(video);
    expect(drawn).toEqual([{ width: 320, height: 240 }]);
    expect(asked).toEqual([{ type: 'image/webp', quality: 0.8 }]);
    expect(decoded.isClosed).toBe(true);
    expect(video.hasAttribute('src')).toBe(false);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:clip');
  });

  it('makes none of a video the browser cannot decode, and lets the file go', async () => {
    const { createImageBitmap } = browser(['image/webp'], () => 30_000);
    const { created, revokeObjectURL } = videos();

    const thumbnail = thumbnailOf(clip, MAX_BYTES);
    created[0].dispatchEvent(new Event('error'));

    expect(await thumbnail).toBeNull();
    expect(createImageBitmap).not.toHaveBeenCalled();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:clip');
  });

  it('makes none of a video without a picture, or of one that gives no frame in time', async () => {
    browser(['image/webp'], () => 30_000);
    const { created } = videos(0, 0);

    const soundOnly = thumbnailOf(clip, MAX_BYTES);
    created[0].dispatchEvent(new Event('loadedmetadata'));
    await turn();
    created[0].dispatchEvent(new Event('seeked'));
    expect(await soundOnly).toBeNull();

    vi.useFakeTimers();
    const stalled = thumbnailOf(clip, MAX_BYTES);
    await vi.advanceTimersByTimeAsync(FIRST_FRAME_TIMEOUT_MS);
    expect(await stalled).toBeNull();
    expect(created[1].hasAttribute('src')).toBe(false);
  });
});

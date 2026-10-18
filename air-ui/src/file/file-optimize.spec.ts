import { afterEach, describe, expect, it, vi } from 'vitest';
import { type ImageRules, optimizeImage } from './file-optimize';
import type { ImageFacts } from './image-facts';

/** A bitmap of the size given, which records that it was closed. */
function bitmapOf(width: number, height: number): ImageBitmap & { isClosed: boolean } {
  const bitmap = { width, height, isClosed: false, close: () => (bitmap.isClosed = true) };
  return bitmap as unknown as ImageBitmap & { isClosed: boolean };
}

interface Browser {
  /** The size the file decodes to; the decoding fails when absent. */
  readonly decodes?: { width: number; height: number };
  /** The types the canvas encodes; any other it answers as PNG, as a browser without that encoder does. */
  readonly encodes: readonly string[];
  /** The size of the blob answered for each type. */
  readonly sizeOf: (type: string) => number;
}

/** A browser whose `createImageBitmap` and `OffscreenCanvas` record what they were asked. */
function browser({ decodes, encodes, sizeOf }: Browser) {
  const canvases: { width: number; height: number }[] = [];
  const drawn: { width: number; height: number }[] = [];
  const turns: number[][] = [];
  const fills: string[] = [];
  const asked: { type: string; quality: number }[] = [];
  const decoded = bitmapOf(decodes?.width ?? 0, decodes?.height ?? 0);
  const resized: (ImageBitmap & { isClosed: boolean })[] = [];
  const createImageBitmap = vi.fn((source: unknown, options?: ImageBitmapOptions) => {
    if (source !== decoded) {
      return decodes ? Promise.resolve(decoded) : Promise.reject(new Error('The browser cannot decode the image.'));
    }
    const bitmap = bitmapOf(options?.resizeWidth ?? 0, options?.resizeHeight ?? 0);
    resized.push(bitmap);
    return Promise.resolve(bitmap);
  });
  class OffscreenCanvas {
    constructor(
      readonly width: number,
      readonly height: number,
    ) {
      canvases.push({ width, height });
    }
    getContext() {
      const context = {
        fillStyle: '',
        imageSmoothingQuality: 'low',
        fillRect: () => fills.push(context.fillStyle),
        setTransform: (...values: number[]) => turns.push(values),
        drawImage: (_image: unknown, _x: number, _y: number, width: number, height: number) => drawn.push({ width, height }),
      };
      return context;
    }
    convertToBlob({ type, quality }: { type: string; quality: number }) {
      asked.push({ type, quality });
      const answered = encodes.includes(type) ? type : 'image/png';
      return Promise.resolve(new Blob([new Uint8Array(sizeOf(answered))], { type: answered }));
    }
  }
  vi.stubGlobal('createImageBitmap', createImageBitmap);
  vi.stubGlobal('OffscreenCanvas', OffscreenCanvas);
  return { canvases, drawn, turns, fills, asked, decoded, resized, createImageBitmap };
}

const WEB_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

function rules(images: ImageRules['images'], accept: readonly string[] = WEB_TYPES): ImageRules {
  return { images, imageMaxSide: 2048, imageQuality: 0.8, accept };
}

function fileOf(name: string, type: string, size: number): File {
  return new File([new Uint8Array(size)], name, { type, lastModified: 1_700_000_000_000 });
}

/** A phone's photo, 4032 by 3024 pixels as stored, taken on its side. */
const PHOTO_FACTS: ImageFacts = { width: 4032, height: 3024, orientation: 6, metadata: { make: 'Apple', latitude: 50.46 } };
const photo = (): File => fileOf('IMG_0042.jpg', 'image/jpeg', 480_000);

/** Gives `<img>` the `decode` jsdom lacks. */
function imageDecodes(decode: () => Promise<void>): void {
  Object.defineProperty(HTMLImageElement.prototype, 'decode', { value: decode, configurable: true });
}

describe('optimizeImage', () => {
  afterEach(() => {
    Reflect.deleteProperty(HTMLImageElement.prototype, 'decode');
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("makes a phone's photo an upright WebP of 1536 by 2048 at the quality asked", async () => {
    const { canvases, drawn, turns, asked, decoded, resized, createImageBitmap } = browser({
      decodes: { width: 3024, height: 4032 },
      encodes: ['image/webp', 'image/jpeg'],
      sizeOf: () => 200_000,
    });

    const posted = await optimizeImage(photo(), rules('optimize'), PHOTO_FACTS);

    expect(createImageBitmap).toHaveBeenCalledWith(expect.any(File), { imageOrientation: 'from-image' });
    expect(posted.name).toBe('IMG_0042.webp');
    expect(posted.type).toBe('image/webp');
    expect(posted.size).toBe(200_000);
    expect(posted.lastModified).toBe(1_700_000_000_000);
    expect(canvases).toEqual([{ width: 1536, height: 2048 }]);
    expect(drawn).toEqual([{ width: 1536, height: 2048 }]);
    expect(turns).toEqual([]);
    expect(asked).toEqual([{ type: 'image/webp', quality: 0.8 }]);
    expect(decoded.isClosed).toBe(true);
    expect(resized.every(bitmap => bitmap.isClosed)).toBe(true);
  });

  it('names the posted image by the picked base and its own extension, a name ending in a dot losing that dot', async () => {
    browser({ decodes: { width: 3024, height: 4032 }, encodes: ['image/webp'], sizeOf: () => 200_000 });

    const trailing = await optimizeImage(fileOf('photo.', 'image/jpeg', 480_000), rules('optimize'), PHOTO_FACTS);
    const doubled = await optimizeImage(fileOf('scan.final.jpeg', 'image/jpeg', 480_000), rules('optimize'), PHOTO_FACTS);
    const hidden = await optimizeImage(fileOf('.photo', 'image/jpeg', 480_000), rules('optimize'), PHOTO_FACTS);

    expect(trailing.name).toBe('photo.webp');
    expect(doubled.name).toBe('scan.final.webp');
    expect(hidden.name).toBe('.photo.webp');
  });

  it('turns the photo on the canvas where the browser decoded it as stored', async () => {
    const { canvases, drawn, turns } = browser({
      decodes: { width: 4032, height: 3024 },
      encodes: ['image/webp'],
      sizeOf: () => 200_000,
    });

    const posted = await optimizeImage(photo(), rules('optimize'), PHOTO_FACTS);

    expect(posted.name).toBe('IMG_0042.webp');
    expect(canvases).toEqual([{ width: 1536, height: 2048 }]);
    // Drawn as stored, 2048 by 1536, turned a quarter clockwise onto the upright canvas
    expect(drawn).toEqual([{ width: 2048, height: 1536 }]);
    expect(turns).toEqual([[0, 1, -1, 0, 1536, 0]]);
  });

  it('takes a JPEG with a white ground where the browser cannot encode WebP, or the upload does not accept it', async () => {
    const { asked, fills } = browser({ decodes: { width: 3024, height: 4032 }, encodes: ['image/jpeg'], sizeOf: () => 300_000 });

    const posted = await optimizeImage(photo(), rules('optimize'), PHOTO_FACTS);

    expect(posted.name).toBe('IMG_0042.jpg');
    expect(posted.type).toBe('image/jpeg');
    expect(asked).toEqual([
      { type: 'image/webp', quality: 0.8 },
      { type: 'image/jpeg', quality: 0.8 },
    ]);
    expect(fills).toEqual(['#fff']);

    asked.length = 0;
    const withoutWebp = await optimizeImage(photo(), rules('optimize', ['image/jpeg', 'image/png']), PHOTO_FACTS);
    expect(withoutWebp.type).toBe('image/jpeg');
    expect(asked).toEqual([{ type: 'image/jpeg', quality: 0.8 }]);
  });

  it('posts a larger encoding only when the picked image exceeds the box', async () => {
    browser({ decodes: { width: 3024, height: 4032 }, encodes: ['image/webp'], sizeOf: () => 600_000 });
    const larger = await optimizeImage(photo(), rules('optimize'), PHOTO_FACTS);
    expect(larger.name).toBe('IMG_0042.webp');

    vi.unstubAllGlobals();
    const { canvases } = browser({ decodes: { width: 1600, height: 1200 }, encodes: ['image/webp'], sizeOf: () => 600_000 });
    const small = photo();
    expect(await optimizeImage(small, rules('optimize'), null)).toBe(small);
    // Never scaled up
    expect(canvases).toEqual([{ width: 1600, height: 1200 }]);
  });

  it('makes a screenshot a lossless WebP when that is smaller, else posts the PNG, under optimize and lossless', async () => {
    const screenshot = fileOf('Screen Shot.png', 'image/png', 500_000);
    const { asked, canvases } = browser({
      decodes: { width: 1200, height: 800 },
      encodes: ['image/webp', 'image/png'],
      sizeOf: () => 300_000,
    });

    for (const images of ['optimize', 'lossless'] as const) {
      const posted = await optimizeImage(screenshot, rules(images), null);
      expect(posted.name).toBe('Screen Shot.webp');
      expect(posted.type).toBe('image/webp');
    }
    expect(asked).toEqual([
      { type: 'image/webp', quality: 1 },
      { type: 'image/webp', quality: 1 },
    ]);
    expect(canvases).toEqual([
      { width: 1200, height: 800 },
      { width: 1200, height: 800 },
    ]);

    vi.unstubAllGlobals();
    browser({ decodes: { width: 1200, height: 800 }, encodes: ['image/webp', 'image/png'], sizeOf: () => 700_000 });
    expect(await optimizeImage(screenshot, rules('optimize'), null)).toBe(screenshot);
    expect(await optimizeImage(screenshot, rules('lossless'), null)).toBe(screenshot);
  });

  it('keeps a PNG a PNG where the browser answers PNG for WebP, as Safari does', async () => {
    const screenshot = fileOf('shot.png', 'image/png', 500_000);
    const { asked } = browser({ decodes: { width: 3000, height: 2000 }, encodes: ['image/png'], sizeOf: () => 400_000 });

    expect(await optimizeImage(screenshot, rules('lossless'), null)).toBe(screenshot);
    const optimized = await optimizeImage(screenshot, rules('optimize'), null);
    expect(optimized.name).toBe('shot.png');
    expect(optimized.type).toBe('image/png');
    expect(asked).toEqual([
      { type: 'image/webp', quality: 1 },
      { type: 'image/webp', quality: 1 },
      { type: 'image/png', quality: 1 },
    ]);
  });

  it('posts as picked under keep, a JPEG under lossless, and a GIF, an SVG or a PDF under every rule, decoding none', async () => {
    const { createImageBitmap } = browser({ decodes: { width: 4032, height: 3024 }, encodes: ['image/webp'], sizeOf: () => 1 });
    const gif = fileOf('cat.gif', 'image/gif', 90_000);
    const svg = fileOf('logo.svg', 'image/svg+xml', 2_000);
    const pdf = fileOf('plan.pdf', 'application/pdf', 90_000);
    const picked = photo();

    expect(await optimizeImage(picked, rules('keep'), PHOTO_FACTS)).toBe(picked);
    expect(await optimizeImage(picked, rules('lossless'), PHOTO_FACTS)).toBe(picked);
    for (const images of ['keep', 'lossless', 'optimize'] as const) {
      for (const file of [gif, svg, pdf]) {
        expect(await optimizeImage(file, rules(images), null)).toBe(file);
      }
    }
    expect(await optimizeImage(picked, rules('optimize', ['image/gif']), PHOTO_FACTS)).toBe(picked);
    expect(createImageBitmap).not.toHaveBeenCalled();
  });

  it('decodes in an <img> where createImageBitmap fails, and lets its URL go', async () => {
    const { canvases } = browser({ encodes: ['image/webp'], sizeOf: () => 100_000 });
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { createObjectURL: () => 'blob:photo', revokeObjectURL });
    imageDecodes(() => Promise.resolve());
    vi.spyOn(HTMLImageElement.prototype, 'naturalWidth', 'get').mockReturnValue(3024);
    vi.spyOn(HTMLImageElement.prototype, 'naturalHeight', 'get').mockReturnValue(4032);

    const posted = await optimizeImage(photo(), rules('optimize'), PHOTO_FACTS);

    expect(posted.name).toBe('IMG_0042.webp');
    expect(canvases).toEqual([{ width: 1536, height: 2048 }]);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:photo');
  });

  it('posts the picked image when the browser cannot decode or encode it', async () => {
    browser({ encodes: ['image/webp'], sizeOf: () => 100_000 });
    vi.stubGlobal('URL', { createObjectURL: () => 'blob:photo', revokeObjectURL: vi.fn() });
    imageDecodes(() => Promise.reject(new Error('The browser cannot decode the image.')));
    const picked = photo();

    expect(await optimizeImage(picked, rules('optimize'), PHOTO_FACTS)).toBe(picked);

    vi.unstubAllGlobals();
    const { decoded } = browser({ decodes: { width: 3024, height: 4032 }, encodes: [], sizeOf: () => 100_000 });
    expect(await optimizeImage(picked, rules('optimize', ['image/jpeg', 'image/webp']), PHOTO_FACTS)).toBe(picked);
    expect(decoded.isClosed).toBe(true);

    vi.stubGlobal(
      'OffscreenCanvas',
      class {
        getContext() {
          throw new Error('No canvas.');
        }
      },
    );
    expect(await optimizeImage(picked, rules('optimize'), PHOTO_FACTS)).toBe(picked);
  });
});

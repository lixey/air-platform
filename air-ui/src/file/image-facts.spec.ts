import { describe, expect, it } from 'vitest';
import { FACTS_HEAD_BYTES, imageFactsOf } from './image-facts';

const BYTE = 1;
const SHORT = 3;
const LONG = 4;

interface Tag {
  readonly id: number;
  readonly type: number;
  readonly value: readonly number[];
}

/** A byte writer in one byte order that grows as it is written. */
class Writer {
  readonly bytes: number[] = [];

  constructor(private readonly isLittle = false) {}

  u8(...values: number[]): this {
    this.bytes.push(...values.map(value => value & 0xff));
    return this;
  }

  u16(value: number): this {
    return this.isLittle ? this.u8(value, value >> 8) : this.u8(value >> 8, value);
  }

  u32(value: number): this {
    const bytes = [value >>> 24, value >>> 16, value >>> 8, value];
    return this.u8(...(this.isLittle ? bytes.reverse() : bytes));
  }

  text(value: string): this {
    return this.u8(...[...value].map(char => char.charCodeAt(0)));
  }

  put(bytes: ArrayLike<number>): this {
    for (let index = 0; index < bytes.length; index++) {
      this.bytes.push(bytes[index]);
    }
    return this;
  }
}

/** A TIFF header with IFD0 entries sorted by tag id. */
function tiffOf(isLittle: boolean, ifd0: Tag[]): number[] {
  const head = new Writer(isLittle)
    .text(isLittle ? 'II' : 'MM')
    .u16(42)
    .u32(8)
    .u16(ifd0.length);
  for (const tag of [...ifd0].sort((a, b) => a.id - b.id)) {
    const val = new Writer(isLittle);
    for (const item of tag.value) {
      if (tag.type === BYTE) {
        val.u8(item);
      } else if (tag.type === SHORT) {
        val.u16(item);
      } else {
        val.u32(item);
      }
    }
    head
      .u16(tag.id)
      .u16(tag.type)
      .u32(tag.value.length)
      .put([...val.bytes, 0, 0, 0, 0].slice(0, 4));
  }
  head.u32(0);
  return head.bytes;
}

function orientationTiff(isLittle: boolean, orientation: number, type = SHORT): number[] {
  return tiffOf(isLittle, [
    { id: 0x0100, type: SHORT, value: [4032] },
    { id: 0x0112, type, value: [orientation] },
  ]);
}

/** A JPEG: SOI, an APP1 segment with the EXIF given, others given, SOF0 of the size, then SOS and a little scan. */
function jpegOf(width: number, height: number, tiff: number[] | null, before: number[] = []): Blob {
  const writer = new Writer().u8(0xff, 0xd8);
  if (tiff) {
    writer
      .u8(0xff, 0xe1)
      .u16(2 + 6 + tiff.length)
      .text('Exif\0\0')
      .put(tiff);
  }
  writer.put(before);
  writer.u8(0xff, 0xc0).u16(17).u8(8).u16(height).u16(width).u8(3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1);
  writer.u8(0xff, 0xda).u16(8).u8(1, 1, 0, 0, 0x3f, 0).u8(1, 2, 3, 4);
  return new Blob([new Uint8Array(writer.bytes)], { type: 'image/jpeg' });
}

/** A PNG chunk; its CRC is left zero, which the reader never checks. */
function chunkOf(type: string, data: number[]): number[] {
  return new Writer().u32(data.length).text(type).put(data).u32(0).bytes;
}

function pngOf(width: number, height: number, tiff: number[] | null): Blob {
  const writer = new Writer().u8(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a);
  writer.put(chunkOf('IHDR', new Writer().u32(width).u32(height).u8(8, 6, 0, 0, 0).bytes));
  if (tiff) {
    writer.put(chunkOf('eXIf', tiff));
  }
  writer.put(chunkOf('IDAT', [1, 2, 3])).put(chunkOf('IEND', []));
  return new Blob([new Uint8Array(writer.bytes)], { type: 'image/png' });
}

/** A RIFF chunk of a WebP, padded to an even length. */
function riffChunkOf(type: string, data: number[]): number[] {
  const writer = new Writer(true).text(type).u32(data.length).put(data);
  return data.length % 2 === 1 ? writer.u8(0).bytes : writer.bytes;
}

function webpOf(chunks: number[][]): Blob {
  const body = chunks.flat();
  const writer = new Writer(true)
    .text('RIFF')
    .u32(4 + body.length)
    .text('WEBP')
    .put(body);
  return new Blob([new Uint8Array(writer.bytes)], { type: 'image/webp' });
}

/** A VP8X chunk of the canvas size given, with the EXIF flag. */
function vp8xOf(width: number, height: number): number[] {
  const w = width - 1;
  const h = height - 1;
  return riffChunkOf('VP8X', [0x08, 0, 0, 0, w, w >> 8, w >> 16, h, h >> 8, h >> 16]);
}

describe('imageFactsOf', () => {
  it("reads a phone JPEG's size and its orientation 6, in either byte order", async () => {
    for (const isLittle of [false, true]) {
      const facts = await imageFactsOf(jpegOf(4032, 3024, orientationTiff(isLittle, 6)));

      expect(facts).toEqual({ width: 4032, height: 3024, orientation: 6 });
    }
  });

  it('reads orientation stored as BYTE or LONG and ignores out-of-range values', async () => {
    expect(await imageFactsOf(jpegOf(640, 480, orientationTiff(false, 3, BYTE)))).toEqual({
      width: 640,
      height: 480,
      orientation: 3,
    });
    expect(await imageFactsOf(jpegOf(640, 480, orientationTiff(true, 8, LONG)))).toEqual({
      width: 640,
      height: 480,
      orientation: 8,
    });
    expect(await imageFactsOf(jpegOf(640, 480, orientationTiff(false, 9)))).toEqual({
      width: 640,
      height: 480,
      orientation: 1,
    });
  });

  it('reads the size of a JPEG without EXIF past its other segments, with orientation 1', async () => {
    const icc = new Writer()
      .u8(0xff, 0xe2)
      .u16(2 + 100)
      .put(new Uint8Array(100)).bytes;

    expect(await imageFactsOf(jpegOf(800, 600, null, icc))).toEqual({ width: 800, height: 600, orientation: 1 });
  });

  it("reads a PNG's IHDR and the orientation of its eXIf chunk", async () => {
    expect(await imageFactsOf(pngOf(1920, 1080, orientationTiff(false, 5)))).toEqual({
      width: 1920,
      height: 1080,
      orientation: 5,
    });
    expect(await imageFactsOf(pngOf(1, 1, null))).toEqual({ width: 1, height: 1, orientation: 1 });
  });

  it("reads a WebP's canvas from VP8X and the orientation of its EXIF chunk, with or without the JPEG's prefix", async () => {
    const tiff = orientationTiff(true, 8);
    const expected = { width: 5000, height: 3000, orientation: 8 };

    expect(await imageFactsOf(webpOf([vp8xOf(5000, 3000), riffChunkOf('EXIF', tiff)]))).toEqual(expected);
    const prefixed = [...new Writer().text('Exif\0\0').bytes, ...tiff];
    expect(await imageFactsOf(webpOf([vp8xOf(5000, 3000), riffChunkOf('EXIF', prefixed)]))).toEqual(expected);
  });

  it('reads the size of a simple lossy and a lossless WebP', async () => {
    const lossy = riffChunkOf('VP8 ', new Writer(true).u8(0, 0, 0, 0x9d, 0x01, 0x2a).u16(640).u16(480).u8(0, 0).bytes);
    const w = 1023;
    const h = 767;
    const bits = w | (h << 14);
    const lossless = riffChunkOf('VP8L', new Writer(true).u8(0x2f).u32(bits).bytes);

    expect(await imageFactsOf(webpOf([lossy]))).toEqual({ width: 640, height: 480, orientation: 1 });
    expect(await imageFactsOf(webpOf([lossless]))).toEqual({ width: 1024, height: 768, orientation: 1 });
  });

  it('keeps the size and defaults orientation to 1 when EXIF is malformed or points outside itself', async () => {
    const badOrder = [...new Writer().text('XX').u16(42).u32(8).bytes, 0, 0];
    const outside = new Writer().text('MM').u16(42).u32(0x7fff_fff0).bytes;
    const cut = orientationTiff(false, 6).slice(0, 14);

    for (const exif of [badOrder, outside, cut]) {
      expect(await imageFactsOf(jpegOf(640, 480, exif))).toEqual({ width: 640, height: 480, orientation: 1 });
    }
  });

  it('tells nothing of a truncated image, one whose size lies past the bytes read, or a file of another kind', async () => {
    const whole = new Uint8Array(await blobBytes(jpegOf(4032, 3024, orientationTiff(false, 6))));
    const truncated = new Blob([whole.slice(0, 20)], { type: 'image/jpeg' });
    const padding = new Writer().u8(0xff, 0xe2).u16(0xfffe).put(new Uint8Array(0xfffc)).bytes;
    const far = jpegOf(640, 480, null, Array.from({ length: 5 }, () => padding).flat());

    expect(far.size).toBeGreaterThan(FACTS_HEAD_BYTES);
    expect(await imageFactsOf(truncated)).toBeNull();
    expect(await imageFactsOf(far)).toBeNull();
    expect(await imageFactsOf(new Blob([new Uint8Array([0x89, 0x50, 0x4e])], { type: 'image/png' }))).toBeNull();
    expect(await imageFactsOf(new Blob(['GIF89a'], { type: 'image/gif' }))).toBeNull();
    expect(await imageFactsOf(new Blob([], { type: 'image/jpeg' }))).toBeNull();
  });
});

/** A blob's bytes, read the way jsdom offers. */
function blobBytes(blob: Blob): Promise<ArrayBuffer> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as ArrayBuffer);
    reader.onerror = () => reject(reader.error);
    reader.readAsArrayBuffer(blob);
  });
}

import { describe, expect, it } from 'vitest';
import { FACTS_HEAD_BYTES, imageFactsOf } from './image-facts';

const BYTE = 1;
const ASCII = 2;
const SHORT = 3;
const LONG = 4;
const RATIONAL = 5;

/** A TIFF tag: ASCII takes a string, RATIONAL numerator and denominator pairs, flattened. */
interface Tag {
  readonly id: number;
  readonly type: number;
  readonly value: string | readonly number[];
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

/** The bytes of a tag's value and its count of values. */
function valueOf(tag: Tag, isLittle: boolean): { bytes: number[]; count: number } {
  const writer = new Writer(isLittle);
  if (typeof tag.value === 'string') {
    writer.text(tag.value).u8(0);
    return { bytes: writer.bytes, count: tag.value.length + 1 };
  }
  for (const value of tag.value) {
    if (tag.type === BYTE) {
      writer.u8(value);
    } else if (tag.type === SHORT) {
      writer.u16(value);
    } else {
      writer.u32(value);
    }
  }
  return { bytes: writer.bytes, count: tag.type === RATIONAL ? tag.value.length / 2 : tag.value.length };
}

/** A TIFF structure of IFD0 and, where given, the Exif IFD and the GPS IFD it points to. */
function tiffOf(isLittle: boolean, ifd0: Tag[], exif: Tag[] = [], gps: Tag[] = []): number[] {
  const sizeOf = (count: number): number => 2 + count * 12 + 4;
  const ifd0Count = ifd0.length + (exif.length > 0 ? 1 : 0) + (gps.length > 0 ? 1 : 0);
  const exifAt = 8 + sizeOf(ifd0Count);
  const gpsAt = exifAt + (exif.length > 0 ? sizeOf(exif.length) : 0);
  let dataAt = gpsAt + (gps.length > 0 ? sizeOf(gps.length) : 0);
  const pointers: Tag[] = [
    ...(exif.length > 0 ? [{ id: 0x8769, type: LONG, value: [exifAt] }] : []),
    ...(gps.length > 0 ? [{ id: 0x8825, type: LONG, value: [gpsAt] }] : []),
  ];
  const head = new Writer(isLittle)
    .text(isLittle ? 'II' : 'MM')
    .u16(42)
    .u32(8);
  const data = new Writer(isLittle);
  for (const tags of [[...ifd0, ...pointers], exif, gps].filter(list => list.length > 0)) {
    head.u16(tags.length);
    for (const tag of [...tags].sort((a, b) => a.id - b.id)) {
      const { bytes, count } = valueOf(tag, isLittle);
      head.u16(tag.id).u16(tag.type).u32(count);
      if (bytes.length <= 4) {
        head.put([...bytes, 0, 0, 0, 0].slice(0, 4));
      } else {
        head.u32(dataAt);
        data.put(bytes);
        dataAt += bytes.length;
      }
    }
    head.u32(0);
  }
  return [...head.bytes, ...data.bytes];
}

/** A phone's photo: the camera, the moment, the exposure, orientation 6 and where it was taken. */
function phoneTiff(isLittle: boolean): number[] {
  return tiffOf(
    isLittle,
    [
      { id: 0x010f, type: ASCII, value: 'Apple' },
      { id: 0x0110, type: ASCII, value: 'iPhone 15 Pro' },
      { id: 0x0112, type: SHORT, value: [6] },
      { id: 0x0131, type: ASCII, value: '17.4' },
    ],
    [
      { id: 0x829a, type: RATIONAL, value: [1, 120] },
      { id: 0x829d, type: RATIONAL, value: [178, 100] },
      { id: 0x8827, type: SHORT, value: [80] },
      { id: 0x9003, type: ASCII, value: '2024:05:01 12:30:45' },
      { id: 0x920a, type: RATIONAL, value: [6860, 1000] },
      { id: 0xa434, type: ASCII, value: 'iPhone 15 Pro back camera' },
    ],
    [
      { id: 0x0001, type: ASCII, value: 'N' },
      { id: 0x0002, type: RATIONAL, value: [50, 1, 27, 1, 36, 1] },
      { id: 0x0003, type: ASCII, value: 'W' },
      { id: 0x0004, type: RATIONAL, value: [30, 1, 31, 1, 1800, 100] },
      { id: 0x0005, type: BYTE, value: [1] },
      { id: 0x0006, type: RATIONAL, value: [125, 10] },
    ],
  );
}

const PHONE_METADATA = {
  make: 'Apple',
  model: 'iPhone 15 Pro',
  lens: 'iPhone 15 Pro back camera',
  takenAt: '2024-05-01T12:30:45',
  exposureTime: '1/120',
  fNumber: 1.8,
  iso: 80,
  focalLength: 6.9,
  orientation: 6,
  software: '17.4',
  latitude: 50.46,
  longitude: -30.5216667,
  altitude: -12.5,
};

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
  it("reads a phone JPEG's size, its orientation 6 and its tags with the place, in either byte order", async () => {
    for (const isLittle of [false, true]) {
      const facts = await imageFactsOf(jpegOf(4032, 3024, phoneTiff(isLittle)));

      expect(facts).toEqual({ width: 4032, height: 3024, orientation: 6, metadata: PHONE_METADATA });
    }
  });

  it('reads a place south of the equator, east of Greenwich and above the sea', async () => {
    const tiff = tiffOf(
      false,
      [],
      [],
      [
        { id: 0x0001, type: ASCII, value: 'S' },
        { id: 0x0002, type: RATIONAL, value: [33, 1, 51, 1, 36, 1] },
        { id: 0x0003, type: ASCII, value: 'E' },
        { id: 0x0004, type: RATIONAL, value: [151, 1, 12, 1, 1800, 100] },
        { id: 0x0005, type: BYTE, value: [0] },
        { id: 0x0006, type: RATIONAL, value: [585, 10] },
      ],
    );

    expect(await imageFactsOf(jpegOf(640, 480, tiff))).toEqual({
      width: 640,
      height: 480,
      orientation: 1,
      metadata: { latitude: -33.86, longitude: 151.205, altitude: 58.5 },
    });
  });

  it('reads the size of a JPEG without EXIF past its other segments, with orientation 1 and no metadata', async () => {
    const icc = new Writer()
      .u8(0xff, 0xe2)
      .u16(2 + 100)
      .put(new Uint8Array(100)).bytes;

    expect(await imageFactsOf(jpegOf(800, 600, null, icc))).toEqual({ width: 800, height: 600, orientation: 1, metadata: null });
  });

  it("reads a PNG's IHDR and the tags of its eXIf chunk", async () => {
    const tiff = tiffOf(false, [
      { id: 0x0131, type: ASCII, value: 'Screenshot' },
      { id: 0x0112, type: SHORT, value: [1] },
    ]);

    expect(await imageFactsOf(pngOf(1920, 1080, tiff))).toEqual({
      width: 1920,
      height: 1080,
      orientation: 1,
      metadata: { software: 'Screenshot', orientation: 1 },
    });
    expect(await imageFactsOf(pngOf(1, 1, null))).toEqual({ width: 1, height: 1, orientation: 1, metadata: null });
  });

  it("reads a WebP's canvas from VP8X and the tags of its EXIF chunk, with or without the JPEG's prefix", async () => {
    const tiff = tiffOf(true, [
      { id: 0x010f, type: ASCII, value: 'Canon' },
      { id: 0x0112, type: SHORT, value: [8] },
    ]);
    const expected = { width: 5000, height: 3000, orientation: 8, metadata: { make: 'Canon', orientation: 8 } };

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

    expect(await imageFactsOf(webpOf([lossy]))).toEqual({ width: 640, height: 480, orientation: 1, metadata: null });
    expect(await imageFactsOf(webpOf([lossless]))).toEqual({ width: 1024, height: 768, orientation: 1, metadata: null });
  });

  it('keeps the size and drops the tags of EXIF that is malformed or points outside itself', async () => {
    const badOrder = [...new Writer().text('XX').u16(42).u32(8).bytes, 0, 0];
    const outside = new Writer().text('MM').u16(42).u32(0x7fff_fff0).bytes;
    const tiff = phoneTiff(false);
    const cut = tiff.slice(0, 40);

    for (const exif of [badOrder, outside]) {
      expect(await imageFactsOf(jpegOf(640, 480, exif))).toEqual({ width: 640, height: 480, orientation: 1, metadata: null });
    }
    const facts = await imageFactsOf(pngOf(640, 480, cut));
    expect(facts?.width).toBe(640);
    expect(facts?.metadata?.latitude).toBeUndefined();
  });

  it('tells nothing of a truncated image, one whose size lies past the bytes read, or a file of another kind', async () => {
    const whole = new Uint8Array(await blobBytes(jpegOf(4032, 3024, phoneTiff(false))));
    const truncated = new Blob([whole.slice(0, 60)], { type: 'image/jpeg' });
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

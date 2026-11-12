/** The leading bytes of a file the facts are read from: EXIF orientation and the size lie well within them. */
export const FACTS_HEAD_BYTES = 256 * 1024;

/** What an image's leading bytes tell without decoding it. */
export interface ImageFacts {
  /** The width in pixels as stored, before its orientation turns it. */
  readonly width: number;
  /** The height in pixels as stored, before its orientation turns it. */
  readonly height: number;
  /** The EXIF orientation, 1 to 8; 1 for an image that carries none. */
  readonly orientation: number;
}

/**
 * The size and the EXIF orientation of a JPEG, PNG or WebP image, read from its first `FACTS_HEAD_BYTES` without
 * decoding it: the size from a JPEG's SOF marker, a PNG's IHDR or a WebP's VP8X, VP8 or VP8L chunk, and the
 * orientation (`0x0112`) from IFD0 of a JPEG's APP1 segment, a PNG's eXIf chunk or a WebP's EXIF chunk, in either byte
 * order. Null for any other file and for one whose size does not lie within those bytes; EXIF that cannot be read
 * leaves the orientation `1`. It never throws.
 */
export async function imageFactsOf(file: Blob): Promise<ImageFacts | null> {
  try {
    return factsOf(new DataView(await bytesOf(file.slice(0, FACTS_HEAD_BYTES))));
  } catch {
    // A file that cannot be read, or bytes that end inside the size, tell nothing
    return null;
  }
}

const ORIENTATION_TAG = 0x0112;
const MARKER = { prefix: 0xff, tem: 0x01, rst0: 0xd0, soi: 0xd8, eoi: 0xd9, sos: 0xda, app1: 0xe1 } as const;
const TYPE = { byte: 1, short: 3, long: 4 } as const;
const JPEG_SIGNATURE = (MARKER.prefix << 8) | MARKER.soi;
const PNG_SIGNATURE = [0x89504e47, 0x0d0a1a0a] as const;
const TIFF_MAGIC = 42;
const VP8_START_CODE = 0x9d012a;
const VP8L_SIGNATURE = 0x2f;
const WEBP_SIDE_MASK = 0x3fff;
const JPEG_FRAMES = new Set([0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf]);

function bytesOf(blob: Blob): Promise<ArrayBuffer> {
  if (typeof blob.arrayBuffer === 'function') {
    return blob.arrayBuffer();
  }
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as ArrayBuffer);
    reader.onerror = () => reject(reader.error);
    reader.readAsArrayBuffer(blob);
  });
}

function factsOf(view: DataView): ImageFacts | null {
  if (view.byteLength >= 4 && view.getUint16(0) === JPEG_SIGNATURE) {
    return jpegFactsOf(view);
  }
  if (view.byteLength >= 8 && view.getUint32(0) === PNG_SIGNATURE[0] && view.getUint32(4) === PNG_SIGNATURE[1]) {
    return pngFactsOf(view);
  }
  if (view.byteLength >= 12 && textAt(view, 0, 4) === 'RIFF' && textAt(view, 8, 4) === 'WEBP') {
    return webpFactsOf(view);
  }
  return null;
}

function jpegFactsOf(view: DataView): ImageFacts | null {
  let orientation: number | null = null;
  let at = 2;
  while (at + 4 <= view.byteLength) {
    if (view.getUint8(at) !== MARKER.prefix) {
      return null;
    }
    const marker = view.getUint8(at + 1);
    if (marker === MARKER.prefix) {
      at += 1;
      continue;
    }
    if (marker === MARKER.tem || (marker >= MARKER.rst0 && marker <= MARKER.soi)) {
      at += 2;
      continue;
    }
    if (marker === MARKER.eoi || marker === MARKER.sos) {
      return null;
    }
    const length = view.getUint16(at + 2);
    const data = at + 4;
    if (length < 2) {
      return null;
    }
    if (marker === MARKER.app1 && orientation === null && length >= 8 && textAt(view, data, 6) === 'Exif\0\0') {
      orientation = orientationOf(view, data + 6, length - 8);
    }
    if (JPEG_FRAMES.has(marker)) {
      return factsWith(view.getUint16(data + 3), view.getUint16(data + 1), orientation);
    }
    at = data + length - 2;
  }
  return null;
}

function pngFactsOf(view: DataView): ImageFacts | null {
  if (textAt(view, 12, 4) !== 'IHDR') {
    return null;
  }
  const width = view.getUint32(16);
  const height = view.getUint32(20);
  let orientation: number | null = null;
  let at = 8;
  while (at + 8 <= view.byteLength) {
    const length = view.getUint32(at);
    const type = textAt(view, at + 4, 4);
    if (type === 'eXIf') {
      orientation = orientationOf(view, at + 8, length);
      break;
    }
    if (type === 'IEND') {
      break;
    }
    at += 12 + length;
  }
  return factsWith(width, height, orientation);
}

function webpFactsOf(view: DataView): ImageFacts | null {
  let width = 0;
  let height = 0;
  let orientation: number | null = null;
  let at = 12;
  while (at + 8 <= view.byteLength) {
    const type = textAt(view, at, 4);
    const length = view.getUint32(at + 4, true);
    const data = at + 8;
    if (type === 'VP8X') {
      width = 1 + uint24At(view, data + 4);
      height = 1 + uint24At(view, data + 7);
    } else if (type === 'VP8 ' && width === 0 && view.getUint32(data + 3) >>> 8 === VP8_START_CODE) {
      width = view.getUint16(data + 6, true) & WEBP_SIDE_MASK;
      height = view.getUint16(data + 8, true) & WEBP_SIDE_MASK;
    } else if (type === 'VP8L' && width === 0 && view.getUint8(data) === VP8L_SIGNATURE) {
      const bits = view.getUint32(data + 1, true);
      width = (bits & WEBP_SIDE_MASK) + 1;
      height = ((bits >>> 14) & WEBP_SIDE_MASK) + 1;
    } else if (type === 'EXIF') {
      const skip = length >= 6 && textAt(view, data, 6) === 'Exif\0\0' ? 6 : 0;
      orientation = orientationOf(view, data + skip, length - skip);
    }
    at = data + length + (length % 2);
  }
  return factsWith(width, height, orientation);
}

function factsWith(width: number, height: number, orientation: number | null): ImageFacts | null {
  if (width <= 0 || height <= 0) {
    return null;
  }
  return { width, height, orientation: orientation ?? 1 };
}

/** Reads IFD0's orientation tag (`0x0112`), 1 to 8, from the TIFF header at `start`; null when absent or malformed. */
function orientationOf(view: DataView, start: number, length: number): number | null {
  try {
    const end = start + Math.min(length, view.byteLength - start);
    if (start < 0 || end - start < 8) {
      return null;
    }
    const order = textAt(view, start, 2);
    if (order !== 'II' && order !== 'MM') {
      return null;
    }
    const isLittle = order === 'II';
    if (view.getUint16(start + 2, isLittle) !== TIFF_MAGIC) {
      return null;
    }
    const ifd0 = view.getUint32(start + 4, isLittle);
    const at = start + ifd0;
    if (ifd0 < 8 || at + 2 > end) {
      return null;
    }
    const count = view.getUint16(at, isLittle);
    for (let index = 0; index < count && at + 2 + (index + 1) * 12 <= end; index++) {
      const entry = at + 2 + index * 12;
      if (view.getUint16(entry, isLittle) !== ORIENTATION_TAG) {
        continue;
      }
      const type = view.getUint16(entry + 2, isLittle);
      const values = view.getUint32(entry + 4, isLittle);
      const unitBytes = type === TYPE.short ? 2 : type === TYPE.long ? 4 : type === TYPE.byte ? 1 : 0;
      if (values < 1 || unitBytes === 0) {
        return null;
      }
      const valueAt = unitBytes * values <= 4 ? entry + 8 : start + view.getUint32(entry + 8, isLittle);
      if (valueAt < start || valueAt + unitBytes > end) {
        return null;
      }
      const raw =
        type === TYPE.short
          ? view.getUint16(valueAt, isLittle)
          : type === TYPE.long
            ? view.getUint32(valueAt, isLittle)
            : view.getUint8(valueAt);
      return raw >= 1 && raw <= 8 ? raw : null;
    }
    return null;
  } catch {
    return null;
  }
}

function textAt(view: DataView, at: number, length: number): string {
  if (at < 0 || at + length > view.byteLength) {
    return '';
  }
  let text = '';
  for (let index = 0; index < length; index++) {
    text += String.fromCharCode(view.getUint8(at + index));
  }
  return text;
}

function uint24At(view: DataView, at: number): number {
  return view.getUint8(at) | (view.getUint8(at + 1) << 8) | (view.getUint8(at + 2) << 16);
}

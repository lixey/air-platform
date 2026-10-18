import type { FileMetadataDto } from '../core/generated/contracts';

/** The leading bytes of a file the facts are read from: EXIF and the size lie well within them. */
export const FACTS_HEAD_BYTES = 256 * 1024;

/** What an image's leading bytes tell without decoding it. */
export interface ImageFacts {
  /** The width in pixels as stored, before its orientation turns it. */
  readonly width: number;
  /** The height in pixels as stored, before its orientation turns it. */
  readonly height: number;
  /** The EXIF orientation, 1 to 8; 1 for an image that carries none. */
  readonly orientation: number;
  /** The tags it carries, the `metadata` an upload posts; null for an image without any, or with EXIF that cannot be read. */
  readonly metadata: FileMetadataDto | null;
}

/**
 * The size and the metadata of a JPEG, PNG or WebP image, read from its first `FACTS_HEAD_BYTES` without decoding it:
 * the size from a JPEG's SOF marker, a PNG's IHDR or a WebP's VP8X, VP8 or VP8L chunk, and the tags from the EXIF of a
 * JPEG's APP1 segment, a PNG's eXIf chunk or a WebP's EXIF chunk, in either byte order. Null for any other file and for
 * one whose size does not lie within those bytes; EXIF that cannot be read leaves the metadata null. It never throws.
 */
export async function imageFactsOf(file: Blob): Promise<ImageFacts | null> {
  try {
    return factsOf(new DataView(await bytesOf(file.slice(0, FACTS_HEAD_BYTES))));
  } catch {
    // A file that cannot be read, or bytes that end inside the size, tell nothing
    return null;
  }
}

/** Tags of IFD0, the Exif IFD and the GPS IFD. */
const TAG = {
  make: 0x010f,
  model: 0x0110,
  orientation: 0x0112,
  software: 0x0131,
  exifIfd: 0x8769,
  gpsIfd: 0x8825,
  exposureTime: 0x829a,
  fNumber: 0x829d,
  iso: 0x8827,
  takenAt: 0x9003,
  focalLength: 0x920a,
  lens: 0xa434,
  latitudeRef: 0x0001,
  latitude: 0x0002,
  longitudeRef: 0x0003,
  longitude: 0x0004,
  altitudeRef: 0x0005,
  altitude: 0x0006,
} as const;

/** The JPEG markers the walk tells apart. */
const MARKER = { prefix: 0xff, tem: 0x01, rst0: 0xd0, soi: 0xd8, eoi: 0xd9, sos: 0xda, app1: 0xe1 } as const;

/** The TIFF types of a value the reader reads. */
const TYPE = { byte: 1, ascii: 2, short: 3, long: 4, rational: 5, signedRational: 10, ifd: 13 } as const;

/** The first two bytes of a JPEG: its SOI marker. */
const JPEG_SIGNATURE = (MARKER.prefix << 8) | MARKER.soi;

/** The first eight bytes of a PNG, as two big-endian words. */
const PNG_SIGNATURE = [0x89504e47, 0x0d0a1a0a] as const;

/** The number a TIFF header carries after its byte order. */
const TIFF_MAGIC = 42;

/** The three bytes that open the frame of a lossy WebP, before its size. */
const VP8_START_CODE = 0x9d012a;

/** The first byte of a lossless WebP's stream. */
const VP8L_SIGNATURE = 0x2f;

/** The fourteen bits a simple WebP stores a side in. */
const WEBP_SIDE_MASK = 0x3fff;

/** The bytes of one value of each TIFF type: BYTE, ASCII, SHORT, LONG, RATIONAL, SBYTE, UNDEFINED, SSHORT, SLONG, SRATIONAL. */
const TYPE_BYTES: Readonly<Record<number, number>> = { 1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8 };

/** The longest text tag kept, in characters. */
const MAX_TEXT = 128;

/** The JPEG markers that start a frame and carry its size: SOF0 to SOF15 but DHT, JPG and DAC. */
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
  let exif: Exif | null = null;
  let at = 2;
  while (at + 4 <= view.byteLength) {
    if (view.getUint8(at) !== MARKER.prefix) {
      return null;
    }
    const marker = view.getUint8(at + 1);
    if (marker === MARKER.prefix) {
      // A fill byte before the marker
      at += 1;
      continue;
    }
    if (marker === MARKER.tem || (marker >= MARKER.rst0 && marker <= MARKER.soi)) {
      at += 2;
      continue;
    }
    if (marker === MARKER.eoi || marker === MARKER.sos) {
      // The image's end, or its scan, before any frame
      return null;
    }
    const length = view.getUint16(at + 2);
    const data = at + 4;
    if (length < 2) {
      return null;
    }
    if (marker === MARKER.app1 && !exif && length >= 8 && textAt(view, data, 6) === 'Exif\0\0') {
      exif = exifOf(view, data + 6, length - 8);
    }
    if (JPEG_FRAMES.has(marker)) {
      return factsWith(view.getUint16(data + 3), view.getUint16(data + 1), exif);
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
  let exif: Exif | null = null;
  let at = 8;
  while (at + 8 <= view.byteLength) {
    const length = view.getUint32(at);
    const type = textAt(view, at + 4, 4);
    if (type === 'eXIf') {
      exif = exifOf(view, at + 8, length);
      break;
    }
    if (type === 'IEND') {
      break;
    }
    at += 12 + length;
  }
  return factsWith(width, height, exif);
}

function webpFactsOf(view: DataView): ImageFacts | null {
  let width = 0;
  let height = 0;
  let exif: Exif | null = null;
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
      // Some encoders keep the JPEG's `Exif\0\0` before the TIFF header
      const skip = length >= 6 && textAt(view, data, 6) === 'Exif\0\0' ? 6 : 0;
      exif = exifOf(view, data + skip, length - skip);
    }
    at = data + length + (length % 2);
  }
  return factsWith(width, height, exif);
}

function factsWith(width: number, height: number, exif: Exif | null): ImageFacts | null {
  if (width <= 0 || height <= 0) {
    return null;
  }
  return { width, height, orientation: exif?.orientation ?? 1, metadata: exif?.metadata ?? null };
}

interface Exif {
  readonly orientation: number;
  readonly metadata: FileMetadataDto | null;
}

/** One entry of an IFD: its type, its count of values and the offset of the first, within the view. */
interface Entry {
  readonly type: number;
  readonly count: number;
  readonly at: number;
}

/** The tags of the TIFF structure at `start`; null when it is malformed or carries none of them. */
function exifOf(view: DataView, start: number, length: number): Exif | null {
  try {
    const tiff = Tiff.at(view, start, Math.min(length, view.byteLength - start));
    if (!tiff) {
      return null;
    }
    const ifd0 = tiff.entriesOf(tiff.firstIfd());
    const exifIfd = tiff.entriesOf(tiff.pointerOf(ifd0.get(TAG.exifIfd)));
    const gpsIfd = tiff.entriesOf(tiff.pointerOf(ifd0.get(TAG.gpsIfd)));
    const orientation = tiff.integerOf(ifd0.get(TAG.orientation));
    const metadata: FileMetadataDto = {
      make: tiff.textOf(ifd0.get(TAG.make)),
      model: tiff.textOf(ifd0.get(TAG.model)),
      lens: tiff.textOf(exifIfd.get(TAG.lens)),
      takenAt: momentOf(tiff.textOf(exifIfd.get(TAG.takenAt))),
      exposureTime: exposureOf(tiff, exifIfd.get(TAG.exposureTime)),
      fNumber: tenthsOf(tiff.rationalOf(exifIfd.get(TAG.fNumber))),
      iso: tiff.integerOf(exifIfd.get(TAG.iso)),
      focalLength: tenthsOf(tiff.rationalOf(exifIfd.get(TAG.focalLength))),
      orientation: orientation !== undefined && orientation >= 1 && orientation <= 8 ? orientation : undefined,
      software: tiff.textOf(ifd0.get(TAG.software)),
      latitude: degreesOf(tiff, gpsIfd.get(TAG.latitude), tiff.textOf(gpsIfd.get(TAG.latitudeRef)) === 'S'),
      longitude: degreesOf(tiff, gpsIfd.get(TAG.longitude), tiff.textOf(gpsIfd.get(TAG.longitudeRef)) === 'W'),
      altitude: altitudeOf(tiff, gpsIfd),
    };
    const present = Object.fromEntries(Object.entries(metadata).filter(([, value]) => value !== undefined)) as FileMetadataDto;
    return { orientation: present.orientation ?? 1, metadata: Object.keys(present).length > 0 ? present : null };
  } catch {
    // An offset or a count that leads outside the bytes read: the tags are malformed, the size still counts
    return null;
  }
}

/** A TIFF structure within the view: its byte order and its bounds; every offset it follows is checked against them. */
class Tiff {
  private constructor(
    private readonly view: DataView,
    private readonly start: number,
    private readonly end: number,
    private readonly isLittle: boolean,
  ) {}

  static at(view: DataView, start: number, length: number): Tiff | null {
    if (length < 8) {
      return null;
    }
    const order = textAt(view, start, 2);
    if (order !== 'II' && order !== 'MM') {
      return null;
    }
    const tiff = new Tiff(view, start, start + length, order === 'II');
    return tiff.uint16At(start + 2) === TIFF_MAGIC ? tiff : null;
  }

  firstIfd(): number {
    return this.uint32At(this.start + 4);
  }

  /** The entries of the IFD at the offset given, by tag; none for an offset outside the structure. */
  entriesOf(offset: number | undefined): ReadonlyMap<number, Entry> {
    const entries = new Map<number, Entry>();
    if (offset === undefined || offset < 8 || this.start + offset + 2 > this.end) {
      return entries;
    }
    const at = this.start + offset;
    const count = this.uint16At(at);
    for (let index = 0; index < count && at + 2 + (index + 1) * 12 <= this.end; index++) {
      const entry = at + 2 + index * 12;
      const type = this.uint16At(entry + 2);
      const values = this.uint32At(entry + 4);
      const bytes = (TYPE_BYTES[type] ?? 0) * values;
      const value = bytes <= 4 ? entry + 8 : this.start + this.uint32At(entry + 8);
      if (bytes > 0 && value + bytes <= this.end) {
        entries.set(this.uint16At(entry), { type, count: values, at: value });
      }
    }
    return entries;
  }

  pointerOf(entry: Entry | undefined): number | undefined {
    return entry && (entry.type === TYPE.long || entry.type === TYPE.ifd) ? this.uint32At(entry.at) : undefined;
  }

  integerOf(entry: Entry | undefined): number | undefined {
    if (entry?.type === TYPE.short) {
      return this.uint16At(entry.at);
    }
    if (entry?.type === TYPE.long) {
      return this.uint32At(entry.at);
    }
    return entry?.type === TYPE.byte ? this.view.getUint8(entry.at) : undefined;
  }

  /** The rational value at `index` as numerator and denominator; undefined for another type or a zero denominator. */
  fractionOf(entry: Entry | undefined, index = 0): [number, number] | undefined {
    if (!entry || (entry.type !== TYPE.rational && entry.type !== TYPE.signedRational) || index >= entry.count) {
      return undefined;
    }
    const at = entry.at + index * 8;
    const isSigned = entry.type === TYPE.signedRational;
    const numerator = isSigned ? this.view.getInt32(at, this.isLittle) : this.uint32At(at);
    const denominator = isSigned ? this.view.getInt32(at + 4, this.isLittle) : this.uint32At(at + 4);
    return denominator === 0 ? undefined : [numerator, denominator];
  }

  rationalOf(entry: Entry | undefined, index = 0): number | undefined {
    const fraction = this.fractionOf(entry, index);
    return fraction && fraction[0] / fraction[1];
  }

  /** An ASCII tag up to its first NUL, trimmed and cut to `MAX_TEXT`; undefined when empty. */
  textOf(entry: Entry | undefined): string | undefined {
    if (entry?.type !== TYPE.ascii) {
      return undefined;
    }
    let text = '';
    for (let index = 0; index < entry.count && text.length < MAX_TEXT; index++) {
      const code = this.view.getUint8(entry.at + index);
      if (code === 0) {
        break;
      }
      text += code >= 0x20 && code !== 0x7f ? String.fromCharCode(code) : ' ';
    }
    text = text.trim();
    return text === '' ? undefined : text;
  }

  private uint16At(at: number): number {
    return this.view.getUint16(at, this.isLittle);
  }

  private uint32At(at: number): number {
    return this.view.getUint32(at, this.isLittle);
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

/** EXIF's `2024:05:01 12:30:45` as `2024-05-01T12:30:45`; undefined for any other text. */
function momentOf(text: string | undefined): string | undefined {
  const parts = /^(\d{4}):(\d{2}):(\d{2}) (\d{2}):(\d{2}):(\d{2})$/.exec(text ?? '');
  if (!parts || parts[1] === '0000') {
    return undefined;
  }
  return `${parts[1]}-${parts[2]}-${parts[3]}T${parts[4]}:${parts[5]}:${parts[6]}`;
}

/** An exposure under a second as `1/120`, a longer one in seconds to a tenth. */
function exposureOf(tiff: Tiff, entry: Entry | undefined): string | undefined {
  const fraction = tiff.fractionOf(entry);
  if (!fraction || fraction[0] <= 0 || fraction[1] <= 0) {
    return undefined;
  }
  const seconds = fraction[0] / fraction[1];
  return seconds < 1 ? `1/${Math.round(fraction[1] / fraction[0])}` : String(tenthsOf(seconds));
}

function tenthsOf(value: number | undefined): number | undefined {
  return value !== undefined && Number.isFinite(value) && value > 0 ? Math.round(value * 10) / 10 : undefined;
}

/** GPS degrees, minutes and seconds as signed degrees to seven places; undefined out of range. */
function degreesOf(tiff: Tiff, entry: Entry | undefined, isNegative: boolean): number | undefined {
  const degrees = tiff.rationalOf(entry, 0);
  const minutes = tiff.rationalOf(entry, 1) ?? 0;
  const seconds = tiff.rationalOf(entry, 2) ?? 0;
  if (degrees === undefined) {
    return undefined;
  }
  const value = Math.round((degrees + minutes / 60 + seconds / 3600) * 1e7) / 1e7;
  if (!Number.isFinite(value) || value > 180) {
    return undefined;
  }
  return isNegative ? -value : value;
}

/** The GPS altitude in metres to a tenth, negative when its reference is below sea level. */
function altitudeOf(tiff: Tiff, gps: ReadonlyMap<number, Entry>): number | undefined {
  const altitude = tiff.rationalOf(gps.get(TAG.altitude));
  if (altitude === undefined || !Number.isFinite(altitude)) {
    return undefined;
  }
  const value = Math.round(altitude * 10) / 10;
  return tiff.integerOf(gps.get(TAG.altitudeRef)) === 1 ? -value : value;
}

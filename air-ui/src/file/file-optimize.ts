import type { FileImages } from '../core/generated/contracts';
import { contentTypeOf, ENCODED_IMAGES, isEncodedImage } from './content-type';
import { splitName } from './file-view';
import { drawEncoded, type ImageTurn, resizedOf } from './image-canvas';
import type { ImageFacts } from './image-facts';

/** The image rules of an upload's offer, with the types it accepts. */
export interface ImageRules {
  /**
   * How the upload treats a picked image: as picked, a PNG made a lossless WebP, or every image drawn upright within
   * the box and encoded.
   */
  readonly images: FileImages;
  /** The longest side an optimised image keeps, in pixels. */
  readonly imageMaxSide: number;
  /** The quality a lossy encoding takes, 0.5 to 1. */
  readonly imageQuality: number;
  readonly accept: readonly string[];
}

/** An encoding tried: its type and its quality; a WebP at 1 is lossless. */
interface Target {
  readonly type: string;
  readonly quality: number;
}

/** A decoded image, upright where the browser honours its orientation, and how to let it go. */
interface Decoded {
  readonly image: CanvasImageSource;
  /** The image as a bitmap, which `createImageBitmap` can resize; null for an `<img>`. */
  readonly bitmap: ImageBitmap | null;
  readonly width: number;
  readonly height: number;
  release(): void;
}

/**
 * The transform that draws an image stored at an orientation that swaps its sides upright on a `width` by `height`
 * canvas, by its orientation; an orientation absent here is drawn as it decoded.
 */
const TURNS: Readonly<Partial<Record<number, (width: number, height: number) => ImageTurn>>> = {
  5: () => [0, 1, 1, 0, 0, 0],
  6: width => [0, 1, -1, 0, width, 0],
  7: (width, height) => [0, -1, -1, 0, width, height],
  8: (_, height) => [0, -1, 1, 0, 0, height],
};

/**
 * The file an upload posts for a picked one by its rules (`ui/grid`, "The upload dialog"). Under `keep`, and for a GIF,
 * an SVG or a file that is no image, the picked file. Under `lossless`, a PNG encoded as a lossless WebP where `accept`
 * holds `image/webp`, the browser encodes one and it is smaller, else the picked file; nothing is scaled. Under
 * `optimize`, the image drawn upright by its orientation, scaled down, never up, to fit `imageMaxSide`, and encoded — a
 * PNG as a lossless WebP, else a PNG, a JPEG or a WebP as a WebP at `imageQuality`, else a JPEG — each only where `accept`
 * holds its type; the encoded image when it is smaller than the picked one or the picked one exceeds `imageMaxSide`,
 * else the picked one. A browser that answers another type than the one asked, as one without a WebP encoder answers a
 * PNG, has not encoded it. An encoded image keeps the name's base and takes its type's extension. An image the browser
 * fails to decode or encode stays as picked; it never throws.
 */
export async function optimizeImage(file: File, rules: ImageRules, facts: ImageFacts | null): Promise<File> {
  const type = contentTypeOf(file.type);
  const targets = targetsOf(type, rules).filter(target => rules.accept.some(accepted => contentTypeOf(accepted) === target.type));
  if (targets.length === 0) {
    return file;
  }
  let decoded: Decoded | null = null;
  try {
    decoded = await decodedOf(file);
    if (!decoded) {
      return file;
    }
    // A browser that drew the image as stored, not upright, leaves the turn to the canvas
    const orientation = isTurnedByCanvas(decoded, facts) ? (facts?.orientation ?? 1) : 1;
    const isSwapped = orientation >= 5;
    const width = isSwapped ? decoded.height : decoded.width;
    const height = isSwapped ? decoded.width : decoded.height;
    const longer = Math.max(width, height);
    const isOptimized = rules.images === 'optimize';
    const scale = isOptimized ? Math.min(1, rules.imageMaxSide / longer) : 1;
    const encoded = await encodedOf(
      decoded,
      orientation,
      Math.max(1, Math.round(width * scale)),
      Math.max(1, Math.round(height * scale)),
      targets,
    );
    if (!encoded) {
      return file;
    }
    const exceedsBox = isOptimized && longer > rules.imageMaxSide;
    return encoded.size < file.size || exceedsBox ? renamedAs(file, encoded) : file;
  } catch {
    // An image the browser cannot decode, draw or encode posts as picked
    return file;
  } finally {
    decoded?.release();
  }
}

/** The encodings tried in turn for an image of the type given; none for an image posted as picked. */
function targetsOf(type: string, rules: ImageRules): Target[] {
  if (!isEncodedImage(type, rules.images)) {
    return [];
  }
  if (type === 'image/png') {
    const lossless = { type: 'image/webp', quality: 1 };
    return rules.images === 'lossless' ? [lossless] : [lossless, { type: 'image/png', quality: 1 }];
  }
  return [
    { type: 'image/webp', quality: rules.imageQuality },
    { type: 'image/jpeg', quality: rules.imageQuality },
  ];
}

/** The image decoded upright by `createImageBitmap`, or by an `<img>` where that fails; null where neither is offered. */
async function decodedOf(file: Blob): Promise<Decoded | null> {
  if (typeof createImageBitmap === 'function') {
    try {
      const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
      return { image: bitmap, bitmap, width: bitmap.width, height: bitmap.height, release: () => bitmap.close() };
    } catch {
      // Some browsers decode in an `<img>` what they refuse here
    }
  }
  if (typeof document === 'undefined' || typeof URL.createObjectURL !== 'function') {
    return null;
  }
  const url = URL.createObjectURL(file);
  try {
    const image = document.createElement('img');
    image.src = url;
    await image.decode();
    return { image, bitmap: null, width: image.naturalWidth, height: image.naturalHeight, release: () => URL.revokeObjectURL(url) };
  } catch (error) {
    URL.revokeObjectURL(url);
    throw error;
  }
}

/**
 * Whether the browser decoded a turned image as stored: its orientation swaps the sides, the image is no square and the
 * decoded sides are the stored ones. A turn that swaps nothing cannot be told apart and is left to the browser.
 */
function isTurnedByCanvas(decoded: Decoded, facts: ImageFacts | null): boolean {
  return (
    facts !== null &&
    facts.orientation >= 5 &&
    facts.width !== facts.height &&
    decoded.width === facts.width &&
    decoded.height === facts.height
  );
}

/**
 * The image drawn upright at `width` by `height` and encoded as the first target the browser encodes; null when it
 * encodes none. `orientation` is the turn the canvas still makes, 1 for none.
 */
async function encodedOf(decoded: Decoded, orientation: number, width: number, height: number, targets: Target[]): Promise<Blob | null> {
  const turn = TURNS[orientation]?.(width, height);
  const drawnWidth = turn ? height : width;
  const drawnHeight = turn ? width : height;
  const resized = decoded.bitmap ? await resizedOf(decoded.bitmap, drawnWidth, drawnHeight) : null;
  try {
    for (const target of targets) {
      const blob = await drawEncoded(resized ?? decoded.image, width, height, target.type, target.quality, turn);
      if (blob) {
        return blob;
      }
    }
    return null;
  } finally {
    if (resized && resized !== decoded.bitmap) {
      resized.close();
    }
  }
}

/**
 * The encoded image as a file named by the picked one's base (`splitName`) and its own type's extension. A name ending
 * in a dot, which `splitName` leaves whole, drops that dot first: `photo.` becomes `photo.webp`, not `photo..webp`.
 */
function renamedAs(file: File, blob: Blob): File {
  const type = contentTypeOf(blob.type);
  const { base, extension } = splitName(file.name);
  const stem = extension === '' && base.length > 1 && base.endsWith('.') ? base.slice(0, -1) : base;
  return new File([blob], `${stem}${ENCODED_IMAGES[type] ?? ''}`, { type, lastModified: file.lastModified });
}

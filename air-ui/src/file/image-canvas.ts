import { contentTypeOf } from './content-type';

/** The canvas an image is drawn on: an offscreen one where the browser has it, an element otherwise. */
export type ImageCanvas = OffscreenCanvas | HTMLCanvasElement;

/** A canvas transform, the six values `setTransform` takes, that turns an image upright; every such turn swaps its sides. */
export type ImageTurn = readonly [number, number, number, number, number, number];

/** The image at the size given by `createImageBitmap`; the image itself where the browser ignores the size asked. */
export async function resizedOf(image: ImageBitmap, width: number, height: number): Promise<ImageBitmap> {
  if (width === image.width && height === image.height) {
    return image;
  }
  try {
    const resized = await createImageBitmap(image, { resizeWidth: width, resizeHeight: height, resizeQuality: 'high' });
    if (resized.width === width && resized.height === height) {
      return resized;
    }
    resized.close();
  } catch {
    // The canvas below resizes where the browser offers no resizing here
  }
  return image;
}

/**
 * The source drawn on a `width` by `height` canvas and encoded as `type` at `quality`, on a white ground for a JPEG,
 * which has no transparency. Under `turn` the source is drawn `height` by `width`, as stored, and turned upright onto
 * the canvas. Null when the browser gives no canvas to draw on or cannot encode the type: it answered another one, or
 * none.
 */
export async function drawEncoded(
  source: CanvasImageSource,
  width: number,
  height: number,
  type: string,
  quality: number,
  turn?: ImageTurn,
): Promise<Blob | null> {
  const canvas = canvasOf(width, height);
  const context = canvas.getContext('2d') as OffscreenCanvasRenderingContext2D | CanvasRenderingContext2D | null;
  if (!context) {
    return null;
  }
  if (type === 'image/jpeg') {
    // JPEG has no transparency: a transparent pixel reads white, not black
    context.fillStyle = '#fff';
    context.fillRect(0, 0, width, height);
  }
  context.imageSmoothingQuality = 'high';
  if (turn) {
    context.setTransform(...turn);
  }
  context.drawImage(source, 0, 0, turn ? height : width, turn ? width : height);
  const blob = await blobOf(canvas, type, quality);
  return blob && contentTypeOf(blob.type) === type ? blob : null;
}

/** A canvas of the size given: an offscreen one where the browser has it, an element otherwise. */
export function canvasOf(width: number, height: number): ImageCanvas {
  if (typeof OffscreenCanvas === 'function') {
    return new OffscreenCanvas(width, height);
  }
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  return canvas;
}

/** The canvas encoded as the type asked, at the quality given; a browser that cannot encode it answers another type. */
export function blobOf(canvas: ImageCanvas, type: string, quality: number): Promise<Blob | null> {
  if ('convertToBlob' in canvas) {
    return canvas.convertToBlob({ type, quality });
  }
  return new Promise(resolve => canvas.toBlob(resolve, type, quality));
}

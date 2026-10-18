import { contentTypeOf } from './content-type';
import { isInlineImage, isInlineVideo } from './file-view';
import { drawEncoded, resizedOf } from './image-canvas';

/** The longer side of a thumbnail, in pixels; a smaller image keeps its own size. */
const THUMBNAIL_SIDE = 320;

/** The encodings a thumbnail is taken in, in order: WebP, and JPEG where the browser cannot encode WebP. */
const THUMBNAIL_TYPES = ['image/webp', 'image/jpeg'] as const;

/** The qualities tried in turn until the thumbnail fits. */
const QUALITIES = [0.8, 0.65, 0.5, 0.35] as const;

/** The moment of a video its thumbnail is drawn at, in seconds: its start, a seek the browser answers with that frame. */
const FIRST_FRAME_AT = 0.001;

/** How long a video may take to give its first frame before it goes without a thumbnail, in milliseconds. */
export const FIRST_FRAME_TIMEOUT_MS = 5000;

/**
 * The thumbnail of a raster image a link serves inline, or of an MP4 video's first frame, drawn by the browser: at most
 * 320 pixels on its longer side, resized by `createImageBitmap` or, where the browser does not resize there, by the
 * canvas, and encoded as WebP, or as JPEG where the browser cannot encode WebP, at a lower quality each time until it
 * fits `maxBytes`, the upload's `thumbnailMaxBytes`, the largest thumbnail the server takes. Null for any other file, in
 * a browser that draws no bitmap, for an image or a video it cannot decode, for a video that gives no frame within
 * `FIRST_FRAME_TIMEOUT_MS`, and when no quality fits: the file then goes without one, and its tile draws the image
 * itself, or a video's badge. The server compares the thumbnail's type and leading bytes; it never decodes it.
 */
export async function thumbnailOf(file: Blob, maxBytes: number): Promise<Blob | null> {
  const type = contentTypeOf(file.type);
  const isVideo = isInlineVideo(type);
  if (!(isVideo || isInlineImage(type)) || typeof createImageBitmap !== 'function') {
    return null;
  }
  let image: ImageBitmap | null = null;
  try {
    image = isVideo ? await firstFrameOf(file) : await createImageBitmap(file);
    if (!image) {
      return null;
    }
    const scale = Math.min(1, THUMBNAIL_SIDE / Math.max(image.width, image.height));
    const width = Math.max(1, Math.round(image.width * scale));
    const height = Math.max(1, Math.round(image.height * scale));
    const source = await resizedOf(image, width, height);
    try {
      return await encodedOf(source, width, height, maxBytes);
    } finally {
      if (source !== image) {
        source.close();
      }
    }
  } catch {
    // A file the browser cannot decode or draw goes without a thumbnail: the server takes it all the same
    return null;
  } finally {
    image?.close();
  }
}

/**
 * A video's first frame as a bitmap: a detached, muted `<video>` loads the file's metadata alone, is seeked to its
 * start and drawn once the seek lands. Null for a video without a picture; rejected for one the browser cannot decode
 * and for one that gives no frame in time. The element lets its source go and the file's object URL is revoked either
 * way.
 */
async function firstFrameOf(file: Blob): Promise<ImageBitmap | null> {
  if (typeof document === 'undefined' || typeof URL.createObjectURL !== 'function') {
    return null;
  }
  const video = document.createElement('video');
  video.muted = true;
  video.playsInline = true;
  video.preload = 'metadata';
  const url = URL.createObjectURL(file);
  let timer: ReturnType<typeof setTimeout> | undefined;
  const expired = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error('The video gave no frame in time.')), FIRST_FRAME_TIMEOUT_MS);
  });
  try {
    const loaded = eventOf(video, 'loadedmetadata');
    video.src = url;
    await Promise.race([loaded, expired]);
    const seeked = eventOf(video, 'seeked');
    video.currentTime = FIRST_FRAME_AT;
    await Promise.race([seeked, expired]);
    return video.videoWidth > 0 && video.videoHeight > 0 ? await createImageBitmap(video) : null;
  } finally {
    clearTimeout(timer);
    video.removeAttribute('src');
    video.load();
    URL.revokeObjectURL(url);
  }
}

/** Resolves on the element's next `name` event, rejects on its next `error`. */
function eventOf(video: HTMLVideoElement, name: 'loadedmetadata' | 'seeked'): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const settle = (event: Event): void => {
      video.removeEventListener(name, settle);
      video.removeEventListener('error', settle);
      if (event.type === 'error') {
        reject(new Error('The browser cannot decode the video.'));
      } else {
        resolve();
      }
    };
    video.addEventListener(name, settle);
    video.addEventListener('error', settle);
  });
}

/** The first encoding and quality that fits `maxBytes`; null when none does. */
async function encodedOf(source: ImageBitmap, width: number, height: number, maxBytes: number): Promise<Blob | null> {
  for (const type of THUMBNAIL_TYPES) {
    for (const quality of QUALITIES) {
      const blob = await drawEncoded(source, width, height, type, quality);
      if (!blob) {
        // The browser cannot encode this type
        break;
      }
      if (blob.size <= maxBytes) {
        return blob;
      }
    }
  }
  return null;
}

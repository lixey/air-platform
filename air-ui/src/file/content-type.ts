import type { FileImages } from '../core/generated/contracts';

/** A declared content type as the server compares it: without its parameters, trimmed and lowercase. */
export function contentTypeOf(declared: string | null | undefined): string {
  return (declared ?? '').split(';', 1)[0].trim().toLowerCase();
}

/**
 * The images the client encodes, by type, with the extension an encoded one takes: the images whose size and metadata an
 * upload reads before posting and its image rules may treat. A GIF, an SVG and every other file post as picked.
 */
export const ENCODED_IMAGES: Readonly<Record<string, string>> = { 'image/jpeg': '.jpg', 'image/png': '.png', 'image/webp': '.webp' };

/**
 * Whether an upload under the rule `images` may encode a file of the type given: a JPEG, a PNG or a WebP under
 * `optimize`, the default, a PNG alone under `lossless`, none under `keep`.
 */
export function isEncodedImage(type: string, images: FileImages = 'optimize'): boolean {
  const encoded = contentTypeOf(type);
  return images !== 'keep' && Object.hasOwn(ENCODED_IMAGES, encoded) && (images === 'optimize' || encoded === 'image/png');
}

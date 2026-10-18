import { isRecord } from '../core/json';
import type { GridRow, GridRowKey } from '../grid/grid-row';

/** The raster images a file link serves inline, the only ones drawn from their own content; SVG is an attachment. */
export const INLINE_IMAGE_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/gif', 'image/webp'];

/** The videos a file link serves inline, the only ones the viewer plays: MP4 alone. */
export const INLINE_VIDEO_TYPES: readonly string[] = ['video/mp4'];

/**
 * One file as the files element, its tiles and the file viewer draw it: a row of a file grid, or a file on its way
 * there, which has no key and no link yet.
 */
export interface FileView {
  readonly key: GridRowKey | null;
  readonly name: string;
  /** The size in bytes; null where the row does not say. */
  readonly size: number | null;
  readonly contentType: string;
  /** The signed link to the content; null where there is none to follow. */
  readonly url: string | null;
  /** The signed link to the thumbnail the browser made at upload; null where the file has none. */
  readonly thumbUrl: string | null;
}

/** Whether a content type is a raster image a link serves inline. */
export function isInlineImage(contentType: string | null | undefined): boolean {
  return typeof contentType === 'string' && INLINE_IMAGE_TYPES.includes(contentType);
}

/** Whether a content type is a video a link serves inline, played by the browser's own player. */
export function isInlineVideo(contentType: string | null | undefined): boolean {
  return typeof contentType === 'string' && INLINE_VIDEO_TYPES.includes(contentType);
}

/**
 * The link that saves a file rather than shows it: its `url` with the query `download`, which makes the server answer
 * the content as an attachment under its name (`data/record-files`); a cross-origin link's `download` attribute is
 * ignored by the browser, the query is not.
 */
export function downloadLinkOf(url: string): string {
  return `${url}${url.includes('?') ? '&' : '?'}download`;
}

/** A file grid's row as a file: its `id`, `name`, `size`, `contentType`, `url` and `thumbUrl`. */
export function fileViewOf(row: GridRow, key: GridRowKey | null): FileView {
  return {
    key,
    name: textOf(row['name']),
    size: typeof row['size'] === 'number' ? row['size'] : null,
    contentType: textOf(row['contentType']),
    url: linkOf(row['url']),
    thumbUrl: linkOf(row['thumbUrl']),
  };
}

/** The rows of a file grid's page of data; none where the answer holds no page. */
export function rowsOfPage(data: unknown): readonly GridRow[] {
  const rows = isRecord(data) ? data['rows'] : null;
  return Array.isArray(rows) ? rows.filter(isRecord) : [];
}

/** The files of a file grid's page of data: each row that carries a key, as a file. */
export function filesOfPage(data: unknown): readonly FileView[] {
  return rowsOfPage(data).flatMap(row => {
    const key = typeof row['id'] === 'number' ? row['id'] : null;
    return key === null ? [] : [fileViewOf(row, key)];
  });
}

/** A file the user gave, before it is posted: its name, size and type, and no link. */
export function fileViewOfUpload(file: File): FileView {
  return { key: null, name: file.name, size: file.size, contentType: file.type, url: null, thumbUrl: null };
}

/**
 * Where a file's picture comes from: its thumbnail, a video's first frame among them, else its own content for a raster
 * image a link serves inline; null for any other file, which is drawn as the badge of its extension.
 */
export function pictureOf(file: FileView): string | null {
  return file.thumbUrl ?? (isInlineImage(file.contentType) ? file.url : null);
}

/**
 * A file name split at the dot of its extension: the base and the extension with its dot (`archive.tar` and `.gz`). The
 * one rule of every file view: the extension is what follows the last dot, where that dot is neither the first nor the
 * last character; a name without one (`README`, `.env`, `draft.`) is all base, its extension empty.
 */
export function splitName(name: string): { readonly base: string; readonly extension: string } {
  const dot = name.lastIndexOf('.');
  return dot > 0 && dot < name.length - 1 ? { base: name.slice(0, dot), extension: name.slice(dot) } : { base: name, extension: '' };
}

/** The badge of a file without a picture: its name's extension in capitals (`PDF`), or nothing for a name without one. */
export function badgeOf(name: string): string {
  return splitName(name).extension.slice(1).toUpperCase();
}

/** A tile's name: the file's name without the extension its badge shows (`catalog-retro`), the whole name for one without. */
export function stemOf(name: string): string {
  return splitName(name).base;
}

function textOf(value: unknown): string {
  if (value === null || value === undefined) {
    return '';
  }
  return typeof value === 'string' ? value : String(value);
}

function linkOf(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

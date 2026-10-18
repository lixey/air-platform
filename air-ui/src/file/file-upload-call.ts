import type { FileMetadataDto, FileUploadLimitsDto, GridRequest } from '../core/generated/contracts';
import { wallOf } from '../core/time-zone';
import { contentTypeOf, ENCODED_IMAGES, isEncodedImage } from './content-type';
import { splitName } from './file-view';

/**
 * The types and extensions an upload accepts, as its `FileUploadLimitsDto` carries them: all that its picker and its
 * names of the accepted files need.
 */
export type UploadAccept = Readonly<Pick<FileUploadLimitsDto, 'accept' | 'extensions'>>;

/** Why the client refuses a file before posting it: larger than the upload takes, or of a type or a name it does not accept. */
export type FileUploadRefusal = 'size' | 'type';

/** The raster images a paste or a camera gives, by type, with the extension a pasted one is named with. */
const RASTER_EXTENSIONS: Readonly<Record<string, string>> = { ...ENCODED_IMAGES, 'image/gif': '.gif' };

/** The longer spellings of an extension the limits name once, by the shorter one they list first. */
const EXTENSION_ALIASES: Readonly<Record<string, string>> = { '.jpeg': '.jpg', '.htm': '.html' };

/** A file name's extension, lowercase and with its dot (`.pdf`); empty for a name without one (`splitName`). */
export function extensionOf(name: string): string {
  return splitName(name).extension.toLowerCase();
}

/**
 * The largest picked file the upload takes: `sourceMaxBytes` for an image its rule may make smaller (every JPEG, PNG and
 * WebP under `optimize`, a PNG under `lossless`), the image it then posts being held to `maxBytes`; `maxBytes` for any
 * other file, which posts as picked.
 */
export function takenMaxBytesOf(type: string, limits: Pick<FileUploadLimitsDto, 'maxBytes' | 'sourceMaxBytes' | 'images'>): number {
  return isEncodedImage(type, limits.images) ? Math.max(limits.maxBytes, limits.sourceMaxBytes) : limits.maxBytes;
}

/**
 * Why the file may not go, checked as the server checks it: larger than the upload takes (`takenMaxBytesOf`), of a type
 * outside `accept`, a file whose type the browser could not tell among them, or with a name ending in none of
 * `extensions`; null when it may be taken.
 */
export function uploadRefusalOf(
  file: Pick<File, 'name' | 'size' | 'type'>,
  limits: Readonly<Pick<FileUploadLimitsDto, 'accept' | 'extensions' | 'maxBytes' | 'sourceMaxBytes' | 'images'>>,
): FileUploadRefusal | null {
  if (file.size > takenMaxBytesOf(file.type, limits)) {
    return 'size';
  }
  const type = contentTypeOf(file.type);
  const isAccepted = type !== '' && limits.accept.some(accepted => contentTypeOf(accepted) === type);
  const hasExtension = limits.extensions.length === 0 || limits.extensions.includes(extensionOf(file.name));
  return isAccepted && hasExtension ? null : 'type';
}

/** What the browser's picker offers: the accepted types and their extensions, so a file of neither is not offered. */
export function pickerAcceptOf(limits: UploadAccept): string {
  return [...limits.accept, ...limits.extensions].join(',');
}

/** The raster images among the accepted types, the ones a camera may give; empty when the upload takes none. */
export function cameraAcceptOf(limits: UploadAccept): string {
  return limits.accept.filter(type => contentTypeOf(type) in RASTER_EXTENSIONS).join(',');
}

/**
 * The videos among the accepted types, the ones a camera may record (`video/mp4`, `video/quicktime`, `video/webm`,
 * `video/3gpp`, whichever the upload takes); empty when the upload takes none.
 */
export function videoAcceptOf(limits: UploadAccept): string {
  return limits.accept.filter(type => contentTypeOf(type).startsWith('video/')).join(',');
}

/**
 * The accepted files as a person names them: each type once, by its extension in capitals (`PNG, JPG, PDF`); the
 * content types where the limits carry no extension.
 */
export function acceptedNamesOf(limits: UploadAccept): string {
  if (limits.extensions.length === 0) {
    return limits.accept.join(', ');
  }
  const listed = limits.extensions.filter(extension => {
    const alias = EXTENSION_ALIASES[extension];
    return !alias || !limits.extensions.includes(alias);
  });
  return [...new Set(listed)].map(extension => extension.slice(1).toUpperCase()).join(', ');
}

/**
 * The files of a paste as the upload takes them: a file of an accepted type named `pasted-yyyyMMdd-HHmmss.<ext>` by the
 * moment of the paste on the clock of `zone`, the user's, its extension its name's when the limits take that one, else
 * its type's; any other file as it came, for the upload to refuse with its reason.
 */
export function pastedFilesOf(files: readonly File[], limits: UploadAccept, at: Date, zone: string): File[] {
  return files.map(file => {
    const type = contentTypeOf(file.type);
    if (!limits.accept.some(accepted => contentTypeOf(accepted) === type)) {
      return file;
    }
    const own = extensionOf(file.name);
    const extension = own !== '' && limits.extensions.includes(own) ? own : (RASTER_EXTENSIONS[type] ?? own);
    return new File([file], `pasted-${stampOf(at, zone)}${extension}`, { type: file.type, lastModified: at.getTime() });
  });
}

/** What an upload posts beside its file: the image's metadata, and the file of the record the new one follows. */
export interface UploadExtras {
  /** The tags read from the picked image, posted as the JSON of a `FileMetadataDto`; none for a file without any. */
  readonly metadata?: FileMetadataDto | null;
  /** The file the new one is placed after; none places it first among the record's files. */
  readonly afterId?: number | null;
}

/**
 * The form of an upload to the grid's `Upload`: the grid's parent, `parentService` and `parentId`, when the grid has one,
 * the file under `file`, the thumbnail the browser made of it, when it made one, under `thumbnail`, the image's
 * `metadata` as JSON and `afterId`, each when there is one.
 */
export function uploadFormOf(
  params: Pick<GridRequest, 'parentService' | 'parentId'>,
  file: File,
  thumbnail: Blob | null = null,
  extras: UploadExtras = {},
): FormData {
  const form = new FormData();
  if (params.parentService && params.parentId != null) {
    form.append('parentService', params.parentService);
    form.append('parentId', String(params.parentId));
  }
  form.append('file', file, file.name);
  if (thumbnail) {
    form.append('thumbnail', thumbnail, thumbnail.type === 'image/jpeg' ? 'thumbnail.jpg' : 'thumbnail.webp');
  }
  if (extras.metadata) {
    form.append('metadata', JSON.stringify(extras.metadata));
  }
  if (extras.afterId != null) {
    form.append('afterId', String(extras.afterId));
  }
  return form;
}

/** A moment as `yyyyMMdd-HHmmss` on the clock of `zone`. */
function stampOf(at: Date, zone: string): string {
  const two = (value: number): string => String(value).padStart(2, '0');
  const wall = wallOf(at, zone);
  return `${wall.year}${two(wall.month)}${two(wall.day)}-${two(wall.hour)}${two(wall.minute)}${two(wall.second)}`;
}

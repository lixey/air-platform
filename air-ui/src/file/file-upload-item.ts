import type { FileUploadLimitsDto } from '../core/generated/contracts';
import type { TextsService, UiTextKey } from '../core/texts.service';
import { newUuid } from '../core/uuid';
import type { GridRowKey } from '../grid/grid-row';
import { isEncodedImage } from './content-type';
import { optimizeImage } from './file-optimize';
import { formatMegabytes } from './file-size';
import { thumbnailOf } from './file-thumbnail';
import { acceptedNamesOf, takenMaxBytesOf, uploadRefusalOf } from './file-upload-call';
import { imageFactsOf } from './image-facts';

/**
 * Where a file of the queue stands: refused before posting, waiting for its turn, being prepared at its turn (its facts
 * read and its image treated), posting, answered, or failed.
 */
export type FileUploadStatus = 'refused' | 'waiting' | 'preparing' | 'uploading' | 'uploaded' | 'failed';

/** The statuses of a file on its way: waiting, being prepared or posting; what a leave asks about and Cancel stops. */
export const UNDER_WAY: readonly FileUploadStatus[] = ['waiting', 'preparing', 'uploading'];

/** The statuses of a file that takes its record's room when a batch is taken: all but a refused one. */
export const TAKING_ROOM: readonly FileUploadStatus[] = ['waiting', 'preparing', 'uploading', 'uploaded', 'failed'];

/**
 * Where the files go: a file grid, the parent they go under, and the title a committed upload's warnings take, with the
 * grid's `FileUploadLimitsDto`: its limits, its image rules, the room a record has (`maxFiles` and `maxTotalBytes`, 0 for
 * no bound) and the server's bounds of every upload (`maxPixels`, `thumbnailMaxBytes`).
 */
export interface FileUploadTarget extends Readonly<FileUploadLimitsDto> {
  readonly service: string;
  readonly parentService?: string | null;
  readonly parentId?: number | null;
  readonly committedWarningTitle?: string | null;
}

/** One file its owner shows under the target: its key and its size in bytes, which take the record's room. */
export interface ShownFile {
  readonly id: GridRowKey;
  readonly size: number;
}

/** One file the user gave, where it goes, where it stands, how much of it is sent, and why it did not go. */
export interface FileUpload {
  readonly id: number;
  readonly file: File;
  readonly target: FileUploadTarget;
  /** The pick, drop or paste the file came in: the files of one are placed first among the record's, in the order given. */
  readonly batch: number;
  readonly status: FileUploadStatus;
  /** The percentage of the body sent, while it posts. */
  readonly progress: number;
  readonly detail?: string;
  /** The key the server answered for the file it stored; none before it is uploaded. */
  readonly fileId?: number | null;
}

/** Whether an upload goes under the record of `place`: its file grid and its parent. */
export function isUnder(upload: FileUpload, place: Pick<FileUploadTarget, 'service' | 'parentId'>): boolean {
  return upload.target.service === place.service && upload.target.parentId === place.parentId;
}

/** The text of each status. */
export const FILE_UPLOAD_TEXTS = {
  refused: 'FileRefused',
  waiting: 'FileWaiting',
  preparing: 'FilePreparing',
  uploading: 'FileUploading',
  uploaded: 'FileUploaded',
  failed: 'FileUploadFailed',
} satisfies Record<FileUploadStatus, UiTextKey>;

/** The ink of each status: a refused or failed file in the error's hue, an uploaded one in the success hue. */
export const FILE_UPLOAD_CLASSES = {
  refused: 'hue-red text-(--hue-ink)',
  waiting: 'text-muted',
  preparing: 'text-muted',
  uploading: 'text-muted',
  uploaded: 'hue-green text-(--hue-ink)',
  failed: 'hue-red text-(--hue-ink)',
} satisfies Record<FileUploadStatus, string>;

/**
 * What a file posts as, made once at its first turn and kept for a retry: the file, its thumbnail and the idempotency
 * key every post of it carries, so a file whose answer was lost is added once however often it is retried.
 */
export interface PreparedUpload {
  readonly file: File;
  readonly thumbnail: Blob | null;
  readonly key: string;
}

/** Builds the initial queue entry for a picked, dropped or pasted file, refusing oversized or unaccepted files upfront. */
export function createUploadItem(
  id: number,
  file: File,
  target: FileUploadTarget,
  batch: number,
  texts: TextsService,
  locale: string,
): FileUpload {
  const refusal = uploadRefusalOf(file, target);
  if (!refusal) {
    return { id, file, target, batch, status: 'waiting', progress: 0 };
  }
  const detail =
    refusal === 'size'
      ? texts.of('FileTooLarge', formatMegabytes(takenMaxBytesOf(file.type, target), locale))
      : texts.of('FileTypeRefused', acceptedNamesOf(target));
  return { id, file, target, batch, status: 'refused', progress: 0, detail };
}

/**
 * What the file posts as: an image of more than the target's `maxPixels` refused by the size its leading bytes tell,
 * else the file treated by the target's image rules, refused above `maxBytes`, with the thumbnail of what it became,
 * within `thumbnailMaxBytes`. A refusal is its reason.
 */
export async function prepareUpload(upload: FileUpload, texts: TextsService, locale: string): Promise<PreparedUpload | string> {
  const { file, target } = upload;
  const facts = isEncodedImage(file.type) ? await imageFactsOf(file) : null;
  if (facts && facts.width * facts.height > target.maxPixels) {
    return texts.of('FilePixelsRefused', new Intl.NumberFormat(locale).format(target.maxPixels / 1_000_000));
  }
  const posted = await optimizeImage(file, target, facts);
  if (posted.size > target.maxBytes) {
    return texts.of('FileTooLarge', formatMegabytes(target.maxBytes, locale));
  }
  return {
    file: posted,
    thumbnail: await thumbnailOf(posted, target.thumbnailMaxBytes),
    key: newUuid(),
  };
}

import { DestroyRef, inject } from '@angular/core';
import type { CloseApproval, CloseAttempt } from '../../core/close-decision';
import type { FileUploadLimitsDto, FormFilesModel, GridRequest } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import type { FileActionMethod, FileActionRequest } from '../../file/file-actions';
import type { FileReorderItem } from '../../file/file-reorder.directive';
import { formatBytes } from '../../file/file-size';
import { acceptedNamesOf, cameraAcceptOf, pickerAcceptOf, type UploadAccept, videoAcceptOf } from '../../file/file-upload-call';
import { type FileUpload, type FileUploadQueue, type FileUploadTarget, isUnder } from '../../file/file-upload-queue';
import { type FileView, fileViewOf, fileViewOfUpload, rowsOfPage } from '../../file/file-view';
import { withSelection } from '../../grid/grid-request';
import { type GridRow, type GridRowKey, keyOf } from '../../grid/grid-row';
import { ConfirmationService } from '../../overlay/confirmation.service';
import { OverlayRef } from '../../overlay/overlay-ref';
import { ScreenLifecycle } from '../../screen/screen-lifecycle';
import { FIELD_FOCUS_WITHIN, FIELD_HOVER } from '../../theme/css-classes';

/** One file of the page the element draws: its row and the file it is. */
export interface FileRow {
  readonly row: GridRow;
  readonly file: FileView;
}

/** One file the element draws: a row of its page, or a file of the upload queue on its way under this record. */
export interface FileItem {
  readonly id: string;
  readonly file: FileView;
  readonly row: GridRow | null;
  readonly upload: FileUpload | null;
}

/** The files and bytes a record holds, by what the element knows of it. */
export interface FilesHeld {
  readonly files: number;
  readonly bytes: number;
}

/** The record the element's files stand under: its file grid and its parent. */
type FilesPlace = Pick<FormFilesModel, 'service' | 'parentId'>;

/** The files of each look: a grid of tiles as wide as the element allows, or the lines of a list. */
export const FILES_LIST_CLASSES = {
  tiles: 'm-0 p-0 list-none grid grid-cols-[repeat(auto-fill,minmax(8rem,1fr))] gap-2',
  list: 'm-0 p-0 list-none flex flex-col divide-y divide-line',
} as const;

/** The zone files enter by: a field's pale edge, turning blue under the pointer or a drag and fuchsia with the focus in it. */
const ZONE = `flex flex-col gap-2 p-2 min-w-0 rounded-lg border bg-field ${FIELD_HOVER} ${FIELD_FOCUS_WITHIN}`;

/** The classes of the element's body: the zone while it takes files, its edge blue under a drag; a plain block else. */
export function filesZoneClasses(isIntake: boolean, isDragging: boolean): string {
  return isIntake ? `${ZONE} ${isDragging ? 'border-field-hover' : 'border-field-edge'}` : 'block min-w-0';
}

/** The rows of a file grid's page of data that carry a key, with the files they are. */
export function fileRowsOf(data: unknown): readonly FileRow[] {
  return rowsOfPage(data).flatMap(row => {
    const key = keyOf(row);
    return key === null ? [] : [{ row, file: fileViewOf(row, key) }];
  });
}

/** The rows in the order a move placed their keys, a row the order does not name last; the rows as given without one. */
export function inPlacedOrder(rows: readonly FileRow[], placed: readonly GridRowKey[] | null): readonly FileRow[] {
  if (!placed) {
    return rows;
  }
  const at = new Map<GridRowKey | null, number>(placed.map((key, index) => [key, index]));
  return [...rows].sort((a, b) => (at.get(a.file.key) ?? rows.length) - (at.get(b.file.key) ?? rows.length));
}

/**
 * The files on their way first, in the order given, as the server places them, then the files of the page, in the
 * order of the handles while a file is being moved (`order`), a file being moved where it stands now.
 */
export function fileItemsOf(
  uploads: readonly FileUpload[],
  rows: readonly FileRow[],
  order?: readonly FileReorderItem[] | null,
): readonly FileItem[] {
  const byKey = new Map(rows.map(item => [item.file.key, item]));
  const shown = order ? order.flatMap(({ key }) => byKey.get(key) ?? []) : rows;
  return [
    ...uploads.map(upload => ({ id: `upload-${upload.id}`, file: fileViewOfUpload(upload.file), row: null, upload })),
    ...shown.map(({ row, file }) => ({ id: `row-${file.key}`, file, row, upload: null })),
  ];
}

/**
 * The files of the upload queue going under the target's record: each until the page shows it — an uploaded one while
 * `isShowingUploaded` (the queue posts or the page is asked again), and not once the page holds its key — and a
 * refused or failed one until the user removes it.
 */
export function uploadsShownOf(
  uploads: readonly FileUpload[],
  target: FileUploadTarget | null,
  rows: readonly FileRow[],
  isShowingUploaded: boolean,
): readonly FileUpload[] {
  if (!target) {
    return [];
  }
  const keys = new Set(rows.map(({ file }) => file.key));
  return uploads.filter(
    upload =>
      isUnder(upload, target) &&
      (upload.status !== 'uploaded' || (isShowingUploaded && (upload.fileId == null || !keys.has(upload.fileId)))),
  );
}

/**
 * The files and bytes the record holds, by what the element knows: the files of the page, and the ones uploaded since
 * the page was asked, until it shows them; one whose key the server did not answer only while `isShowingUploaded`.
 * An uploaded file counts the bytes the user gave, the page's the bytes the server holds.
 */
export function filesHeldOf(
  rows: readonly FileRow[],
  uploads: readonly FileUpload[],
  place: FilesPlace | null,
  isShowingUploaded: boolean,
): FilesHeld {
  const keys = new Set(rows.map(({ file }) => file.key));
  const uploaded = place
    ? uploads.filter(
        upload =>
          upload.status === 'uploaded' && isUnder(upload, place) && (upload.fileId == null ? isShowingUploaded : !keys.has(upload.fileId)),
      )
    : [];
  return {
    files: rows.length + uploaded.length,
    bytes: rows.reduce((sum, { file }) => sum + (file.size ?? 0), 0) + uploaded.reduce((sum, upload) => sum + upload.file.size, 0),
  };
}

/** Whether a record holding `held` holds `maxFiles` files or `maxTotalBytes`: no file enters it then. */
export function isFilesFull(limits: Pick<FileUploadLimitsDto, 'maxFiles' | 'maxTotalBytes'>, held: FilesHeld): boolean {
  const { maxFiles, maxTotalBytes } = limits;
  return (maxFiles > 0 && held.files >= maxFiles) || (maxTotalBytes > 0 && held.bytes >= maxTotalBytes);
}

/**
 * The count of the record's files against `maxFiles` (`7 of 50`), naming the record full when it is; without
 * `maxFiles`, the text of no room when the record is full, else none.
 */
export function filesCountText(
  texts: Pick<TextsService, 'of'>,
  limits: Pick<FileUploadLimitsDto, 'maxFiles'>,
  held: FilesHeld,
  isFull: boolean,
): string {
  if (limits.maxFiles <= 0) {
    return isFull ? texts.of('FilesNoRoom') : '';
  }
  return texts.of(isFull ? 'FilesFull' : 'FilesCount', held.files, limits.maxFiles);
}

/**
 * What each of the element's pickers takes: the browser's picker every accepted type and extension, the camera's photo
 * the accepted raster images and its video the accepted videos; none of them anything without limits.
 */
export function filesAcceptsOf(limits: UploadAccept | null | undefined): {
  readonly picker: string;
  readonly camera: string;
  readonly video: string;
} {
  return limits
    ? { picker: pickerAcceptOf(limits), camera: cameraAcceptOf(limits), video: videoAcceptOf(limits) }
    : { picker: '', camera: '', video: '' };
}

/** The upload's limits, shown before any pick: `PNG, JPG, PDF - up to 10 MB`. */
export function filesLimitsText(
  texts: Pick<TextsService, 'of'>,
  limits: Pick<FileUploadLimitsDto, 'accept' | 'extensions' | 'maxBytes'>,
  locale: string,
): string {
  return texts.of('FileLimits', acceptedNamesOf(limits), formatBytes(limits.maxBytes, locale));
}

/**
 * Where the element's files go: the file grid under the model's record, within its limits, a committed answer's
 * warnings shown under the form's title; null while nothing may be uploaded, the record not saved yet among it.
 */
export function filesTargetOf(model: FormFilesModel | null | undefined, committedWarningTitle?: string | null): FileUploadTarget | null {
  if (!model?.canUpload || model.parentId == null) {
    return null;
  }
  return {
    ...model.limits,
    service: model.service,
    parentService: model.parentService,
    parentId: model.parentId,
    committedWarningTitle,
  };
}

/**
 * The request of a file action under the model's record: its file grid, and the body naming the record and the files
 * `rows` as every grid action names its selection, their versions with them; the record alone where `rows` is null.
 * None for a record not saved yet.
 */
export function fileActionRequestOf(
  model: FormFilesModel | null | undefined,
  method: FileActionMethod,
  rows: readonly GridRow[] | null,
): FileActionRequest | null {
  if (!model || model.parentId == null) {
    return null;
  }
  const params: GridRequest = { parentService: model.parentService, parentId: model.parentId };
  return { service: model.service, body: rows ? withSelection(params, rows) : params };
}

/**
 * Where the focus goes once the file viewer closed, when the element that opened it is gone: to the first file left in
 * the zone, else to the zone where it takes focus; nowhere while the focus stands on another element.
 */
export function focusAfterViewer(zone: HTMLElement | undefined): void {
  const document = zone?.ownerDocument;
  const active = document?.activeElement;
  if (!zone || !document || (active && active !== document.body)) {
    return;
  }
  const target = zone.querySelector<HTMLElement>('[data-file-open]') ?? (zone.tabIndex === 0 ? zone : null);
  target?.focus();
}

/**
 * Holds the form in an overlay, the routed screen and the page while a file of `queue` is on its way: a leave or a
 * close is approved at once when none is, else once the user confirms it, which cancels the files; given up, the
 * attempt closes the question unanswered. A reset of the screen cancels the files. Called in the injection context of
 * the element that provides the queue; the registrations end with it.
 */
export function holdLeaveWhileUploading(queue: FileUploadQueue): void {
  const confirmation = inject(ConfirmationService);
  const texts = inject(TextsService);
  const approveLeave = async (request: CloseAttempt): Promise<boolean | CloseApproval> => {
    if (request.signal?.aborted) {
      return false;
    }
    if (!queue.isPosting()) {
      return true;
    }
    const confirmed = await confirmation.confirm(
      { message: texts.of('FilesUploadingLeave'), title: texts.of('Leave'), confirmLabel: texts.of('Leave'), color: 'red' },
      request.signal,
    );
    if (!confirmed || request.signal?.aborted) {
      return false;
    }
    queue.cancelAll();
    return true;
  };
  // The form's own guard says why the leave is asked
  const ref = inject(OverlayRef, { optional: true });
  const unregisterClose = ref?.registerCloseParticipant(approveLeave);
  const unregisterOwner = inject(ScreenLifecycle, { optional: true })?.registerOwner({
    closeParticipant: ref ? undefined : approveLeave,
    needsProtection: () => queue.isPosting(),
    reset: () => queue.cancelAll(),
  });
  inject(DestroyRef).onDestroy(() => {
    unregisterClose?.();
    unregisterOwner?.();
  });
}

import { HttpClient, HttpEventType, HttpRequest } from '@angular/common/http';
import { computed, DestroyRef, inject, Service, signal } from '@angular/core';
import type { Subscription } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { withIdempotencyKey } from '../app/auth/credentials.interceptor';
import { LocaleService } from '../app/locale.service';
import { type AnswerOf, serviceUrl } from '../core/api';
import type { FileGridServiceEndpoints, FileMetadataDto, FileUploadLimitsDto } from '../core/generated/contracts';
import { type IsCurrent, Lifetime } from '../core/lifetime';
import { classifyMutationFailure, type MutationFailure, refusalOf } from '../core/mutation-failure';
import { TextsService, type UiTextKey } from '../core/texts.service';
import type { GridRowKey } from '../grid/grid-row';
import { OverlayRef } from '../overlay/overlay-ref';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { isEncodedImage } from './content-type';
import { isCommitted } from './file-actions';
import { optimizeImage } from './file-optimize';
import { formatMegabytes } from './file-size';
import { thumbnailOf } from './file-thumbnail';
import { acceptedNamesOf, takenMaxBytesOf, uploadFormOf, uploadRefusalOf } from './file-upload-call';
import { imageFactsOf } from './image-facts';

/**
 * Where a file of the queue stands: refused before posting, waiting for its turn, being prepared at its turn (its facts
 * read and its image treated), posting, answered, or failed.
 */
export type FileUploadStatus = 'refused' | 'waiting' | 'preparing' | 'uploading' | 'uploaded' | 'failed';

/** The statuses of a file on its way: waiting, being prepared or posting; what a leave asks about and Cancel stops. */
export const UNDER_WAY: readonly FileUploadStatus[] = ['waiting', 'preparing', 'uploading'];

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
 * What a file posts as, made once at its first turn and kept for a retry: the file, its metadata, its thumbnail and the
 * idempotency key every post of it carries, so a file whose answer was lost is added once however often it is retried.
 */
interface Prepared {
  readonly file: File;
  readonly metadata: FileMetadataDto | null;
  readonly thumbnail: Blob | null;
  readonly key: string;
}

/** The request in flight: its file, and how to abort it and end its turn. */
interface InFlight {
  readonly id: number;
  readonly subscription: Subscription;
  readonly settle: () => void;
}

/** The statuses of a file that takes its record's room when a batch is taken: all but a refused one. */
const TAKING_ROOM: readonly FileUploadStatus[] = ['waiting', 'preparing', 'uploading', 'uploaded', 'failed'];

/**
 * The files on their way to a file grid, shared by the upload dialog and a form's files element, and provided by the
 * component that draws them (`ui/grid`, "The upload dialog"). A file of a type outside the accepted ones, with a name
 * ending in none of their extensions, or larger than the upload takes — `sourceMaxBytes` for an image its rule makes
 * smaller, else `maxBytes` — is refused here with its reason and never posted. So is every file of a pick, drop or paste
 * beyond the room its record has left: `maxFiles` and `maxTotalBytes` less the files its owner shows
 * (`registerShownFiles`) and the ones the queue holds for it; the files that fit post, in order.
 *
 * The others post one at a time. At its turn a JPEG, PNG or WebP image has its size and metadata read from its leading
 * bytes, and is refused over the target's `maxPixels`; it is then treated by the upload's image rules (`optimizeImage`), and
 * the file it becomes is refused above `maxBytes`, or, with the rest of its batch, when it no longer fits the record's
 * room. Each posts alone as a multipart form with its parent, the thumbnail the browser made of a raster image or of an
 * MP4 video's first frame (none where it could not draw one), the image's `metadata` whatever the image became, and
 * `afterId`: none for the first file of its batch, which is placed first, and for each next one the key the server
 * answered for the nearest file before it in the batch, so the batch keeps its order; a retried file names its own
 * predecessor's. Each shows its status — preparing while its facts are read and its image treated, then uploading — and,
 * while it posts, its progress. A refusal of the server or a failed request marks the file failed with the reason; a
 * committed answer shows its warnings. A failed file may be retried; one on its way (`UNDER_WAY`) cancelled, which
 * removes it and aborts its request; a refused, failed or uploaded one removed.
 *
 * The queue works while the screen and the overlay it stands in do; its owner going away aborts the post in flight.
 */
@Service({ autoProvided: false })
export class FileUploadQueue {
  private readonly http = inject(HttpClient);
  private readonly alert = inject(AlertService);
  private readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;
  private readonly ref = inject(OverlayRef, { optional: true });

  private readonly _uploads = signal<readonly FileUpload[]>([]);
  /** The files given, in the order given. */
  readonly uploads = this._uploads.asReadonly();

  /** Whether a file is waiting, being prepared or posting: what a leave asks about. */
  readonly isPosting = computed(() => this._uploads().some(upload => UNDER_WAY.includes(upload.status)));

  /** The queue's own lifetime inside its screen's: over once its overlay closes or its owner goes away. */
  private readonly lifetime = (inject(SCREEN_LIFETIME, { optional: true }) ?? new Lifetime()).child(() => !this.ref?.isClosed);
  private readonly drainedCallbacks = new Set<() => void>();
  private readonly prepared = new Map<number, Prepared>();
  /** The upload ids of each batch, in the order given; kept after a file leaves the queue, for its batch's order. */
  private readonly batches = new Map<number, readonly number[]>();
  /** The key the server answered for each uploaded file, by its upload id; kept after it leaves the queue. */
  private readonly answeredIds = new Map<number, number>();
  private shownFiles: (() => readonly ShownFile[]) | null = null;
  private nextId = 0;
  private nextBatch = 0;
  private isDraining = false;
  private inFlight: InFlight | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.lifetime.end();
      this.inFlight?.subscription.unsubscribe();
      this.inFlight = undefined;
    });
  }

  /**
   * Registers what the owner shows under the target it gives: its files, whose count and sizes take the record's room.
   * Returns the function that cancels it.
   */
  registerShownFiles(source: () => readonly ShownFile[]): () => void {
    this.shownFiles = source;
    return () => {
      if (this.shownFiles === source) {
        this.shownFiles = null;
      }
    };
  }

  /**
   * Takes the files given for `target` as one batch: a refused one is marked with its reason, as is each one beyond the
   * room its record has left; the others wait for their turn to post.
   */
  add(files: readonly File[], target: FileUploadTarget): void {
    if (files.length === 0) {
      return;
    }
    const batch = ++this.nextBatch;
    let room = target.maxFiles > 0 ? target.maxFiles - this.roomTakenBy(target, TAKING_ROOM).files : Infinity;
    const added = files.map(file => {
      const upload = this.uploadOf(file, target, batch);
      if (upload.status !== 'waiting') {
        return upload;
      }
      if (room <= 0) {
        return { ...upload, status: 'refused' as const, detail: this.texts.of('FilesNoRoom') };
      }
      room--;
      return upload;
    });
    this.batches.set(
      batch,
      added.map(upload => upload.id),
    );
    this._uploads.update(uploads => [...uploads, ...added]);
    void this.drain();
  }

  /** Posts a failed file again, in its turn, after the file its batch placed before it. */
  retry(id: number): void {
    if (!this._uploads().some(upload => upload.id === id && upload.status === 'failed')) {
      return;
    }
    this.update(id, { status: 'waiting', progress: 0, detail: undefined });
    void this.drain();
  }

  /**
   * Cancels a waiting, preparing or posting file: it leaves the queue, and its request, when one is under way, is
   * aborted; one being prepared is dropped once its preparation ends.
   */
  cancel(id: number): void {
    if (!this.isUnderWay(id)) {
      return;
    }
    this._uploads.update(uploads => uploads.filter(upload => upload.id !== id));
    this.prepared.delete(id);
    if (this.inFlight?.id === id) {
      const { subscription, settle } = this.inFlight;
      this.inFlight = undefined;
      subscription.unsubscribe();
      settle();
    }
  }

  /** Cancels every file on its way, as a confirmed leave does. */
  cancelAll(): void {
    this._uploads()
      .filter(upload => UNDER_WAY.includes(upload.status))
      .forEach(upload => this.cancel(upload.id));
  }

  /** Forgets a file that is not on its way. */
  remove(id: number): void {
    if (!this._uploads().some(upload => upload.id === id && !UNDER_WAY.includes(upload.status))) {
      return;
    }
    this._uploads.update(uploads => uploads.filter(upload => upload.id !== id));
    this.prepared.delete(id);
  }

  /** Forgets the uploaded files, once their owner shows them from its page. */
  clearUploaded(): void {
    this._uploads()
      .filter(upload => upload.status === 'uploaded')
      .forEach(upload => this.prepared.delete(upload.id));
    this._uploads.update(uploads => uploads.filter(upload => upload.status !== 'uploaded'));
  }

  /** Calls `callback` each time the last waiting file has had its answer; returns the function that cancels it. */
  onDrained(callback: () => void): () => void {
    this.drainedCallbacks.add(callback);
    return () => this.drainedCallbacks.delete(callback);
  }

  private uploadOf(file: File, target: FileUploadTarget, batch: number): FileUpload {
    const id = ++this.nextId;
    const refusal = uploadRefusalOf(file, target);
    if (!refusal) {
      return { id, file, target, batch, status: 'waiting', progress: 0 };
    }
    const detail =
      refusal === 'size'
        ? this.texts.of('FileTooLarge', formatMegabytes(takenMaxBytesOf(file.type, target), this.locale()))
        : this.texts.of('FileTypeRefused', acceptedNamesOf(target));
    return { id, file, target, batch, status: 'refused', progress: 0, detail };
  }

  /**
   * The files and bytes of the target's record: those its owner shows, and the queue's own files for it of the
   * statuses given, an uploaded one only while its owner does not show it yet.
   */
  private roomTakenBy(target: FileUploadTarget, statuses: readonly FileUploadStatus[]): { files: number; bytes: number } {
    const shown = this.shownFiles?.() ?? [];
    const shownIds = new Set(shown.map(file => file.id));
    const own = this._uploads().filter(
      upload => isUnder(upload, target) && statuses.includes(upload.status) && (upload.fileId == null || !shownIds.has(upload.fileId)),
    );
    return {
      files: shown.length + own.length,
      bytes:
        shown.reduce((sum, file) => sum + file.size, 0) +
        own.reduce((sum, upload) => sum + (this.prepared.get(upload.id)?.file.size ?? upload.file.size), 0),
    };
  }

  /** Posts the waiting files one after another, until none waits; a second call while it runs leaves it to the first. */
  private async drain(): Promise<void> {
    if (this.isDraining) {
      return;
    }
    this.isDraining = true;
    let posted = false;
    try {
      let next = this.nextWaiting();
      while (next && this.lifetime.isCurrent()) {
        posted = (await this.post(next)) || posted;
        next = this.nextWaiting();
      }
    } finally {
      this.isDraining = false;
    }
    if (posted && this.lifetime.isCurrent()) {
      this.drainedCallbacks.forEach(callback => callback());
    }
  }

  private nextWaiting(): FileUpload | undefined {
    return this._uploads().find(upload => upload.status === 'waiting');
  }

  /**
   * Prepares the file at its first turn, checks it against its record's room, posts it and settles once its answer, its
   * failure or its cancel comes; answers whether a request went. An owner that goes away meanwhile aborts the request;
   * the loop that awaits it goes with the owner.
   */
  private async post(upload: FileUpload): Promise<boolean> {
    const isCurrent = this.lifetime.capture();
    const prepared = await this.admit(upload, isCurrent);
    if (!prepared) {
      return false;
    }
    this.update(upload.id, { status: 'uploading' });
    await this.send(upload, this.requestOf(upload, prepared), isCurrent);
    return true;
  }

  /**
   * The file prepared at its first turn and let into its record's room; null where it was cancelled meanwhile,
   * refused, or left with the rest of its batch for want of room.
   */
  private async admit(upload: FileUpload, isCurrent: IsCurrent): Promise<Prepared | null> {
    const { id, target } = upload;
    this.update(id, { status: this.prepared.has(id) ? 'uploading' : 'preparing', progress: 0 });
    const prepared = this.prepared.get(id) ?? (await this.prepare(upload));
    if (!isCurrent() || !this.isUnderWay(id)) {
      // Cancelled while it was being prepared
      return null;
    }
    if (typeof prepared === 'string') {
      this.update(id, { status: 'refused', progress: 0, detail: prepared });
      return null;
    }
    this.prepared.set(id, prepared);
    if (this.exceedsRoom(target, prepared.file.size)) {
      this.refuseRestOf(upload);
      return null;
    }
    return prepared;
  }

  /** The request of the grid's `upload` for the prepared file: its form under its key, its progress reported, the alert silent. */
  private requestOf(upload: FileUpload, prepared: Prepared): HttpRequest<FormData> {
    const { target } = upload;
    return new HttpRequest<FormData>(
      'POST',
      serviceUrl<FileGridServiceEndpoints>(target.service, 'upload'),
      uploadFormOf(target, prepared.file, prepared.thumbnail, { metadata: prepared.metadata, afterId: this.predecessorIdOf(upload) }),
      { reportProgress: true, ...withIdempotencyKey(prepared.key) },
    );
  }

  /** Sends the request; resolves once its answer, its failure or its cancel settles it. */
  private send(upload: FileUpload, request: HttpRequest<FormData>, isCurrent: IsCurrent): Promise<void> {
    const { id, target } = upload;
    return new Promise<void>(resolve => {
      let isSettled = false;
      const settle = (): void => {
        if (isSettled) {
          return;
        }
        isSettled = true;
        if (this.inFlight?.id === id) {
          this.inFlight = undefined;
        }
        resolve();
      };
      const subscription = this.http.request<AnswerOf<FileGridServiceEndpoints, 'upload'>>(request).subscribe({
        next: event => {
          if (!isCurrent()) {
            return;
          }
          if (event.type === HttpEventType.UploadProgress && event.total) {
            this.update(id, { progress: Math.round((event.loaded / event.total) * 100) });
          } else if (event.type === HttpEventType.Response) {
            this.answered(id, event.body, target);
          }
        },
        error: (error: unknown) => {
          // The failure is this file's, shown with it: the global alert was told to stay silent
          if (isCurrent()) {
            this.fail(id, classifyMutationFailure(error));
          }
          settle();
        },
        complete: settle,
      });
      if (!isSettled) {
        this.inFlight = { id, subscription, settle };
      }
    });
  }

  /**
   * What the file posts as: an image of more than the target's `maxPixels` refused by the size its leading bytes tell,
   * else the file treated by the target's image rules, refused above `maxBytes`, with the metadata its leading bytes
   * carry and the thumbnail of what it became, within `thumbnailMaxBytes`. A refusal is its reason.
   */
  private async prepare(upload: FileUpload): Promise<Prepared | string> {
    const { file, target } = upload;
    const facts = isEncodedImage(file.type) ? await imageFactsOf(file) : null;
    if (facts && facts.width * facts.height > target.maxPixels) {
      return this.texts.of('FilePixelsRefused', new Intl.NumberFormat(this.locale()).format(target.maxPixels / 1_000_000));
    }
    const posted = await optimizeImage(file, target, facts);
    if (posted.size > target.maxBytes) {
      return this.texts.of('FileTooLarge', formatMegabytes(target.maxBytes, this.locale()));
    }
    return {
      file: posted,
      metadata: facts?.metadata ?? null,
      thumbnail: await thumbnailOf(posted, target.thumbnailMaxBytes),
      key: crypto.randomUUID(),
    };
  }

  private isUnderWay(id: number): boolean {
    return this._uploads().some(upload => upload.id === id && UNDER_WAY.includes(upload.status));
  }

  /** Whether one more file of `size` bytes goes beyond the room of the target's record, by what it holds now. */
  private exceedsRoom(target: FileUploadTarget, size: number): boolean {
    const taken = this.roomTakenBy(target, ['uploaded']);
    return (
      (target.maxFiles > 0 && taken.files + 1 > target.maxFiles) || (target.maxTotalBytes > 0 && taken.bytes + size > target.maxTotalBytes)
    );
  }

  /** Refuses the file, and every file of its batch still waiting, as its record having no room left. */
  private refuseRestOf(upload: FileUpload): void {
    const detail = this.texts.of('FilesNoRoom');
    this._uploads.update(uploads =>
      uploads.map(other =>
        other.id === upload.id || (other.batch === upload.batch && other.status === 'waiting')
          ? { ...other, status: 'refused', progress: 0, detail }
          : other,
      ),
    );
  }

  /** The key the server answered for the nearest file before this one in its batch; none for the first one posted. */
  private predecessorIdOf(upload: FileUpload): number | null {
    const order = this.batches.get(upload.batch) ?? [];
    for (let index = order.indexOf(upload.id) - 1; index >= 0; index--) {
      const fileId = this.answeredIds.get(order[index]);
      if (fileId !== undefined) {
        return fileId;
      }
    }
    return null;
  }

  /** A committed answer marks the file uploaded with its key and shows its warnings; any other outcome is the file's failure. */
  private answered(id: number, answer: AnswerOf<FileGridServiceEndpoints, 'upload'> | null, target: FileUploadTarget): void {
    if (!isCommitted(answer)) {
      this.fail(id, classifyMutationFailure(answer));
      return;
    }
    const fileId = answer?.id ?? null;
    if (fileId !== null) {
      this.answeredIds.set(id, fileId);
    }
    this.update(id, { status: 'uploaded', progress: 100, fileId });
    this.alert.warnings(answer?.warnings, target.committedWarningTitle);
  }

  /** The file failed, with what the server said of it, or of the request, as its detail. */
  private fail(id: number, failure: MutationFailure): void {
    const refusal = refusalOf(failure, this.texts);
    this.update(id, { status: 'failed', detail: refusal.detail ?? refusal.title });
  }

  private update(id: number, change: Partial<Omit<FileUpload, 'id' | 'file' | 'target' | 'batch'>>): void {
    this._uploads.update(uploads => uploads.map(upload => (upload.id === id ? { ...upload, ...change } : upload)));
  }
}

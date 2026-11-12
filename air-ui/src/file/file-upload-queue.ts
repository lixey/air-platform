import { HttpClient, HttpEventType, HttpRequest } from '@angular/common/http';
import { computed, DestroyRef, inject, Service, signal } from '@angular/core';
import type { Subscription } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { withIdempotencyKey } from '../app/auth/credentials.interceptor';
import { LocaleService } from '../app/locale.service';
import { type AnswerOf, serviceUrl } from '../core/api';
import type { FileGridServiceEndpoints } from '../core/generated/contracts';
import { type IsCurrent, Lifetime } from '../core/lifetime';
import { classifyMutationFailure, type MutationFailure, refusalOf } from '../core/mutation-failure';
import { TextsService } from '../core/texts.service';
import { OverlayRef } from '../overlay/overlay-ref';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { isCommitted } from './file-actions';
import { uploadFormOf } from './file-upload-call';
import {
  createUploadItem,
  FILE_UPLOAD_CLASSES,
  FILE_UPLOAD_TEXTS,
  type FileUpload,
  type FileUploadStatus,
  type FileUploadTarget,
  isUnder,
  type PreparedUpload,
  prepareUpload,
  type ShownFile,
  TAKING_ROOM,
  UNDER_WAY,
} from './file-upload-item';

export {
  FILE_UPLOAD_CLASSES,
  FILE_UPLOAD_TEXTS,
  isUnder,
  UNDER_WAY,
  type FileUpload,
  type FileUploadStatus,
  type FileUploadTarget,
  type ShownFile,
};

/** The request in flight: its file, and how to abort it and end its turn. */
interface InFlight {
  readonly id: number;
  readonly subscription: Subscription;
  readonly settle: () => void;
}

/**
 * The files on their way to a file grid, shared by the upload dialog and a form's files element, and provided by the
 * component that draws them (`ui/grid`, "The upload dialog"). A file of a type outside the accepted ones, with a name
 * ending in none of their extensions, or larger than the upload takes — `sourceMaxBytes` for an image its rule makes
 * smaller, else `maxBytes` — is refused here with its reason and never posted. So is every file of a pick, drop or paste
 * beyond the room its record has left: `maxFiles` and `maxTotalBytes` less the files its owner shows
 * (`registerShownFiles`) and the ones the queue holds for it; the files that fit post, in order.
 *
 * The others post one at a time. At its turn a JPEG, PNG or WebP image has its dimensions and orientation read from its
 * leading bytes, and is refused over the target's `maxPixels`; it is then treated by the upload's image rules
 * (`optimizeImage`), and the file it becomes is refused above `maxBytes`, or, with the rest of its batch, when it no
 * longer fits the record's room. Each posts alone as a multipart form with its parent, the thumbnail the browser made of
 * a raster image or of an MP4 video's first frame (none where it could not draw one), and `afterId`: none for the first
 * file of its batch, which is placed first, and for each next one the key the server answered for the nearest file before
 * it in the batch, so the batch keeps its order; a retried file names its own predecessor's.
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
  private readonly prepared = new Map<number, PreparedUpload>();
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
      const upload = createUploadItem(++this.nextId, file, target, batch, this.texts, this.locale());
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

  private async admit(upload: FileUpload, isCurrent: IsCurrent): Promise<PreparedUpload | null> {
    const { id, target } = upload;
    this.update(id, { status: this.prepared.has(id) ? 'uploading' : 'preparing', progress: 0 });
    const prepared = this.prepared.get(id) ?? (await prepareUpload(upload, this.texts, this.locale()));
    if (!isCurrent() || !this.isUnderWay(id)) {
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
  private requestOf(upload: FileUpload, prepared: PreparedUpload): HttpRequest<FormData> {
    const { target } = upload;
    return new HttpRequest<FormData>(
      'POST',
      serviceUrl<FileGridServiceEndpoints>(target.service, 'upload'),
      uploadFormOf(target, prepared.file, prepared.thumbnail, { afterId: this.predecessorIdOf(upload) }),
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

  private isUnderWay(id: number): boolean {
    return this._uploads().some(upload => upload.id === id && UNDER_WAY.includes(upload.status));
  }

  private exceedsRoom(target: FileUploadTarget, size: number): boolean {
    const taken = this.roomTakenBy(target, ['uploaded']);
    return (
      (target.maxFiles > 0 && taken.files + 1 > target.maxFiles) || (target.maxTotalBytes > 0 && taken.bytes + size > target.maxTotalBytes)
    );
  }

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

  private fail(id: number, failure: MutationFailure): void {
    const refusal = refusalOf(failure, this.texts);
    this.update(id, { status: 'failed', detail: refusal.detail ?? refusal.title });
  }

  private update(id: number, change: Partial<Omit<FileUpload, 'id' | 'file' | 'target' | 'batch'>>): void {
    this._uploads.update(uploads => uploads.map(upload => (upload.id === id ? { ...upload, ...change } : upload)));
  }
}

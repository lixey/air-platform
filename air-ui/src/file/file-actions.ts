import { HttpClient } from '@angular/common/http';
import { DestroyRef, type Injector } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { type AnswerOf, Api, type RequestOf, serviceUrl } from '../core/api';
import { type DownloadedFile, fetchFile } from '../core/download';
import type { FileGridServiceEndpoints, GridRequest, MutationOutcome } from '../core/generated/contracts';
import type { IsCurrent } from '../core/lifetime';
import { classifyMutationFailure, refusalOf } from '../core/mutation-failure';
import { TextsService } from '../core/texts.service';
import type { GridRow } from '../grid/grid-row';

/** The file grid's methods an opener posts under its record for the files it names, or for all of them. */
export type FileActionMethod = 'set-cover' | 'move' | 'delete' | 'download-all';

/** The methods of `FileActionMethod` that answer a mutation's outcome rather than a file. */
export type FileMutationMethod = Exclude<FileActionMethod, 'download-all'>;

/** What a file action's body carries beside the record and the files it names: `move`'s `beforeId`, nothing for the rest. */
export type FileActionExtra<M extends FileActionMethod> = Omit<RequestOf<FileGridServiceEndpoints, M>, keyof GridRequest>;

/**
 * What a file action came to: its answer while the opener still stands for the record, `failed` once its failure was
 * reported, and null when the opener moved on meanwhile or stands in no record, and nothing is shown.
 */
export type FileActionOutcome<T> = { readonly answer: T } | 'failed' | null;

/** A file action's request: the file grid under the record and the body naming the record and the files. */
export interface FileActionRequest {
  readonly service: string;
  readonly body: GridRequest;
}

/** What an opener of a record's files says of them for `postFileAction`. */
export interface FileActionOpener {
  /** The opener's injector: the requests, the failure's report and the opener's end, after which no busy flag is reset. */
  readonly injector: Injector;
  /**
   * The request of `method` for the files `rows`, the files of none where `rows` is null; null where the opener stands
   * in no record, and nothing is posted.
   */
  readonly requestOf: (method: FileActionMethod, rows: readonly GridRow[] | null) => FileActionRequest | null;
  /** Takes the opener's currency when an action is asked, so an answer it no longer stands for is dropped. */
  readonly capture: () => IsCurrent;
}

/** The name `download-all` saves the record's files under when the answer names none. */
const ARCHIVE_NAME = 'files.zip';

/** Whether a mutation's answer committed: it names no outcome, or `committed`; so does no answer at all. */
export function isCommitted(answer: { readonly outcome?: MutationOutcome | null } | null | undefined): boolean {
  return !answer?.outcome || answer.outcome === 'committed';
}

/**
 * Reports a failure of a file action, or an answer that refuses it, as a refused action's is: the title and the
 * detail of `refusalOf`, a stale version under `conflictTitle` where one is given.
 */
export function reportFileRefusal(
  alert: AlertService,
  texts: Pick<TextsService, 'of'>,
  failure: unknown,
  conflictTitle?: string | null,
): void {
  const refusal = refusalOf(classifyMutationFailure(failure), texts, conflictTitle);
  alert.error(refusal.title, refusal.detail);
}

/**
 * Posts a file action of the opener's record: the file grid's `set-cover`, `move` or `delete` for the files `rows`, with
 * `extra` in the body, or its `download-all`, which names no files and answers the archive. `setBusy` is raised while
 * the request is under way and lowered after it unless the opener is gone. An answer the opener no longer stands for is
 * dropped; a failure it still stands for is reported here, as a refused action's is, the global alert told to stay
 * silent.
 */
export function postFileAction(
  opener: FileActionOpener,
  method: 'download-all',
  rows: null,
  extra: FileActionExtra<'download-all'>,
  setBusy: (busy: boolean) => void,
): Promise<FileActionOutcome<DownloadedFile>>;
export function postFileAction<M extends FileMutationMethod>(
  opener: FileActionOpener,
  method: M,
  rows: readonly GridRow[],
  extra: FileActionExtra<M>,
  setBusy: (busy: boolean) => void,
): Promise<FileActionOutcome<AnswerOf<FileGridServiceEndpoints, M>>>;
export async function postFileAction(
  opener: FileActionOpener,
  method: FileActionMethod,
  rows: readonly GridRow[] | null,
  extra: object,
  setBusy: (busy: boolean) => void,
): Promise<FileActionOutcome<unknown>> {
  const request = opener.requestOf(method, rows);
  if (!request) {
    return null;
  }
  const { injector } = opener;
  const destroyRef = injector.get(DestroyRef);
  const isCurrent = opener.capture();
  const body = { ...request.body, ...extra };
  setBusy(true);
  try {
    const answer =
      method === 'download-all'
        ? await fetchFile(
            injector.get(HttpClient),
            serviceUrl<FileGridServiceEndpoints>(request.service, method),
            body,
            ARCHIVE_NAME,
            shownByCaller(),
          )
        : await firstValueFrom(
            injector.get(Api).postTo<FileGridServiceEndpoints, typeof method>(request.service, method, body, shownByCaller()),
          );
    return isCurrent() ? { answer } : null;
  } catch (error: unknown) {
    // The failure is reported here: the global alert was told to stay silent
    if (!isCurrent()) {
      return null;
    }
    reportFileRefusal(injector.get(AlertService), injector.get(TextsService), error);
    return 'failed';
  } finally {
    if (!destroyRef.destroyed) {
      setBusy(false);
    }
  }
}

import { computed, DestroyRef, DOCUMENT, inject, Injector, linkedSignal, signal } from '@angular/core';
import { AlertService } from '../../app/alert/alert.service';
import { LocaleService } from '../../app/locale.service';
import { saveFile, stampedName } from '../../core/download';
import type { FormFilesModel, GridActionDto, GridDeleteDto } from '../../core/generated/contracts';
import type { IsCurrent } from '../../core/lifetime';
import { TextsService } from '../../core/texts.service';
import { type FileActionOpener, isCommitted, postFileAction, reportFileRefusal } from '../../file/file-actions';
import type { FileReorderMove } from '../../file/file-reorder.directive';
import type { FileView } from '../../file/file-view';
import type { GridRow, GridRowKey } from '../../grid/grid-row';
import { ConfirmationService } from '../../overlay/confirmation.service';
import { FormService } from '../form.service';
import { fileActionRequestOf, type FileItem, type FileRow } from './form-files';

/** What the file actions are given of their element. */
export interface FormFilesActionsOptions {
  /** The element's model: the file grid, the record and what the model allows. */
  readonly model: () => FormFilesModel | null | undefined;
  /** Whether the record is not saved yet. */
  readonly isNew: () => boolean;
  /** The rows of the page with their files, in the page's order. */
  readonly rows: () => readonly FileRow[];
  /** Whether a file may be moved now. */
  readonly canMove: () => boolean;
  /** Asks the page again. */
  readonly reload: () => void;
}

/**
 * The actions over the files of a form's record: the pin of the cover, a file's move, the delete of one and of all
 * after a confirmation, and the archive. Created in a field of the files element; it is given the element's model,
 * rows and page, and takes the form, the alert, the texts and the confirmation from the element's injector.
 */
export class FormFilesActions {
  private readonly form = inject(FormService);
  private readonly alert = inject(AlertService);
  private readonly texts = inject(TextsService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly timeZone = inject(LocaleService).zone;

  private readonly _deleting = signal<GridRowKey | null>(null);
  /** The file whose delete is posting. */
  readonly deleting = this._deleting.asReadonly();
  private readonly _isDeletingAll = signal(false);
  /** Whether "Delete all" is posting. */
  readonly isDeletingAll = this._isDeletingAll.asReadonly();
  private readonly _isDownloadingAll = signal(false);
  /** Whether "Download all" is asking for the archive. */
  readonly isDownloadingAll = this._isDownloadingAll.asReadonly();
  private readonly _settingCover = signal<GridRowKey | null>(null);
  /** The file whose pin is posting. */
  readonly settingCover = this._settingCover.asReadonly();
  private readonly _isMovingFile = signal(false);
  /** Whether a file's `move` is posting. */
  readonly isMovingFile = this._isMovingFile.asReadonly();

  /**
   * The order a move dropped the files in, held while its `move` posts and until the page answers again; forgotten
   * with every new page, and when the move fails.
   */
  private readonly _placed = linkedSignal<unknown, readonly GridRowKey[] | null>({
    source: () => this.options.rows(),
    computation: () => null,
  });
  /** The order a move put the files in until the page answers; null where the page's own order stands. */
  readonly placed = this._placed.asReadonly();

  /** Whether a delete, a pin or a move is posting. */
  readonly isBusy = computed(
    () => this._deleting() !== null || this._isDeletingAll() || this._settingCover() !== null || this._isMovingFile(),
  );

  /** What the element's file actions post under: the file grid under the record, the files named by their rows. */
  private readonly opener: FileActionOpener = {
    injector: this.injector,
    requestOf: (method, rows) => fileActionRequestOf(this.options.model(), method, rows),
    capture: () => this.capture(),
  };

  constructor(private readonly options: FormFilesActionsOptions) {}

  /** Takes the element's currency: the form's record, this model of the element, and the element itself. */
  capture(): IsCurrent {
    const isCurrent = this.form.capture();
    const model = this.options.model();
    return () => !this.destroyRef.destroyed && isCurrent() && this.options.model() === model;
  }

  /**
   * The pin of a file: makes it the record's cover, or, on the cover, clears it, through the file grid's `set-cover`
   * under the record, with the file's version. The page is asked again whatever the answer, which a refusal reports.
   */
  async setCover(item: FileItem): Promise<void> {
    const key = item.file.key;
    if (!this.options.model()?.canSetCover || this.options.isNew() || !item.row || key === null || this._settingCover() !== null) {
      return;
    }
    const rows = item.row['isCover'] === true ? [] : [item.row];
    const outcome = await postFileAction(this.opener, 'set-cover', rows, {}, busy => this._settingCover.set(busy ? key : null));
    if (outcome === 'failed') {
      this.options.reload();
    } else if (outcome) {
      this.committed(outcome.answer);
    }
  }

  /**
   * A file dropped at a new place: shown there at once, and posted to the file grid's `move` under the record with its
   * version and the file it now stands before, none when it stands last. A committed move asks the page again; a
   * refused or failed one puts the file back, is reported as a refused action is, and asks the page again as well.
   */
  async move(move: FileReorderMove): Promise<void> {
    const row = this.options.rows().find(item => item.file.key === move.key)?.row;
    if (!this.options.model()?.canUpload || this.options.isNew() || !row || !this.options.canMove()) {
      return;
    }
    const before = move.beforeKey === null ? {} : { beforeId: move.beforeKey };
    this._placed.set(move.keys);
    const outcome = await postFileAction(this.opener, 'move', [row], before, busy => this._isMovingFile.set(busy));
    if (outcome === 'failed') {
      this._placed.set(null);
      this.options.reload();
    } else if (outcome && !this.committed(outcome.answer)) {
      this._placed.set(null);
    }
  }

  /**
   * Saves every file of the record as one ZIP archive: the file grid's `download-all` under the record, saved under the
   * name its `Content-Disposition` gives (`files.zip` when it gives none) with the moment of the user's clock inserted
   * before `.zip` as `_yyMMddHHmmss`; a refusal, the archive's cap among them, is reported.
   */
  async downloadAll(): Promise<void> {
    if (this._isDownloadingAll()) {
      return;
    }
    const outcome = await postFileAction(this.opener, 'download-all', null, {}, busy => this._isDownloadingAll.set(busy));
    if (outcome && outcome !== 'failed') {
      saveFile({ blob: outcome.answer.blob, name: stampedName(outcome.answer.name, new Date(), this.timeZone()) }, this.document);
    }
  }

  /**
   * Deletes one file after the user confirms it: posted to the file grid's `delete` under the record, with the row's
   * version. A committed answer shows its warnings and asks the page again; any other is reported as a refused action's
   * is, and the page is asked again as well, since it may have changed.
   */
  async remove(item: FileItem): Promise<void> {
    await this.removeFile(item.file);
  }

  /**
   * Deletes every file of the page in one request, with their versions, after a confirmation naming their count and
   * asking the user to type it.
   */
  async removeAll(): Promise<void> {
    const rows = this.options.rows().map(({ row }) => row);
    if (rows.length > 0) {
      await this.deleteRows(
        rows,
        this.texts.of('DeleteAllFilesConfirm', rows.length),
        busy => this._isDeletingAll.set(busy),
        String(rows.length),
      );
    }
  }

  /** Deletes a file of the page after the user confirms it; resolves whether it was deleted. */
  async removeFile(file: FileView): Promise<boolean> {
    const key = file.key;
    const row = this.options.rows().find(item => item.file.key === key)?.row;
    if (!row || key === null) {
      return false;
    }
    return this.deleteRows([row], this.texts.of('DeleteFileConfirm', file.name), busy => this._deleting.set(busy ? key : null));
  }

  /** Deletes the rows after the user confirms, typing `typedAnswer` where one is given; resolves whether the delete committed. */
  private async deleteRows(
    rows: readonly GridRow[],
    message: string,
    setBusy: (busy: boolean) => void,
    typedAnswer?: string,
  ): Promise<boolean> {
    const model = this.options.model();
    if (!model?.canDelete || model.parentId == null || this._deleting() !== null || this._isDeletingAll()) {
      return false;
    }
    const isCurrent = this.capture();
    const confirmed = await this.confirmation.confirm({
      message,
      title: this.texts.of('Delete'),
      confirmLabel: this.texts.of('Delete'),
      color: 'red',
      ...(typedAnswer === undefined ? {} : { typedAnswer }),
    });
    if (!confirmed || !isCurrent()) {
      return false;
    }
    const outcome = await postFileAction(this.opener, 'delete', rows, {}, setBusy);
    return !!outcome && outcome !== 'failed' && this.committed(outcome.answer);
  }

  /** Shows the answer of a delete, a pin or a move — its warnings, or the refusal it is — and asks the page again; whether it committed. */
  private committed(answer: GridDeleteDto | GridActionDto): boolean {
    const hasCommitted = isCommitted(answer);
    if (hasCommitted) {
      this.alert.warnings(answer.warnings, this.form.model()?.mutationTexts.committedWarningTitle);
    } else {
      reportFileRefusal(this.alert, this.texts, answer);
    }
    this.options.reload();
    return hasCommitted;
  }
}

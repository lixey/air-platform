import { computed, inject, linkedSignal, type Signal, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { LocaleService } from '../app/locale.service';
import { Api } from '../core/api';
import type {
  GridCellDto,
  GridCellRequest,
  GridColumnDto,
  GridModel,
  GridRequest,
  GridServiceEndpoints,
  MessageDto,
} from '../core/generated/contracts';
import { valueAt } from '../core/json';
import type { IsCurrent } from '../core/lifetime';
import { classifyMutationFailure, type MutationFailure, refusalDetailOf, refusalOf } from '../core/mutation-failure';
import { TextsService } from '../core/texts.service';
import { ConfirmationService } from '../overlay/confirmation.service';
import { ScreenMutations, type ScreenOperation } from '../screen/screen-mutations';
import { cellQuestionOf } from './grid-cell-question';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';

/** The cell's method of every grid: its own key of the grid's endpoint map. */
export const GRID_SAVE_CELL = 'save-cell' satisfies keyof GridServiceEndpoints;

/** One cell being edited: the row's key and the column's path hold it, so a row drawn again finds it. */
export interface GridCellEdit {
  readonly key: GridRowKey;
  readonly path: string;
  /** What the editor holds: the cell's value at the start, then what the user put in. */
  readonly value: unknown;
  /** Whether the value is posted and its answer awaited: the editor takes no input meanwhile. */
  readonly isSaving: boolean;
  /** The server's messages at the column's path of the last refused save, drawn under the cell. */
  readonly messages: readonly MessageDto[];
  /** The key typed on the cell that started the edit, which the editor types into its box once; none after. */
  readonly typed?: string;
}

/** Where the active cell goes once a commit is answered: the row below, the row's next or previous editable cell, or nowhere. */
export type GridCellMove = 'down' | 'next' | 'previous' | 'stay';

/** What a column's editable cell is edited as, by its cell's kind: one of the editor's controls, or a checkbox that toggles. */
export type GridCellEditKind =
  'text' | 'email' | 'url' | 'number' | 'money' | 'percent' | 'date' | 'datetime' | 'time' | 'select' | 'checkbox';

/** The cells an editor takes, by kind; any other cell is never edited. */
const EDIT_KINDS: Readonly<Record<string, GridCellEditKind>> = {
  GridCellText: 'text',
  GridCellEmail: 'email',
  GridCellUrl: 'url',
  GridCellNumber: 'number',
  GridCellMoney: 'money',
  GridCellPercent: 'percent',
  GridCellDate: 'date',
  GridCellDatetime: 'datetime',
  GridCellTime: 'time',
  GridCellSelect: 'select',
  GridCellCheckbox: 'checkbox',
};

/** The kinds a printable key types into: a box whose text is the user's. */
const TYPED_KINDS: ReadonlySet<GridCellEditKind> = new Set(['text', 'email', 'url', 'number', 'money', 'percent']);

/** How a column's cell is edited in place; null for a column the model does not mark editable or a cell no editor takes. */
export function editKindOf(column: GridColumnDto | null | undefined): GridCellEditKind | null {
  if (!column?.isEditable || !column.path) {
    return null;
  }
  return Object.hasOwn(EDIT_KINDS, column.cell.kind) ? EDIT_KINDS[column.cell.kind] : null;
}

/** What the edits are given of their grid. */
export interface GridCellEditsOptions {
  /** What an edit belongs to: a change of the request — sort, filters, search, page — or of the model cancels it. */
  readonly resetBy: () => unknown;
  readonly model: () => GridModel | null;
  readonly service: () => string | null;
  /** The grid's parent parameters, as its read sends them. */
  readonly params: () => Pick<GridRequest, 'parentService' | 'parentId'>;
  /** The rows on the page, in its order. */
  readonly rows: () => readonly GridRow[];
  /** The columns the table draws, in its order: where `Tab` finds the next editable cell. */
  readonly columns: () => readonly GridColumnDto[];
  /** Whether the grid edits nothing in place: the list of a select or a picker. */
  readonly isPicking: () => boolean;
  readonly capture: () => IsCurrent;
  readonly beginMutation: () => ScreenOperation | undefined;
  readonly finishMutation: (operation: ScreenOperation | undefined) => void;
  /** Takes a committed cell's answer: the record's cells to be marked against the rows on screen, and its row replaced. */
  readonly adoptRow: (id: GridRowKey, row: GridRow | null) => void;
  /** Moves the active row by `delta` rows. */
  readonly moveActive: (delta: number) => void;
  /** Loads the same page again. */
  readonly reload: () => void;
}

/** What a save of one value came to: committed with the row answered, refused at the path, or failed and reported. */
type SaveResult =
  | { readonly kind: 'committed'; readonly row: GridRow | null }
  | { readonly kind: 'refused'; readonly messages: readonly MessageDto[] }
  | { readonly kind: 'failed' };

/**
 * The cells of one grid edited in place, saved one value at a time by the grid's form through `save-cell`. One edit is
 * held, by the row's key and the column's path, so the virtual body that draws a row again finds it; a row whose save
 * runs takes no second edit, and a change of the request or of the model cancels the edit. A commit posts the value
 * with the row's version and the grid's parent parameters, and nothing for a value equal to the cell's. Its answer
 * marks the record's changed cells on the page the reload loads, replaces the row by the answered one, tells the
 * screen, shows the commit's warnings, moves the active cell and loads the page again. A refusal with messages at the
 * column's path keeps the edit open with them; a conflict shows the model's conflict title, cancels and reloads; any
 * other failure is reported as a grid action's and cancels. A checkbox takes no edit: `toggle` saves its other value
 * at once. A column that `confirmsEdit` asks before either posts: `Cancel` posts nothing and keeps the edit as it is,
 * and while the question is open no second commit, toggle or start is taken.
 *
 * Created in a field of the grid service, so its injections are the service's. It knows nothing of the service: it is
 * given its options.
 */
export class GridCellEdits {
  private readonly api = inject(Api);
  private readonly alert = inject(AlertService);
  private readonly texts = inject(TextsService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly locale = inject(LocaleService);
  private readonly mutations = inject(ScreenMutations, { optional: true });

  /** The edit held, none once the request or the model changed. */
  private readonly edit = linkedSignal<unknown, GridCellEdit | null>({ source: () => this.options.resetBy(), computation: () => null });

  /** The keys of the rows whose save runs. */
  private readonly saving = signal<ReadonlySet<GridRowKey>>(new Set());

  /** Whether the question before a confirmed save is open: no second commit, toggle or start is taken meanwhile. */
  private isAsking = false;

  /** The edit held while its row is on the page; a reload that drops the row leaves none. */
  readonly current: Signal<GridCellEdit | null> = computed(() => {
    const edit = this.edit();
    return edit && this.options.rows().some(row => keyOf(row) === edit.key) ? edit : null;
  });

  constructor(private readonly options: GridCellEditsOptions) {}

  /** Whether the cell of this row and column is the one being edited; reads a signal. */
  isEditing(key: GridRowKey | null, path: string | null | undefined): boolean {
    const edit = this.current();
    return key !== null && !!path && edit?.key === key && edit.path === path;
  }

  /** Whether a save of this row runs; reads a signal. */
  isSaving(key: GridRowKey | null): boolean {
    return key !== null && this.saving().has(key);
  }

  /**
   * Starts the edit of the row's cell in the column, with the cell's value and the key `typed` into a box that takes
   * text; false for a column not edited in place, a checkbox, a row without a key, a row whose save runs, in a
   * select's list, and while a question is open.
   */
  start(row: GridRow, column: GridColumnDto, typed?: string): boolean {
    const key = keyOf(row);
    const kind = editKindOf(column);
    if (
      this.isAsking ||
      key === null ||
      !column.path ||
      kind === null ||
      kind === 'checkbox' ||
      this.options.isPicking() ||
      untracked(() => this.isSaving(key))
    ) {
      return false;
    }
    const takesTyped = typed !== undefined && TYPED_KINDS.has(kind);
    this.edit.set({
      key,
      path: column.path,
      value: valueAt(row, column.path),
      isSaving: false,
      messages: [],
      ...(takesTyped ? { typed } : {}),
    });
    return true;
  }

  /** What the editor holds now; nothing while the save runs. The typed key is spent. */
  update(value: unknown): void {
    const edit = untracked(this.edit);
    if (!edit || edit.isSaving || (Object.is(edit.value, value) && edit.typed === undefined)) {
      return;
    }
    this.edit.set({ key: edit.key, path: edit.path, value, isSaving: false, messages: edit.messages });
  }

  /**
   * Saves the edit's value and, once it is answered, moves as `move` says while the edit it saved is still the one held:
   * an edit started meanwhile keeps its cell. An unchanged value posts nothing and moves at once. A confirmed column
   * asks first: `Cancel`, or an answer that finds the edit ended or changed, posts nothing and keeps the edit held.
   * Resolves when the answer is taken.
   */
  async commit(move: GridCellMove): Promise<void> {
    const edit = untracked(this.current);
    if (!edit || edit.isSaving || this.isAsking) {
      return;
    }
    const row = this.rowOf(edit.key);
    const column = untracked(this.options.columns).find(c => c.path === edit.path);
    if (!row || !column) {
      this.cancel();
      return;
    }
    if (sameValue(edit.value, valueAt(row, edit.path))) {
      this.end(edit);
      this.move(move, row, edit.path);
      return;
    }
    if (column.confirmsEdit && !((await this.ask(column, valueAt(row, edit.path), edit.value)) && untracked(this.edit) === edit)) {
      return;
    }

    const saving: GridCellEdit = { key: edit.key, path: edit.path, value: edit.value, isSaving: true, messages: [] };
    this.edit.set(saving);
    const result = await this.save(row, edit.path, edit.value, true);
    if (result.kind === 'refused' && untracked(this.edit) === saving) {
      this.edit.set({ ...saving, isSaving: false, messages: result.messages });
      return;
    }
    const isHeld = untracked(this.edit) === saving;
    if (isHeld) {
      this.end(saving);
    }
    if (result.kind === 'committed') {
      if (isHeld) {
        this.move(move, result.row ?? row, edit.path);
      }
      this.options.reload();
    }
  }

  /**
   * Saves the other value of the row's editable checkbox at once, a confirmed column's once the user said `Save`;
   * nothing while a save of the row runs or a question is open.
   */
  async toggle(row: GridRow, column: GridColumnDto): Promise<void> {
    const key = keyOf(row);
    if (
      this.isAsking ||
      key === null ||
      !column.path ||
      editKindOf(column) !== 'checkbox' ||
      this.options.isPicking() ||
      untracked(() => this.isSaving(key))
    ) {
      return;
    }
    const before = valueAt(row, column.path);
    const after = before !== true;
    if (column.confirmsEdit && !(await this.ask(column, before, after))) {
      return;
    }
    const result = await this.save(row, column.path, after, false);
    if (result.kind === 'committed') {
      this.options.reload();
    }
  }

  /** Ends the edit with nothing posted. */
  cancel(): void {
    this.edit.set(null);
  }

  /** Asks the confirmed column's question of a change from `before` to `after`; holds `isAsking` while it is open. */
  private async ask(column: GridColumnDto, before: unknown, after: unknown): Promise<boolean> {
    this.isAsking = true;
    try {
      const clock = { locale: untracked(this.locale.current), zone: untracked(this.locale.zone) };
      return await this.confirmation.confirm(cellQuestionOf(column, before, after, clock, this.texts));
    } finally {
      this.isAsking = false;
    }
  }

  /** Ends `edit` when it is still the one held. */
  private end(edit: GridCellEdit): void {
    if (untracked(this.edit)?.key === edit.key && untracked(this.edit)?.path === edit.path) {
      this.edit.set(null);
    }
  }

  /** Where the active cell goes after a commit: the row below, or the edit of the row's next or previous editable cell. */
  private move(move: GridCellMove, row: GridRow, path: string): void {
    if (move === 'down') {
      this.options.moveActive(1);
    } else if (move === 'next' || move === 'previous') {
      const columns = untracked(this.options.columns);
      const at = columns.findIndex(column => column.path === path);
      const step = move === 'next' ? 1 : -1;
      for (let index = at + step; index >= 0 && index < columns.length; index += step) {
        if (this.start(row, columns[index])) {
          return;
        }
      }
    }
  }

  /**
   * Posts one value of the row: a committed answer is adopted while the grid shows what it showed at the post, a refusal
   * with messages at the path is answered when an editor `keepsRefusal` to show them, and any other failure is reported here.
   */
  private async save(row: GridRow, path: string, value: unknown, keepsRefusal: boolean): Promise<SaveResult> {
    const service = untracked(this.options.service);
    const id = keyOf(row);
    if (!service || id === null) {
      return { kind: 'failed' };
    }
    const ver = row['ver'];
    const body: GridCellRequest = { ...untracked(this.options.params), id, path, value, ...(typeof ver === 'number' ? { ver } : {}) };
    const isCurrent = this.options.capture();
    const operation = this.options.beginMutation();
    this.saving.update(keys => new Set(keys).add(id));
    try {
      const answer = await firstValueFrom(
        this.api.postTo<GridServiceEndpoints, typeof GRID_SAVE_CELL>(service, GRID_SAVE_CELL, body, shownByCaller()),
      );
      if (!isCurrent()) {
        return { kind: 'failed' };
      }
      this.adopt(answer, id, service, operation);
      return { kind: 'committed', row: isRow(answer.row) ? answer.row : null };
    } catch (error: unknown) {
      return isCurrent() ? this.fail(classifyMutationFailure(error), keepsRefusal ? path : null) : { kind: 'failed' };
    } finally {
      this.saving.update(keys => {
        const next = new Set(keys);
        next.delete(id);
        return next;
      });
      this.options.finishMutation(operation);
    }
  }

  /** A committed answer: the row adopted, the screen told, the commit's warnings shown. */
  private adopt(answer: GridCellDto, id: GridRowKey, service: string, operation: ScreenOperation | undefined): void {
    this.options.adoptRow(id, isRow(answer.row) ? answer.row : null);
    if (operation) {
      this.mutations?.publish(operation, { service, operation: 'save', selectedIds: [id] });
    }
    this.alert.warnings(answer.warnings, untracked(this.options.model)?.mutationTexts.committedWarningTitle);
  }

  /** A failure: a refusal at `path` is answered; a conflict shows its title and reloads; any other is reported. */
  private fail(failure: MutationFailure, path: string | null): SaveResult {
    const atPath = path === null ? [] : failure.messages.filter(message => message.path === path);
    if (failure.outcome === 'refused' && atPath.length > 0) {
      return { kind: 'refused', messages: atPath };
    }
    if (failure.outcome === 'conflict') {
      const title = untracked(this.options.model)?.mutationTexts.conflictTitle ?? refusalOf(failure, this.texts).title;
      this.alert.error(title, refusalDetailOf(failure));
      this.options.reload();
    } else {
      this.alert.error(refusalOf(failure, this.texts).title, refusalDetailOf(failure, failure.messages, '\n'));
    }
    return { kind: 'failed' };
  }

  private rowOf(key: GridRowKey): GridRow | undefined {
    return untracked(this.options.rows).find(row => keyOf(row) === key);
  }
}

/** Whether two values of a cell are the same, by structure. */
function sameValue(one: unknown, other: unknown): boolean {
  return JSON.stringify(one ?? null) === JSON.stringify(other ?? null);
}

function isRow(value: unknown): value is GridRow {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

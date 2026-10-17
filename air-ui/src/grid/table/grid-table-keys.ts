import { computed, linkedSignal, untracked } from '@angular/core';
import type { GridColumnDto } from '../../core/generated/contracts';
import { SELECTION_PATH } from '../column/grid-selection-column';
import { editKindOf } from '../grid-cell-edits';
import { type GridRow, keyOf } from '../grid-row';
import type { GridService } from '../grid.service';
import { STATUS_ROW } from './grid-table-tab-order';

/** What `Enter` presses in the active cell: a link or a button a cell draws. */
const CELL_ACTIONS = 'a[href], button:not([disabled]), [role="link"], [role="button"]';

/** The element of a cell's editor, whose keys are its control's. */
export const CELL_EDITOR = 'ui-grid-cell-editor';

/**
 * Where a key belongs to the control it was pressed on, not to the table: the head, whose sort, filter and menu
 * controls keep their Tab stops and no active cell reaches, a status row, and a cell's editor.
 */
const OWN_KEYS = `thead, ${STATUS_ROW}, ${CELL_EDITOR}`;

/** The two ways the active cell moves: from row to row, or along its row from column to column. */
export type TableAxis = 'row' | 'column';

/** What a key asks of the table; the table applies it to the grid's active row and its own active column. */
export type TableKeyAction =
  /** One row down or up, one column right or left. */
  | { readonly kind: 'move'; readonly axis: TableAxis; readonly step: 1 | -1 }
  /** What the body shows, less one row, down or up. */
  | { readonly kind: 'page'; readonly step: 1 | -1 }
  /** The first or the last row of the page, or the first or the last column of the row. */
  | { readonly kind: 'edge'; readonly axis: TableAxis; readonly edge: 'first' | 'last' }
  /** The active row's selection toggled, with `Shift` the range from the row toggled last. */
  | { readonly kind: 'select' }
  /**
   * The active row picked in a select, else the active cell edited when its column is editable, else the active cell's
   * link or button pressed, else the row opened.
   */
  | { readonly kind: 'activate' }
  /** The active cell edited when its column is editable, with the printable key `typed` that started it. */
  | { readonly kind: 'edit'; readonly typed?: string };

/**
 * What a key pressed on the table asks, by the WAI-ARIA grid pattern: the arrows move the active cell, `PageDown` and
 * `PageUp` move it by a page, `Home` and `End` take it to the row's first or last column, or with `isTable` — `Ctrl`
 * or `Cmd` held — to the first or last row, `Space` selects, `Enter` activates, and `F2` or a printable key without
 * `Ctrl`, `Cmd` or `Alt` — `isAlt` — edits; null for a key the table leaves to the browser.
 */
export function tableKeyAction(key: string, isTable: boolean, isAlt = false): TableKeyAction | null {
  switch (key) {
    case 'ArrowDown':
      return { kind: 'move', axis: 'row', step: 1 };
    case 'ArrowUp':
      return { kind: 'move', axis: 'row', step: -1 };
    case 'ArrowRight':
      return { kind: 'move', axis: 'column', step: 1 };
    case 'ArrowLeft':
      return { kind: 'move', axis: 'column', step: -1 };
    case 'PageDown':
      return { kind: 'page', step: 1 };
    case 'PageUp':
      return { kind: 'page', step: -1 };
    case 'Home':
      return { kind: 'edge', axis: isTable ? 'row' : 'column', edge: 'first' };
    case 'End':
      return { kind: 'edge', axis: isTable ? 'row' : 'column', edge: 'last' };
    case ' ':
      return { kind: 'select' };
    case 'Enter':
      return { kind: 'activate' };
    case 'F2':
      return { kind: 'edit' };
    default:
      return key.length === 1 && !isTable && !isAlt ? { kind: 'edit', typed: key } : null;
  }
}

/** What the keyboard is given of its table beside the grid. */
export interface TableKeyboardHost {
  /** The rows a page key moves by: what the body shows, less one. */
  readonly pageRows: () => number;
  /** The element of the active cell, or null while none is drawn. */
  readonly activeCell: () => HTMLElement | null;
  /** Runs `run` once the table has drawn what changed. */
  readonly afterRender: (run: () => void) => void;
}

/**
 * The table's keyboard: its active column — the active row is the grid's — and what a key or a click on a cell does to
 * the active cell. The selection column is worked by `Space`, never visited; a column move scrolls the cell into view.
 * `Enter`, `F2` and a printable key edit an editable active cell (`GridCellEdits`), a checkbox by its toggle; the keys
 * pressed in the cell's editor are the editor's.
 */
export class TableKeyboard {
  /** The column the user moved to, or null for the first one; an edit that starts takes its cell's column. */
  private readonly column = linkedSignal<string | null, number | null>({
    source: () => this.grid.edits.current()?.path ?? null,
    computation: (path, previous) => {
      const index = path === null ? -1 : untracked(this.grid.tableColumns).findIndex(column => column.path === path);
      return index >= 0 ? index : (previous?.value ?? null);
    },
  });

  /** The first column the keyboard reaches: the selection column is worked by `Space`, not visited. */
  private readonly firstColumn = computed(() =>
    Math.max(
      0,
      this.grid.tableColumns().findIndex(column => column.path !== SELECTION_PATH),
    ),
  );

  /** The active cell's column, held to the drawn columns; -1 while there is none. */
  readonly activeColumn = computed(() => {
    const chosen = this.column();
    return this.grid.tableColumns().length === 0 ? -1 : this.held(chosen ?? this.firstColumn());
  });

  constructor(
    private readonly grid: GridService,
    private readonly host: TableKeyboardHost,
  ) {}

  /**
   * Does what a key pressed on the table asks and keeps it from the browser; nothing on a page without rows, nor for a
   * key a control of the head or of a status row answers.
   */
  onKeyDown(event: KeyboardEvent): void {
    if (event.target instanceof Element && event.target.closest(OWN_KEYS) !== null) {
      return;
    }
    const action = tableKeyAction(event.key, event.ctrlKey || event.metaKey, event.altKey);
    if (action && this.grid.rows().length > 0 && (action.kind !== 'edit' || this.editableCell() !== null)) {
      this.apply(action, event.shiftKey);
      event.preventDefault();
    }
  }

  /** A click on a cell makes its column the active one, as the arrows would; the selection column stays unvisited. */
  activateColumn(column: number): void {
    if (column >= this.firstColumn()) {
      this.column.set(column);
    }
  }

  private apply(action: TableKeyAction, shift: boolean): void {
    switch (action.kind) {
      case 'move':
        this.moveBy(action.axis, action.step);
        break;
      case 'page':
        this.grid.moveActive(action.step * this.host.pageRows());
        break;
      case 'edge':
        this.moveToEdge(action.axis, action.edge);
        break;
      case 'select':
        this.toggleActiveRow(shift);
        break;
      case 'activate':
        this.activateActiveCell();
        break;
      case 'edit':
        this.editActiveCell(action.typed);
        break;
    }
  }

  private moveBy(axis: TableAxis, step: 1 | -1): void {
    if (axis === 'row') {
      this.grid.moveActive(step);
    } else {
      this.moveColumnTo(this.activeColumn() + step);
    }
  }

  private moveToEdge(axis: TableAxis, edge: 'first' | 'last'): void {
    if (axis === 'row') {
      this.grid.setActive(edge === 'first' ? 0 : this.grid.rows().length - 1);
    } else {
      this.moveColumnTo(edge === 'first' ? this.firstColumn() : this.grid.tableColumns().length - 1);
    }
  }

  /** Makes a column of the active row the active cell — the first row when none is active — and scrolls it into view. */
  private moveColumnTo(column: number): void {
    if (this.grid.activeRowIndex() < 0) {
      this.grid.setActive(0);
    }
    this.column.set(this.held(column));
    this.host.afterRender(() => this.host.activeCell()?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' }));
  }

  /** A column held to the columns the keyboard reaches: from the first that is not the selection column to the last drawn. */
  private held(column: number): number {
    return Math.min(Math.max(column, this.firstColumn()), this.grid.tableColumns().length - 1);
  }

  /** Toggles the active row's selection, with `shift` the range from the row toggled last; a grid that selects nothing ignores it. */
  private toggleActiveRow(shift: boolean): void {
    const row = this.grid.activeRow();
    if (row && this.grid.isSelectable()) {
      this.grid.selection.toggle(row, this.grid.rows(), shift);
    }
  }

  /**
   * A select picks the active row, else an editable active cell is edited, else the cell's own link or button is
   * pressed; a row that none takes opens.
   */
  private activateActiveCell(): void {
    const active = this.grid.activeRowIndex();
    if (active < 0) {
      return;
    }
    const handled = this.grid.pickActive() || this.editActiveCell() || this.pressActiveCell();
    const row = this.grid.rows()[active];
    if (!handled && row) {
      this.grid.openForm(keyOf(row));
    }
  }

  /** The active cell of a body row when its column is editable, with its row; null otherwise. */
  private editableCell(): { readonly row: GridRow; readonly column: GridColumnDto } | null {
    const row = this.grid.activeRow();
    const column = this.grid.tableColumns()[this.activeColumn()];
    return row && editKindOf(column) !== null ? { row, column } : null;
  }

  /**
   * Edits the active cell when its column is editable: a checkbox toggles and saves, any other cell opens its editor,
   * with `typed` in it; false when the cell is not editable, or its row's save runs.
   */
  private editActiveCell(typed?: string): boolean {
    const cell = this.editableCell();
    if (!cell) {
      return false;
    }
    if (editKindOf(cell.column) === 'checkbox') {
      if (typed === undefined) {
        void this.grid.edits.toggle(cell.row, cell.column);
      }
      return true;
    }
    this.grid.edits.start(cell.row, cell.column, typed);
    return true;
  }

  /** Presses the link or the button the active cell draws, as a click on it would; false when it draws none. */
  private pressActiveCell(): boolean {
    const action = this.host.activeCell()?.querySelector<HTMLElement>(CELL_ACTIONS);
    action?.click();
    return !!action;
  }
}

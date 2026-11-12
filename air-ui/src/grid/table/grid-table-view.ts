import { computed, type Signal } from '@angular/core';
import type { GridColumnDto, SortDirection } from '../../core/generated/contracts';
import type { GridCellContext } from '../cell/grid-cell-context';
import { tableMeasure } from '../column/grid-column-fit';
import { effectiveWidthOf } from '../column/grid-column-width';
import type { GridRow } from '../grid-row';
import type { GridService } from '../grid.service';
import { type GridHeaderRowCell, groupStartPaths, spanHeaderRow } from '../header/grid-header-rows';
import { leftOffsetsOf, rightOffsetsOf, stickyEdgeOf, stickyValueOf } from './grid-sticky-columns';
import { headerCells, headerRowCell, rowCells, sameColumnsLessWidth } from './grid-table-cells';
import {
  BODY_CELL,
  columnClassesOf,
  editingCellClassOf,
  headerRowClassesOf,
  PINNED_BOTTOM_CELL,
  PINNED_TOP_CELL,
} from './grid-table-classes';
import { contentWidthOf, sharedWidthOf } from './grid-table-measure';
import {
  type BodyRow,
  bodyRowOf,
  type BodyRowSource,
  bodyRowStatesOf,
  cellElementIdOf,
  rowCellClassesOf,
  type TableRow,
} from './grid-table-rows';

/** The height of a header row, the `h-6` of its cells, in rem: what a row of the head below it sticks under. */
export const HEADER_ROW_REM = 1.5;
/** The height of the column header and of a pinned row, the `h-9` of their cells, in rem. */
export const ROW_REM = 2.25;
/** What a body row is taken to be until it is measured: the `h-9` of its cells at the root font size, 36 pixels. */
export const ESTIMATED_ROW_HEIGHT = ROW_REM * 16;

/** What a header says of its column's sort, as the grid pattern names it. */
const ARIA_SORT = { asc: 'ascending', desc: 'descending' } as const satisfies Record<SortDirection, string>;

/** Whether the event fell on a link or a button the cell it reached draws, or inside one. */
export function isOwnControl(event: MouseEvent): boolean {
  const control = event.target instanceof Element ? event.target.closest('a, button') : null;
  return control !== null && event.currentTarget instanceof Element && event.currentTarget.contains(control);
}

export interface GridTableViewOptions {
  readonly id: string;
  readonly grid: GridService;
  readonly startIndex: Signal<number>;
  readonly endIndex: Signal<number>;
  readonly activeColumn: Signal<number>;
}

/**
 * Derives the reactive column geometry, sticky offsets, header rows, body rows, pinned summary rows, ARIA attributes,
 * and cell class sets for `GridTableComponent`.
 */
export class GridTableView {
  private readonly grid: GridService;
  private readonly id: string;
  private readonly headerRowCells = new WeakMap<GridHeaderRowCell, GridCellContext>();
  private readonly columnHeaderCells = new WeakMap<GridColumnDto, GridCellContext>();

  readonly cellColumns: Signal<readonly GridColumnDto[]>;
  readonly declaredCellColumns: Signal<readonly GridColumnDto[]>;
  readonly stretches: Signal<boolean>;
  readonly shares: Signal<boolean>;
  readonly hasFiller: Signal<boolean>;
  readonly fullSpan: Signal<number>;
  readonly visibleRows: Signal<readonly GridRow[]>;
  readonly headerCells: Signal<readonly GridCellContext[]>;
  private readonly bodyCells: Signal<(index: number) => readonly GridCellContext[]>;
  readonly headerRows: Signal<readonly GridHeaderRowCell[][]>;
  private readonly groupStarts: Signal<ReadonlySet<number>>;
  readonly topRows: Signal<readonly TableRow[]>;
  readonly bottomRows: Signal<readonly TableRow[]>;
  private readonly headRows: Signal<number>;
  readonly bodyRows: Signal<readonly BodyRow[]>;
  readonly bodyRowStates: ReturnType<typeof computed<ReturnType<typeof bodyRowStatesOf>>>;
  readonly ariaRowCount: Signal<number>;
  readonly activeCellId: Signal<string | null>;
  readonly ariaSorts: Signal<readonly (string | null)[]>;
  readonly widths: Signal<readonly number[]>;
  readonly tableWidth: Signal<number>;
  readonly colWidths: Signal<readonly (string | null)[]>;
  private readonly leftOffsets: Signal<readonly number[]>;
  private readonly rightOffsets: Signal<readonly number[]>;
  readonly stickyStart: Signal<number>;
  readonly stickyEnd: Signal<number>;
  readonly stickyLefts: Signal<readonly (string | null)[]>;
  readonly stickyRights: Signal<readonly (string | null)[]>;
  readonly columnClasses: Signal<readonly string[]>;
  readonly headerClasses: Signal<readonly string[]>;
  readonly bodyCellClasses: Signal<string[][]>;
  readonly topCellClasses: Signal<string[][]>;
  readonly bottomCellClasses: Signal<string[][]>;
  readonly editingCellClass: Signal<string>;
  readonly headerRowClasses: Signal<string[][]>;

  constructor(options: GridTableViewOptions) {
    const { grid, id, startIndex, endIndex, activeColumn } = options;
    this.grid = grid;
    this.id = id;

    this.cellColumns = computed(() => grid.tableColumns(), { equal: sameColumnsLessWidth });
    this.declaredCellColumns = computed(() => grid.allColumns(), { equal: sameColumnsLessWidth });
    this.stretches = computed(() => grid.isPicking() && grid.tableColumns().length === 1);
    this.shares = computed(() => grid.isPicking() && !this.stretches());
    this.hasFiller = computed(() => !this.stretches() && !this.shares());
    this.fullSpan = computed(() => grid.tableColumns().length + (this.hasFiller() ? 1 : 0));
    this.visibleRows = computed(() => grid.rows().slice(startIndex(), endIndex()));
    this.headerCells = computed<readonly GridCellContext[]>(() => headerCells(grid.tableColumns(), this.columnHeaderCells));

    this.bodyCells = computed(() => {
      const rows = grid.rows();
      const columns = this.cellColumns();
      const cells = new Map<GridRow, readonly GridCellContext[]>();
      return (index: number): readonly GridCellContext[] => {
        const row = rows[index];
        let built = cells.get(row);
        if (!built) {
          built = rowCells(row, columns, index);
          cells.set(row, built);
        }
        return built;
      };
    });

    this.headerRows = computed<readonly GridHeaderRowCell[][]>(() => {
      const declared = this.declaredCellColumns();
      const drawn = this.cellColumns();
      return (grid.model()?.headers ?? []).map(row => spanHeaderRow(row, declared, drawn));
    });

    this.groupStarts = computed<ReadonlySet<number>>(() => {
      const drawn = this.cellColumns();
      const paths = groupStartPaths(grid.model()?.headers ?? [], this.declaredCellColumns(), drawn);
      return new Set(drawn.map((column, index) => (column.path && paths.has(column.path) ? index : -1)).filter(i => i >= 0));
    });

    this.topRows = computed(() => this.pinnedRows(grid.topRows(), 'top'));
    this.bottomRows = computed(() => this.pinnedRows(grid.bottomRows(), 'bottom'));
    this.headRows = computed(() => this.headerRows().length + 1 + this.topRows().length);

    this.bodyRows = computed<readonly BodyRow[]>(() => {
      const cellsAt = this.bodyCells();
      const start = startIndex();
      const source: BodyRowSource = {
        tableId: id,
        ariaStart: this.headRows() + (grid.page() - 1) * grid.pageSize() + 1,
        cellClass: this.stretches() ? `${BODY_CELL} border-r-0` : BODY_CELL,
        isChanged: (row, path) => grid.isChanged(row, path),
      };
      return this.visibleRows().map((row, offset) => bodyRowOf(row, start + offset, cellsAt(start + offset), source));
    });

    this.bodyRowStates = computed(() =>
      bodyRowStatesOf(this.bodyRows(), grid.activeRowIndex(), grid.isSelectable(), row => grid.selection.isSelected(row)),
    );

    this.ariaRowCount = computed(() =>
      grid.isCountPending() || grid.isCountFailed() ? -1 : this.headRows() + grid.total() + this.bottomRows().length,
    );

    this.activeCellId = computed(() => {
      const index = grid.activeRowIndex();
      const column = activeColumn();
      return index >= 0 && index < grid.rows().length && column >= 0 ? cellElementIdOf(id, index, column) : null;
    });

    this.ariaSorts = computed(() => {
      const first = grid.sort()[0];
      return grid.tableColumns().map(column => (first && column.path === first.field ? ARIA_SORT[first.direction] : null));
    });

    this.widths = computed(() => grid.tableColumns().map(column => effectiveWidthOf(column)));
    this.tableWidth = computed(() => this.widths().reduce((sum, width) => sum + width, 0));

    this.colWidths = computed<readonly (string | null)[]>(() => {
      if (this.stretches()) {
        return this.widths().map(() => null);
      }
      if (this.shares()) {
        const total = this.tableWidth();
        return this.widths().map(width => `${(width * 100) / total}%`);
      }
      return this.widths().map(width => `${width}px`);
    });

    this.leftOffsets = computed(() => leftOffsetsOf(grid.tableColumns(), this.widths()));
    this.rightOffsets = computed(() => rightOffsetsOf(grid.tableColumns(), this.widths()));
    this.stickyStart = computed(() => stickyEdgeOf(this.leftOffsets(), this.widths()));
    this.stickyEnd = computed(() => stickyEdgeOf(this.rightOffsets(), this.widths()));
    this.stickyLefts = computed(() => this.leftOffsets().map(offset => stickyValueOf(offset)));
    this.stickyRights = computed(() => this.rightOffsets().map(offset => stickyValueOf(offset)));

    this.columnClasses = computed(() => columnClassesOf(grid.tableColumns(), this.leftOffsets(), this.rightOffsets(), this.groupStarts()));

    this.headerClasses = computed(() => {
      const stretches = this.stretches();
      const picking = grid.isPicking();
      return this.columnClasses().map(classes => {
        let result = classes;
        if (stretches) result += ' border-r-0';
        if (picking) result += ' h-7!';
        return result;
      });
    });

    this.bodyCellClasses = computed(() => {
      const columns = this.columnClasses();
      const active = grid.activeRowIndex();
      return this.bodyRows().map(item => rowCellClassesOf(item, columns, item.index === active ? activeColumn() : -1, item.marks));
    });
    this.topCellClasses = computed(() => this.pinnedCellClasses(this.topRows()));
    this.bottomCellClasses = computed(() => this.pinnedCellClasses(this.bottomRows()));
    this.editingCellClass = computed(() => editingCellClassOf((grid.edits.current()?.messages.length ?? 0) > 0));
    this.headerRowClasses = computed(() => headerRowClassesOf(this.headerRows(), this.columnClasses(), this.groupStarts()));
  }

  headerRowCell(item: GridHeaderRowCell): GridCellContext {
    let cell = this.headerRowCells.get(item);
    if (!cell) {
      cell = headerRowCell(item);
      this.headerRowCells.set(item, cell);
    }
    return cell;
  }

  headTop(headerRows: number, rows: number): string {
    return `${headerRows * HEADER_ROW_REM + rows * ROW_REM}rem`;
  }

  bottomRowIndexOf(index: number): number | null {
    const count = this.ariaRowCount();
    return count < 0 ? null : count - this.bottomRows().length + index + 1;
  }

  widthDrawing(column: GridColumnDto, drawn: number, viewportWidth: number): number {
    if (!this.shares()) {
      return drawn;
    }
    const own = this.widths()[this.grid.tableColumns().indexOf(column)] ?? 0;
    return sharedWidthOf(drawn, this.tableWidth() - own, viewportWidth);
  }

  resizeColumn(column: GridColumnDto, width: number, viewportWidth: number): void {
    if (column.path) {
      this.grid.columnState.setWidth(column.path, this.widthDrawing(column, width, viewportWidth));
    }
  }

  toggleFit(column: GridColumnDto, renderedRows: readonly HTMLTableRowElement[], viewportWidth: number): void {
    if (column.path) {
      this.grid.columnState.toggleFit(column.path, () =>
        this.widthDrawing(
          column,
          contentWidthOf(column, this.grid.tableColumns().indexOf(column), renderedRows, tableMeasure()),
          viewportWidth,
        ),
      );
    }
  }

  private pinnedCellClasses(rows: readonly TableRow[]): string[][] {
    const columns = this.columnClasses();
    return rows.map(item => rowCellClassesOf(item, columns, -1));
  }

  private pinnedRows(rows: readonly GridRow[], pinned: 'top' | 'bottom'): readonly TableRow[] {
    const columns = this.cellColumns();
    const cellClass = pinned === 'top' ? PINNED_TOP_CELL : PINNED_BOTTOM_CELL;
    return rows.map(row => ({ row, cells: rowCells(row, columns, -1, pinned), cellClass }));
  }
}

import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  untracked,
  viewChild,
  viewChildren,
} from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import { BtnComponent } from '../../btn/btn.component';
import type { GridColumnDto, SortDirection } from '../../core/generated/contracts';
import { problemOf } from '../../core/problem';
import { TextsService } from '../../core/texts.service';
import { type GridCellContext } from '../cell/grid-cell-context';
import { GridCellEditorComponent } from '../cell/grid-cell-editor.component';
import { GridCellOutletComponent } from '../cell/grid-cell-outlet.component';
import { TABLE_TEXT, tableMeasure } from '../column/grid-column-fit';
import { effectiveWidthOf, MAX_COLUMN_WIDTH, minWidthOf } from '../column/grid-column-width';
import { editKindOf } from '../grid-cell-edits';
import { type GridRow, type GridRowKey, keyOf } from '../grid-row';
import { GridService, type GridTableIds } from '../grid.service';
import { type GridHeaderRowCell, groupStartPaths, spanHeaderRow } from '../header/grid-header-rows';
import { leftOffsetsOf, rightOffsetsOf, stickyEdgeOf, stickyValueOf } from './grid-sticky-columns';
import { headerCells, headerRowCell, rowCells, sameColumnsLessWidth } from './grid-table-cells';
import {
  BODY_CELL,
  columnClassesOf,
  editingCellClassOf,
  FILLER,
  HEADER_CELL,
  HEADER_ROW_CELL,
  headerRowClassesOf,
  PINNED_BOTTOM_CELL,
  PINNED_ROW,
  PINNED_TOP_CELL,
  ROW,
  TABLE_RING,
} from './grid-table-classes';
import { TableKeyboard } from './grid-table-keys';
import { contentWidthOf, fitColumnsAtOpening, fitSampleOf, pinnedInset, sharedWidthOf } from './grid-table-measure';
import {
  type BodyRow,
  bodyRowOf,
  type BodyRowSource,
  bodyRowStatesOf,
  cellElementIdOf,
  rowCellClassesOf,
  type TableRow,
} from './grid-table-rows';
import { keepCellControlsOutOfTabOrder } from './grid-table-tab-order';
import { ResizableDirective } from './resizable.directive';
import { PIXEL_TOLERANCE, VirtualScrollDriver } from './virtual-scroll-driver';

/** The height of a header row, the `h-6` of its cells, in rem: what a row of the head below it sticks under. */
const HEADER_ROW_REM = 1.5;
/** The height of the column header and of a pinned row, the `h-9` of their cells, in rem. */
const ROW_REM = 2.25;
/** What a body row is taken to be until it is measured: the `h-9` of its cells at the root font size, 36 pixels. */
const ESTIMATED_ROW_HEIGHT = ROW_REM * 16;

/** What a header says of its column's sort, as the grid pattern names it. */
const ARIA_SORT = { asc: 'ascending', desc: 'descending' } as const satisfies Record<SortDirection, string>;

/** Whether the event fell on a link or a button the cell it reached draws, or inside one. */
function isOwnControl(event: MouseEvent): boolean {
  const control = event.target instanceof Element ? event.target.closest('a, button') : null;
  return control !== null && event.currentTarget instanceof Element && event.currentTarget.contains(control);
}

/**
 * The virtual table of a grid: the rows near the viewport are in
 * the DOM and spacers stand for the rest; every rendered row is measured and the layout
 * engine keeps the heights, so the scroll position is anchored to a row across
 * measurements; the header row stays visible; sticky columns keep their edge; a column's
 * edge is dragged through the resize directive and the width goes to the grid.
 *
 * The table follows the WAI-ARIA grid pattern: the `<table>` is the grid and the one Tab stop,
 * its rows are counted as the whole result counts them, and it names its active cell — the
 * grid's active row, the table's active column — with `aria-activedescendant`. The arrows,
 * `Home`, `End` and their `Ctrl` forms move the cell, `Enter` edits an editable cell, else
 * presses the cell's link or button or opens the row, and every control a cell draws, at once
 * or later, is taken out of the Tab order. The cell being edited draws its editor in place of
 * its outlet; a click on an editable checkbox toggles and saves it. The header of the first
 * sorted column says its sort; a grid that selects says it selects many rows. A body cell a
 * save changed draws the mark in its corner and is described by the table's one hidden text `Changed`. When the first page is in, a
 * column the server left without a width is fitted once to its label and the first fifty
 * rows, measured with a canvas. The rows the page pins are drawn
 * outside the virtual sequence: the top rows in the `thead` under the column header, the
 * bottom rows in a `tfoot`. The head's rows stick one by one, each at the height of the rows
 * above it, so the head stays in the flow the layout engine counts and the pins can give way
 * on a narrow window: below `lg` every sticky column but the selection column scrolls with
 * the rest, below `md` the pinned rows leave the edges; the column header stays at every width.
 */
@Component({
  selector: 'ui-grid-table',
  imports: [BtnComponent, GridCellEditorComponent, GridCellOutletComponent, NgTemplateOutlet, ResizableDirective],
  host: {
    class: 'flex flex-col flex-1 w-full h-full min-w-0 min-h-0 overflow-hidden',
  },
  template: `
    <div
      #viewport
      class="flex-1 min-h-0 min-w-0 overflow-auto bg-surface ${TABLE_RING}"
      style="overflow-anchor: none"
      [style.scroll-padding-left.px]="stickyStart()"
      [style.scroll-padding-right.px]="stickyEnd()"
    >
      <!--
        Full width, the columns' widths fixed and the filler taking the rest, or in a picker's list the columns sharing it
        by their widths; the room under the last row is the floating pager's
      -->
      <table
        #table
        role="grid"
        [id]="id"
        tabindex="0"
        class="w-full table-fixed border-separate border-spacing-0 ${TABLE_TEXT} outline-none"
        [class.mb-10]="paged()"
        [style.min-width.px]="stretches() ? null : tableWidth()"
        [attr.aria-rowcount]="ariaRowCount()"
        [attr.aria-colcount]="tableColumns().length"
        [attr.aria-multiselectable]="grid.isSelectable() ? 'true' : null"
        [attr.aria-activedescendant]="activeCellId()"
        (keydown)="keyboard.onKeyDown($event)"
      >
        <colgroup>
          @for (column of tableColumns(); track column.path; let index = $index) {
            <col [style.width]="colWidths()[index]" />
          }
          @if (hasFiller()) {
            <col />
          }
        </colgroup>

        <thead>
          @for (row of headerRows(); track $index; let rowIndex = $index) {
            <tr class="sticky z-20" [style.top]="headTop(rowIndex, 0)" [attr.aria-rowindex]="rowIndex + 1">
              @for (item of row; track $index; let cellIndex = $index) {
                <th
                  ui-grid-cell-outlet
                  scope="colgroup"
                  class="${HEADER_ROW_CELL}"
                  [class]="headerRowClasses()[rowIndex][cellIndex]"
                  [style.--sticky-left]="item.isSticky ? stickyLefts()[item.index] : null"
                  [attr.colspan]="item.span"
                  [cell]="headerRowCell(item)"
                  [model]="item.cell.model"
                  fallback="GridHeaderLabel"
                ></th>
              }
              @if (hasFiller()) {
                <th class="${HEADER_ROW_CELL} border-r-0" aria-hidden="true"></th>
              }
            </tr>
          }
          <tr class="sticky z-20" [style.top]="headTop(headerRows().length, 0)" [attr.aria-rowindex]="headerRows().length + 1">
            @for (header of headerCells(); let index = $index; track header.column.path) {
              <th
                ui-grid-cell-outlet
                scope="col"
                class="${HEADER_CELL}"
                [class]="headerClasses()[index]"
                [style.--sticky-left]="stickyLefts()[index]"
                [style.--sticky-right]="stickyRights()[index]"
                [attr.aria-sort]="ariaSorts()[index]"
                [cell]="header"
                [model]="header.column.header"
                fallback="GridHeaderDefault"
                [uiResizable]="header.column.isResizable ? 'right' : 'none'"
                [uiResizableMin]="minWidthOf(header.column)"
                [uiResizableMax]="shares() ? maxWidth : (header.column.maxWidth ?? maxWidth)"
                (uiResizableResized)="resizeColumn(header.column, $event)"
                (uiResizableFit)="toggleFit(header.column)"
              ></th>
            }
            @if (hasFiller()) {
              <th class="${HEADER_CELL} border-r-0" [class.h-7!]="grid.isPicking()" aria-hidden="true"></th>
            }
          </tr>
          <!-- The pinned top rows: under the column header and sticky with it from md up, scrolling away below -->
          @for (item of topRows(); track $index; let pinnedIndex = $index) {
            <tr
              role="row"
              class="${PINNED_ROW} md:sticky z-20"
              [attr.aria-rowindex]="headerRows().length + 2 + pinnedIndex"
              [attr.aria-label]="texts.of('PinnedRow')"
              [style.top]="headTop(headerRows().length, pinnedIndex + 1)"
            >
              <ng-container
                [ngTemplateOutlet]="rowCellTemplate"
                [ngTemplateOutletContext]="{ $implicit: item, classes: topCellClasses()[pinnedIndex] }"
              />
              @if (hasFiller()) {
                <td class="${FILLER} border-b" aria-hidden="true"></td>
              }
            </tr>
          }
        </thead>

        <tbody>
          <tr aria-hidden="true">
            <td class="p-0 border-0" [attr.colspan]="fullSpan()" [style.height.px]="topSpacer()"></td>
          </tr>

          @for (item of bodyRows(); track trackRow(item.index, item.row); let offset = $index) {
            <tr
              #virtualRow
              role="row"
              class="${ROW}"
              [id]="item.id"
              [class]="bodyRowStates()[offset].classes"
              [attr.data-row-index]="item.index"
              [attr.aria-rowindex]="item.ariaRowIndex"
              [attr.aria-selected]="bodyRowStates()[offset].ariaSelected"
              (click)="activateRow(item.index)"
            >
              <ng-container
                [ngTemplateOutlet]="rowCellTemplate"
                [ngTemplateOutletContext]="{ $implicit: item, classes: bodyCellClasses()[offset] }"
              />
              @if (hasFiller()) {
                <td class="${FILLER}" aria-hidden="true"></td>
              }
            </tr>
          }

          <!-- A picker's list whose text is too short says the least length; a failed page offers to ask it again; an empty one says so -->
          @if (grid.shortOf(); as least) {
            <tr role="row" data-grid-state="short">
              <td role="gridcell" class="px-3 py-2 text-muted" [attr.colspan]="fullSpan()">
                <span role="status">{{ texts.of('TypeAtLeast', least) }}</span>
              </td>
            </tr>
          } @else if (grid.error() !== undefined) {
            <tr role="row" data-grid-state="error">
              <td role="gridcell" class="px-3 py-2 text-warning" [attr.colspan]="fullSpan()">
                <span role="alert" class="inline-flex items-center gap-3">
                  {{ failure() }}
                  <ui-btn size="small" [label]="texts.of('Retry')" (click)="grid.retry()" />
                </span>
              </td>
            </tr>
          } @else if (rows().length === 0 && grid.data() !== null && !grid.isLoading()) {
            <tr role="row" data-grid-state="empty">
              <td role="gridcell" class="px-3 py-2 text-muted" [attr.colspan]="fullSpan()">
                {{ texts.of('NoRows') }}
              </td>
            </tr>
          }

          <tr aria-hidden="true">
            <td
              class="p-0 border-line"
              [class.border-b]="bottomRows().length === 0"
              [attr.colspan]="fullSpan()"
              [style.height.px]="bottomSpacer()"
            ></td>
          </tr>
        </tbody>

        @if (bottomRows().length > 0) {
          <tfoot #pinnedBottom class="md:sticky bottom-0 z-20">
            @for (item of bottomRows(); track $index; let bottomIndex = $index) {
              <tr
                role="row"
                class="${PINNED_ROW}"
                [attr.aria-label]="texts.of('PinnedRow')"
                [attr.aria-rowindex]="bottomRowIndexOf(bottomIndex)"
              >
                <ng-container
                  [ngTemplateOutlet]="rowCellTemplate"
                  [ngTemplateOutletContext]="{ $implicit: item, classes: bottomCellClasses()[bottomIndex] }"
                />
                @if (hasFiller()) {
                  <td class="${FILLER} border-t" aria-hidden="true"></td>
                }
              </tr>
            }
          </tfoot>
        }
      </table>
    </div>

    <!-- What a screen reader says of a cell a save changed, described by it -->
    <span [id]="changedTextId" hidden>{{ texts.of('Changed') }}</span>

    <!-- The cells of a body or pinned row, with the row's class sets by column; the cell being edited draws its editor -->
    <ng-template #rowCellTemplate let-item let-classes="classes">
      @for (cell of item.cells; let index = $index; track cell.column.path) {
        @if (isEdited(cell)) {
          <td
            role="gridcell"
            [class]="classes[index] + ' ' + editingCellClass()"
            [attr.id]="item.cellIds?.[index]"
            [style.--sticky-left]="stickyLefts()[index]"
            [style.--sticky-right]="stickyRights()[index]"
          >
            <ui-grid-cell-editor [cell]="cell" />
          </td>
        } @else {
          <td
            ui-grid-cell-outlet
            role="gridcell"
            [class]="classes[index]"
            [attr.id]="item.cellIds?.[index]"
            [attr.aria-describedby]="item.marks?.[index] ? changedTextId : null"
            (click)="clickCell(cell, index)"
            (dblclick)="editCell(cell, $event)"
            (mousedown)="keepWordsUnselected($event, cell)"
            [style.--sticky-left]="stickyLefts()[index]"
            [style.--sticky-right]="stickyRights()[index]"
            [cell]="cell"
            [model]="cell.column.cell"
            fallback="GridCellText"
          ></td>
        }
      }
    </ng-template>
  `,
})
export class GridTableComponent {
  private static next = 0;

  protected readonly grid = inject(GridService);
  protected readonly texts = inject(TextsService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;

  /** Whether the floating pager stands over the table's bottom edge, so the last row keeps clear of it. */
  readonly paged = input(false);

  private readonly viewport = viewChild<ElementRef<HTMLDivElement>>('viewport');
  private readonly table = viewChild.required<ElementRef<HTMLTableElement>>('table');
  private readonly renderedRows = viewChildren<ElementRef<HTMLTableRowElement>>('virtualRow');
  private readonly pinnedBottom = viewChild<ElementRef<HTMLTableSectionElement>>('pinnedBottom');

  /** The height of the pinned bottom rows, what floats over the table's bottom edge sits above. */
  private readonly _bottomInset = signal(0);
  readonly bottomInset = this._bottomInset.asReadonly();
  protected readonly rows = this.grid.rows;
  /** What the failed page says: the problem's title, or the platform's text for a failed request. */
  protected readonly failure = computed(() => problemOf(this.grid.error())?.title ?? this.texts.of('RequestFailed'));
  protected readonly tableColumns = this.grid.tableColumns;

  /** The drawn columns as a cell needs them: replaced when a column enters, leaves or changes in anything but its width. */
  private readonly cellColumns = computed(() => this.tableColumns(), { equal: sameColumnsLessWidth });

  /** Every declared column, hidden ones too, held the same way: what the header rows span. */
  private readonly declaredCellColumns = computed(() => this.grid.allColumns(), { equal: sameColumnsLessWidth });

  /** The virtual window and the scrolling behind it; the component draws what it says and hands it the DOM. */
  private readonly scroll = new VirtualScrollDriver(
    {
      viewport: () => this.viewportElement(),
      renderedRows: () => this.renderedRows().map(row => row.nativeElement),
      rowCount: () => this.rows().length,
      afterRender: run => afterNextRender(run, { injector: this.injector }),
      viewportMeasured: () => this.measurePinnedBottom(),
    },
    ESTIMATED_ROW_HEIGHT,
  );
  protected readonly startIndex = this.scroll.startIndex;
  protected readonly endIndex = this.scroll.endIndex;
  protected readonly topSpacer = this.scroll.topSpacer;
  protected readonly bottomSpacer = this.scroll.bottomSpacer;

  /** In a select's or a picker's list of one column that column takes the whole width and draws no right line. */
  protected readonly stretches = computed(() => this.grid.isPicking() && this.tableColumns().length === 1);

  /**
   * In a select's or a picker's list of more than one column the columns share the table by their widths, each the share its width
   * is of the columns' widths together, and no filler takes the rest.
   */
  protected readonly shares = computed(() => this.grid.isPicking() && !this.stretches());

  /** Whether the table draws the filler column and its cells: unless the one column stretches or the columns share. */
  protected readonly hasFiller = computed(() => !this.stretches() && !this.shares());

  /** The span of a cell across the whole row: every column, and the filler where the table draws it. */
  protected readonly fullSpan = computed(() => this.tableColumns().length + (this.hasFiller() ? 1 : 0));

  protected readonly visibleRows = computed(() => this.rows().slice(this.startIndex(), this.endIndex()));

  /**
   * The header cells, one object per column that stays the same while the column's object stands, so an outlet is fed
   * again only when its own column changed: a header reads the live column, its width too.
   */
  protected readonly headerCells = computed<readonly GridCellContext[]>(() => headerCells(this.tableColumns(), this.columnHeaderCells));

  /**
   * A body row's cells by its index on the page, one object per row and column for the same reason: kept while the
   * window moves and while a width changes, built again when the rows change (a multi-pick replaces them, so an index
   * moves) or a column changes in anything but its width.
   */
  private readonly bodyCells = computed(() => {
    const rows = this.rows();
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

  /**
   * The rendered rows with their cells and what each says of itself: its ids, its place in the whole result — after
   * the head, the pages before this one and the rows above it — and whether a save changed each of its cells. A body
   * cell draws no right line when the one column stretches.
   */
  protected readonly bodyRows = computed<readonly BodyRow[]>(() => {
    const cellsAt = this.bodyCells();
    const start = this.startIndex();
    const source: BodyRowSource = {
      tableId: this.id,
      ariaStart: this.headRows() + (this.grid.page() - 1) * this.grid.pageSize() + 1,
      cellClass: this.stretches() ? `${BODY_CELL} border-r-0` : BODY_CELL,
      isChanged: (row, path) => this.grid.isChanged(row, path),
    };
    return this.visibleRows().map((row, offset) => bodyRowOf(row, start + offset, cellsAt(start + offset), source));
  });

  /** The rows' grounds through the grid roles and their `aria-selected`, by row of the window. */
  protected readonly bodyRowStates = computed(() =>
    bodyRowStatesOf(this.bodyRows(), this.grid.activeRowIndex(), this.grid.isSelectable(), row => this.grid.selection.isSelected(row)),
  );

  /** The header rows above the columns, each laid over the columns the table draws. */
  protected readonly headerRows = computed<readonly GridHeaderRowCell[][]>(() => {
    const declared = this.declaredCellColumns();
    const drawn = this.cellColumns();
    return (this.grid.model()?.headers ?? []).map(row => spanHeaderRow(row, declared, drawn));
  });

  /** The columns that start a group of more than one in any header row, by index among the drawn columns. */
  private readonly groupStarts = computed<ReadonlySet<number>>(() => {
    const drawn = this.cellColumns();
    const paths = groupStartPaths(this.grid.model()?.headers ?? [], this.declaredCellColumns(), drawn);
    return new Set(drawn.map((column, index) => (column.path && paths.has(column.path) ? index : -1)).filter(i => i >= 0));
  });

  /** The rows the page pins under the header, with their cells; outside the virtual sequence. */
  protected readonly topRows = computed(() => this.pinnedRows(this.grid.topRows(), 'top'));

  /** The rows the page pins at the bottom edge. */
  protected readonly bottomRows = computed(() => this.pinnedRows(this.grid.bottomRows(), 'bottom'));

  /** The rows the head counts before the page's: the header rows, the column header and the pinned top rows. */
  private readonly headRows = computed(() => this.headerRows().length + 1 + this.topRows().length);

  /** Every row of the result and of the head and foot; -1 while the total is unknown. */
  protected readonly ariaRowCount = computed(() =>
    this.grid.isCountPending() || this.grid.isCountFailed() ? -1 : this.headRows() + this.grid.total() + this.bottomRows().length,
  );

  /** The active cell's column and what the keys do to the active cell; the active row is the grid's. */
  protected readonly keyboard = new TableKeyboard(this.grid, {
    pageRows: () => this.scroll.pageRows(),
    activeCell: () => this.activeCellElement(),
    afterRender: run => afterNextRender(run, { injector: this.injector }),
  });

  protected readonly activeCellId = computed(() => {
    const index = this.grid.activeRowIndex();
    const column = this.keyboard.activeColumn();
    return index >= 0 && index < this.rows().length && column >= 0 ? cellElementIdOf(this.id, index, column) : null;
  });

  /** What each column's header says of the sort: only the first sorted column's, as the pattern allows one. */
  protected readonly ariaSorts = computed(() => {
    const first = this.grid.sort()[0];
    return this.tableColumns().map(column => (first && column.path === first.field ? ARIA_SORT[first.direction] : null));
  });

  /** What the sticky columns cover at either edge, so revealing a cell scrolls it clear of them. */
  protected readonly stickyStart = computed(() => stickyEdgeOf(this.leftOffsets(), this.widths()));
  protected readonly stickyEnd = computed(() => stickyEdgeOf(this.rightOffsets(), this.widths()));

  /** Widths in effect, by column index; what the colgroup, the sticky offsets and the table's width read. */
  protected readonly widths = computed(() => this.tableColumns().map(column => effectiveWidthOf(column)));

  protected readonly tableWidth = computed(() => this.widths().reduce((sum, width) => sum + width, 0));

  /** What each column's `col` states: its share of the table where the columns share it, nothing where one stretches, its width. */
  protected readonly colWidths = computed<readonly (string | null)[]>(() => {
    if (this.stretches()) {
      return this.widths().map(() => null);
    }
    if (this.shares()) {
      const total = this.tableWidth();
      return this.widths().map(width => `${(width * 100) / total}%`);
    }
    return this.widths().map(width => `${width}px`);
  });

  private readonly leftOffsets = computed(() => leftOffsetsOf(this.tableColumns(), this.widths()));
  private readonly rightOffsets = computed(() => rightOffsetsOf(this.tableColumns(), this.widths()));

  /**
   * The sticky edges as CSS values, by column index, read by the `left`/`right`
   * of the sticky variant alone, so a static cell is not moved by them.
   */
  protected readonly stickyLefts = computed(() => this.leftOffsets().map(offset => stickyValueOf(offset)));
  protected readonly stickyRights = computed(() => this.rightOffsets().map(offset => stickyValueOf(offset)));

  /** The classes a column gives its header and body cells, by column index: its sticky variant and a group's start. */
  protected readonly columnClasses = computed(() =>
    columnClassesOf(this.tableColumns(), this.leftOffsets(), this.rightOffsets(), this.groupStarts()),
  );

  /** The column header's classes, by column index: its column's, and no right line when the one column stretches. */
  protected readonly headerClasses = computed(() => {
    const stretches = this.stretches();
    const picking = this.grid.isPicking();
    return this.columnClasses().map(classes => {
      let result = classes;
      if (stretches) result += ' border-r-0';
      if (picking) result += ' h-7!';
      return result;
    });
  });

  /**
   * The class sets of the drawn rows' cells, by row and column, one list per section. They stand apart from the rows,
   * so a move of the active cell or of a save's marks keeps the rows and their cells, and the outlets with them.
   */
  protected readonly bodyCellClasses = computed(() => {
    const columns = this.columnClasses();
    const active = this.grid.activeRowIndex();
    return this.bodyRows().map(item =>
      rowCellClassesOf(item, columns, item.index === active ? this.keyboard.activeColumn() : -1, item.marks),
    );
  });
  protected readonly topCellClasses = computed(() => this.pinnedCellClasses(this.topRows()));
  protected readonly bottomCellClasses = computed(() => this.pinnedCellClasses(this.bottomRows()));
  /** What the cell being edited adds to its classes: the field's frame, red while the messages of a refused save stand. */
  protected readonly editingCellClass = computed(() => editingCellClassOf((this.grid.edits.current()?.messages.length ?? 0) > 0));

  /** The classes of each header row's cells, by row and cell. */
  protected readonly headerRowClasses = computed(() => headerRowClassesOf(this.headerRows(), this.columnClasses(), this.groupStarts()));

  protected readonly id = `ui-grid-${++GridTableComponent.next}`;
  /** The id of the hidden text a changed cell is described by. */
  protected readonly changedTextId = `${this.id}-changed`;
  /**
   * The most a drag draws a column; a column's own most width bounds the drag as well, unless the columns share the
   * table, where it bounds the width written (`GridColumnState.setWidth`) and not the one drawn.
   */
  protected readonly maxWidth = MAX_COLUMN_WIDTH;
  protected readonly minWidthOf = minWidthOf;
  private columnLayoutKey: string | null = null;

  /** A header row's cell for its outlet, the same object between renders. */
  private readonly headerRowCells = new WeakMap<GridHeaderRowCell, GridCellContext>();

  /** A column header's cell for its outlet, the same object while the column's object stands. */
  private readonly columnHeaderCells = new WeakMap<GridColumnDto, GridCellContext>();

  constructor() {
    // The rows the page holds: a new sequence starts the window from the top
    effect(() => {
      const keys = this.rows().map((row, index) => keyOf(row) ?? index);
      untracked(() => this.scroll.setRows(keys));
    });

    // The active row is kept in view
    effect(() => {
      const index = this.grid.activeRowIndex();
      untracked(() => {
        if (index >= 0 && index < this.rows().length) {
          this.scroll.scrollToIndex(index);
        }
      });
    });

    // Other widths make other row heights: what is off screen is measured again as it comes in
    effect(() => {
      const widths = this.widths();
      const key = JSON.stringify(this.tableColumns().map((column, index) => [column.path, widths[index], column.cell.kind]));
      untracked(() => {
        if (this.columnLayoutKey !== null && this.columnLayoutKey !== key) {
          this.scroll.invalidateColumnLayout();
        }
        this.columnLayoutKey = key;
      });
    });

    // Once the viewport is in the DOM it is watched and its scroll heard by the
    // driver; then rows that entered or left the DOM are observed or forgotten
    afterRenderEffect(() => {
      this.renderedRows();
      this.scroll.attach();
      this.scroll.syncObservedRows();
      this.measurePinnedBottom();
    });

    // The first page is in: the columns without a width are fitted once, before the user sees them settle. A picker's
    // list fits them again at every page it reads and raises each to the wider width, so they only widen while it is
    // open; a run that follows a raise fits the same widths and writes nothing
    afterRenderEffect(() => {
      const data = this.grid.data();
      const columns = this.tableColumns();
      const keepsWider = this.grid.isPickerList();
      untracked(() => {
        if (data) {
          fitColumnsAtOpening(this.grid.columnState, columns, fitSampleOf(data), tableMeasure(), this.locale(), this.zone(), keepsWider);
        }
      });
    });

    // The table is the one Tab stop: whatever a cell draws that would take one of its own is reached through the active cell
    let stopTabOrder: (() => void) | null = null;
    afterNextRender(() => (stopTabOrder = keepCellControlsOutOfTabOrder(this.table().nativeElement)), { injector: this.injector });

    // A search box that drives the grid names its table and its active cell
    const ids: GridTableIds = { table: this.id, activeCell: this.activeCellId, tableWidth: this.tableWidth };
    this.grid.tableIds.set(ids);

    this.destroyRef.onDestroy(() => {
      this.scroll.destroy();
      stopTabOrder?.();
      if (untracked(this.grid.tableIds) === ids) {
        this.grid.tableIds.set(null);
      }
    });
  }

  protected headerRowCell(item: GridHeaderRowCell): GridCellContext {
    let cell = this.headerRowCells.get(item);
    if (!cell) {
      cell = headerRowCell(item);
      this.headerRowCells.set(item, cell);
    }
    return cell;
  }

  /** Where a row of the head sticks: under the header rows above it and the column header or pinned rows before it. */
  protected headTop(headerRows: number, rows: number): string {
    return `${headerRows * HEADER_ROW_REM + rows * ROW_REM}rem`;
  }

  /** A pinned bottom row's place: after every row of the result; unknown while the total is. */
  protected bottomRowIndexOf(index: number): number | null {
    const count = this.ariaRowCount();
    return count < 0 ? null : count - this.bottomRows().length + index + 1;
  }

  protected trackRow(index: number, row: GridRow): GridRowKey {
    return this.scroll.hasDuplicateKeys ? index : (keyOf(row) ?? index);
  }

  /** Whether the cell is the one being edited: a body cell, never a pinned one. */
  protected isEdited(cell: GridCellContext): boolean {
    return cell.index >= 0 && !cell.pinned && this.grid.edits.isEditing(keyOf(cell.row), cell.column.path);
  }

  /**
   * A double click on a body row's editable cell starts its edit, as `F2` does; the edits refuse any other cell. One on
   * a link or a button the cell draws edits nothing: each press keeps its own action.
   */
  protected editCell(cell: GridCellContext, event: MouseEvent): void {
    if (cell.row && cell.index >= 0 && !cell.pinned && !isOwnControl(event)) {
      this.grid.edits.start(cell.row, cell.column);
    }
  }

  /** The second press of a double click on an editable cell, a checkbox among them, selects no word of its text. */
  protected keepWordsUnselected(event: MouseEvent, cell: GridCellContext): void {
    if (event.detail > 1 && editKindOf(cell.column) !== null) {
      event.preventDefault();
    }
  }

  /** A click on a cell makes its column the active one; on a body row's editable checkbox it toggles and saves it. */
  protected clickCell(cell: GridCellContext, index: number): void {
    this.keyboard.activateColumn(index);
    if (cell.row && cell.index >= 0 && !cell.pinned && editKindOf(cell.column) === 'checkbox') {
      void this.grid.edits.toggle(cell.row, cell.column);
    }
  }

  protected activateRow(index: number): void {
    this.grid.setActive(index);
    this.grid.pickActive();
  }

  /** A drag on the column's edge writes the width that draws the column as wide as the drag reached. */
  protected resizeColumn(column: GridColumnDto, width: number): void {
    if (column.path) {
      this.grid.columnState.setWidth(column.path, this.widthDrawing(column, width));
    }
  }

  /** A double click on the column's edge fits the drawn column to its content, or restores it the second time. */
  protected toggleFit(column: GridColumnDto): void {
    if (column.path) {
      this.grid.columnState.toggleFit(column.path, () =>
        this.widthDrawing(
          column,
          contentWidthOf(
            column,
            this.tableColumns().indexOf(column),
            this.renderedRows().map(row => row.nativeElement),
            tableMeasure(),
          ),
        ),
      );
    }
  }

  /**
   * The width that draws the column `drawn` pixels wide: `drawn` itself unless the columns share the table, where it is
   * taken beside the other columns' widths in the room the table spans, its viewport's width (`sharedWidthOf`).
   */
  private widthDrawing(column: GridColumnDto, drawn: number): number {
    if (!this.shares()) {
      return drawn;
    }
    const own = this.widths()[this.tableColumns().indexOf(column)] ?? 0;
    return sharedWidthOf(drawn, this.tableWidth() - own, this.viewportElement()?.clientWidth ?? 0);
  }

  private activeCellElement(): HTMLElement | null {
    const id = this.activeCellId();
    return id ? this.table().nativeElement.querySelector<HTMLElement>(`[id="${id}"]`) : null;
  }

  /** The cell classes of pinned rows by row and column: their section's and their columns'; a pinned cell is never active nor marked. */
  private pinnedCellClasses(rows: readonly TableRow[]): string[][] {
    const columns = this.columnClasses();
    return rows.map(item => rowCellClassesOf(item, columns, -1));
  }

  private pinnedRows(rows: readonly GridRow[], pinned: 'top' | 'bottom'): readonly TableRow[] {
    const columns = this.cellColumns();
    const cellClass = pinned === 'top' ? PINNED_TOP_CELL : PINNED_BOTTOM_CELL;
    return rows.map(row => ({ row, cells: rowCells(row, columns, -1, pinned), cellClass }));
  }

  /** The pinned bottom rows' height, for what floats above them; nothing while they stand in the flow, below `md`. */
  private measurePinnedBottom(): void {
    const height = pinnedInset(this.pinnedBottom()?.nativeElement);
    if (Math.abs(height - this.bottomInset()) > PIXEL_TOLERANCE) {
      this._bottomInset.set(height);
    }
  }

  private viewportElement(): HTMLDivElement | null {
    return this.viewport()?.nativeElement ?? null;
  }
}

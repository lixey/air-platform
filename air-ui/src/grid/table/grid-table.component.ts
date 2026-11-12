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
import type { GridColumnDto } from '../../core/generated/contracts';
import { problemOf } from '../../core/problem';
import { TextsService } from '../../core/texts.service';
import type { GridCellContext } from '../cell/grid-cell-context';
import { GridCellEditorComponent } from '../cell/grid-cell-editor.component';
import { GridCellOutletComponent } from '../cell/grid-cell-outlet.component';
import { TABLE_TEXT, tableMeasure } from '../column/grid-column-fit';
import { MAX_COLUMN_WIDTH, minWidthOf } from '../column/grid-column-width';
import { editKindOf } from '../grid-cell-edits';
import { type GridRow, type GridRowKey, keyOf } from '../grid-row';
import { GridService, type GridTableIds } from '../grid.service';
import { FILLER, HEADER_CELL, HEADER_ROW_CELL, PINNED_ROW, ROW, TABLE_RING } from './grid-table-classes';
import { TableKeyboard } from './grid-table-keys';
import { fitColumnsAtOpening, fitSampleOf, pinnedInset } from './grid-table-measure';
import { keepCellControlsOutOfTabOrder } from './grid-table-tab-order';
import { ESTIMATED_ROW_HEIGHT, GridTableView, isOwnControl } from './grid-table-view';
import { ResizableDirective } from './resizable.directive';
import { PIXEL_TOLERANCE, VirtualScrollDriver } from './virtual-scroll-driver';

/**
 * The virtual WAI-ARIA table of a grid: renders only rows near the viewport over `VirtualScrollDriver`, projects
 * columns, sticky edges, group headers, and pinned rows via `GridTableView`, and manages single-tab-stop keyboard
 * navigation and inline cell editing.
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
      [style.scroll-padding-left.px]="view.stickyStart()"
      [style.scroll-padding-right.px]="view.stickyEnd()"
    >
      <table
        #table
        role="grid"
        [id]="id"
        tabindex="0"
        class="w-full table-fixed border-separate border-spacing-0 ${TABLE_TEXT} outline-none"
        [class.mb-10]="paged()"
        [style.min-width.px]="view.stretches() ? null : view.tableWidth()"
        [attr.aria-rowcount]="view.ariaRowCount()"
        [attr.aria-colcount]="tableColumns().length"
        [attr.aria-multiselectable]="grid.isSelectable() ? 'true' : null"
        [attr.aria-activedescendant]="view.activeCellId()"
        (keydown)="keyboard.onKeyDown($event)"
      >
        <colgroup>
          @for (column of tableColumns(); track column.path; let index = $index) {
            <col [style.width]="view.colWidths()[index]" />
          }
          @if (view.hasFiller()) {
            <col />
          }
        </colgroup>

        <thead>
          @for (row of view.headerRows(); track $index; let rowIndex = $index) {
            <tr class="sticky z-20" [style.top]="view.headTop(rowIndex, 0)" [attr.aria-rowindex]="rowIndex + 1">
              @for (item of row; track $index; let cellIndex = $index) {
                <th
                  ui-grid-cell-outlet
                  scope="colgroup"
                  class="${HEADER_ROW_CELL}"
                  [class]="view.headerRowClasses()[rowIndex][cellIndex]"
                  [style.--sticky-left]="item.isSticky ? view.stickyLefts()[item.index] : null"
                  [attr.colspan]="item.span"
                  [cell]="view.headerRowCell(item)"
                  [model]="item.cell.model"
                  fallback="GridHeaderLabel"
                ></th>
              }
              @if (view.hasFiller()) {
                <th class="${HEADER_ROW_CELL} border-r-0" aria-hidden="true"></th>
              }
            </tr>
          }
          <tr
            class="sticky z-20"
            [style.top]="view.headTop(view.headerRows().length, 0)"
            [attr.aria-rowindex]="view.headerRows().length + 1"
          >
            @for (header of view.headerCells(); let index = $index; track header.column.path) {
              <th
                ui-grid-cell-outlet
                scope="col"
                class="${HEADER_CELL}"
                [class]="view.headerClasses()[index]"
                [style.--sticky-left]="view.stickyLefts()[index]"
                [style.--sticky-right]="view.stickyRights()[index]"
                [attr.aria-sort]="view.ariaSorts()[index]"
                [cell]="header"
                [model]="header.column.header"
                fallback="GridHeaderDefault"
                [uiResizable]="header.column.isResizable ? 'right' : 'none'"
                [uiResizableMin]="minWidthOf(header.column)"
                [uiResizableMax]="view.shares() ? maxWidth : (header.column.maxWidth ?? maxWidth)"
                (uiResizableResized)="resizeColumn(header.column, $event)"
                (uiResizableFit)="toggleFit(header.column)"
              ></th>
            }
            @if (view.hasFiller()) {
              <th class="${HEADER_CELL} border-r-0" [class.h-7!]="grid.isPicking()" aria-hidden="true"></th>
            }
          </tr>
          @for (item of view.topRows(); track $index; let pinnedIndex = $index) {
            <tr
              role="row"
              class="${PINNED_ROW} md:sticky z-20"
              [attr.aria-rowindex]="view.headerRows().length + 2 + pinnedIndex"
              [attr.aria-label]="texts.of('PinnedRow')"
              [style.top]="view.headTop(view.headerRows().length, pinnedIndex + 1)"
            >
              <ng-container
                [ngTemplateOutlet]="rowCellTemplate"
                [ngTemplateOutletContext]="{ $implicit: item, classes: view.topCellClasses()[pinnedIndex] }"
              />
              @if (view.hasFiller()) {
                <td class="${FILLER} border-b" aria-hidden="true"></td>
              }
            </tr>
          }
        </thead>

        <tbody>
          <tr aria-hidden="true">
            <td class="p-0 border-0" [attr.colspan]="view.fullSpan()" [style.height.px]="topSpacer()"></td>
          </tr>

          @for (item of view.bodyRows(); track trackRow(item.index, item.row); let offset = $index) {
            <tr
              #virtualRow
              role="row"
              class="${ROW}"
              [id]="item.id"
              [class]="view.bodyRowStates()[offset].classes"
              [attr.data-row-index]="item.index"
              [attr.aria-rowindex]="item.ariaRowIndex"
              [attr.aria-selected]="view.bodyRowStates()[offset].ariaSelected"
              (click)="activateRow(item.index)"
            >
              <ng-container
                [ngTemplateOutlet]="rowCellTemplate"
                [ngTemplateOutletContext]="{ $implicit: item, classes: view.bodyCellClasses()[offset] }"
              />
              @if (view.hasFiller()) {
                <td class="${FILLER}" aria-hidden="true"></td>
              }
            </tr>
          }

          @if (grid.shortOf(); as least) {
            <tr role="row" data-grid-state="short">
              <td role="gridcell" class="px-3 py-2 text-muted" [attr.colspan]="view.fullSpan()">
                <span role="status">{{ texts.of('TypeAtLeast', least) }}</span>
              </td>
            </tr>
          } @else if (grid.error() !== undefined) {
            <tr role="row" data-grid-state="error">
              <td role="gridcell" class="px-3 py-2 text-warning" [attr.colspan]="view.fullSpan()">
                <span role="alert" class="inline-flex items-center gap-3">
                  {{ failure() }}
                  <ui-btn size="small" [label]="texts.of('Retry')" (click)="grid.retry()" />
                </span>
              </td>
            </tr>
          } @else if (rows().length === 0 && grid.data() !== null && !grid.isLoading()) {
            <tr role="row" data-grid-state="empty">
              <td role="gridcell" class="px-3 py-2 text-muted" [attr.colspan]="view.fullSpan()">
                {{ texts.of('NoRows') }}
              </td>
            </tr>
          }

          <tr aria-hidden="true">
            <td
              class="p-0 border-line"
              [class.border-b]="view.bottomRows().length === 0"
              [attr.colspan]="view.fullSpan()"
              [style.height.px]="bottomSpacer()"
            ></td>
          </tr>
        </tbody>

        @if (view.bottomRows().length > 0) {
          <tfoot #pinnedBottom class="md:sticky bottom-0 z-20">
            @for (item of view.bottomRows(); track $index; let bottomIndex = $index) {
              <tr
                role="row"
                class="${PINNED_ROW}"
                [attr.aria-label]="texts.of('PinnedRow')"
                [attr.aria-rowindex]="view.bottomRowIndexOf(bottomIndex)"
              >
                <ng-container
                  [ngTemplateOutlet]="rowCellTemplate"
                  [ngTemplateOutletContext]="{ $implicit: item, classes: view.bottomCellClasses()[bottomIndex] }"
                />
                @if (view.hasFiller()) {
                  <td class="${FILLER} border-t" aria-hidden="true"></td>
                }
              </tr>
            }
          </tfoot>
        }
      </table>
    </div>

    <span [id]="changedTextId" hidden>{{ texts.of('Changed') }}</span>

    <ng-template #rowCellTemplate let-item let-classes="classes">
      @for (cell of item.cells; let index = $index; track cell.column.path) {
        @if (isEdited(cell)) {
          <td
            role="gridcell"
            [class]="classes[index] + ' ' + view.editingCellClass()"
            [attr.id]="item.cellIds?.[index]"
            [style.--sticky-left]="view.stickyLefts()[index]"
            [style.--sticky-right]="view.stickyRights()[index]"
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
            [style.--sticky-left]="view.stickyLefts()[index]"
            [style.--sticky-right]="view.stickyRights()[index]"
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

  readonly paged = input(false);

  private readonly viewport = viewChild<ElementRef<HTMLDivElement>>('viewport');
  private readonly table = viewChild.required<ElementRef<HTMLTableElement>>('table');
  private readonly renderedRows = viewChildren<ElementRef<HTMLTableRowElement>>('virtualRow');
  private readonly pinnedBottom = viewChild<ElementRef<HTMLTableSectionElement>>('pinnedBottom');

  private readonly _bottomInset = signal(0);
  readonly bottomInset = this._bottomInset.asReadonly();
  protected readonly rows = this.grid.rows;
  protected readonly failure = computed(() => problemOf(this.grid.error())?.title ?? this.texts.of('RequestFailed'));
  protected readonly tableColumns = this.grid.tableColumns;

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
  protected readonly topSpacer = this.scroll.topSpacer;
  protected readonly bottomSpacer = this.scroll.bottomSpacer;

  protected readonly id = `ui-grid-${++GridTableComponent.next}`;
  protected readonly changedTextId = `${this.id}-changed`;
  protected readonly maxWidth = MAX_COLUMN_WIDTH;
  protected readonly minWidthOf = minWidthOf;

  protected readonly keyboard = new TableKeyboard(this.grid, {
    pageRows: () => this.scroll.pageRows(),
    activeCell: () => this.activeCellElement(),
    afterRender: run => afterNextRender(run, { injector: this.injector }),
  });

  protected readonly view = new GridTableView({
    id: this.id,
    grid: this.grid,
    startIndex: this.scroll.startIndex,
    endIndex: this.scroll.endIndex,
    activeColumn: this.keyboard.activeColumn,
  });

  private columnLayoutKey: string | null = null;

  constructor() {
    effect(() => {
      const keys = this.rows().map((row, index) => keyOf(row) ?? index);
      untracked(() => this.scroll.setRows(keys));
    });

    effect(() => {
      const index = this.grid.activeRowIndex();
      untracked(() => {
        if (index >= 0 && index < this.rows().length) {
          this.scroll.scrollToIndex(index);
        }
      });
    });

    effect(() => {
      const widths = this.view.widths();
      const key = JSON.stringify(this.tableColumns().map((column, index) => [column.path, widths[index], column.cell.kind]));
      untracked(() => {
        if (this.columnLayoutKey !== null && this.columnLayoutKey !== key) {
          this.scroll.invalidateColumnLayout();
        }
        this.columnLayoutKey = key;
      });
    });

    afterRenderEffect(() => {
      this.renderedRows();
      this.scroll.attach();
      this.scroll.syncObservedRows();
      this.measurePinnedBottom();
    });

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

    let stopTabOrder: (() => void) | null = null;
    afterNextRender(() => (stopTabOrder = keepCellControlsOutOfTabOrder(this.table().nativeElement)), { injector: this.injector });

    const ids: GridTableIds = { table: this.id, activeCell: this.view.activeCellId, tableWidth: this.view.tableWidth };
    this.grid.tableIds.set(ids);

    this.destroyRef.onDestroy(() => {
      this.scroll.destroy();
      stopTabOrder?.();
      if (untracked(this.grid.tableIds) === ids) {
        this.grid.tableIds.set(null);
      }
    });
  }

  protected trackRow(index: number, row: GridRow): GridRowKey {
    return this.scroll.hasDuplicateKeys ? index : (keyOf(row) ?? index);
  }

  protected isEdited(cell: GridCellContext): boolean {
    return cell.index >= 0 && !cell.pinned && this.grid.edits.isEditing(keyOf(cell.row), cell.column.path);
  }

  protected editCell(cell: GridCellContext, event: MouseEvent): void {
    if (cell.row && cell.index >= 0 && !cell.pinned && !isOwnControl(event)) {
      this.grid.edits.start(cell.row, cell.column);
    }
  }

  protected keepWordsUnselected(event: MouseEvent, cell: GridCellContext): void {
    if (event.detail > 1 && editKindOf(cell.column, cell.row) !== null) {
      event.preventDefault();
    }
  }

  protected clickCell(cell: GridCellContext, index: number): void {
    this.keyboard.activateColumn(index);
    if (cell.row && cell.index >= 0 && !cell.pinned && editKindOf(cell.column, cell.row) === 'checkbox') {
      void this.grid.edits.toggle(cell.row, cell.column);
    }
  }

  protected activateRow(index: number): void {
    this.grid.setActive(index);
    this.grid.pickActive();
  }

  protected resizeColumn(column: GridColumnDto, width: number): void {
    this.view.resizeColumn(column, width, this.viewportElement()?.clientWidth ?? 0);
  }

  protected toggleFit(column: GridColumnDto): void {
    this.view.toggleFit(
      column,
      this.renderedRows().map(row => row.nativeElement),
      this.viewportElement()?.clientWidth ?? 0,
    );
  }

  private activeCellElement(): HTMLElement | null {
    const id = this.view.activeCellId();
    return id ? this.table().nativeElement.querySelector<HTMLElement>(`[id="${id}"]`) : null;
  }

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

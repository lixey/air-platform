import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { GridColumnDto } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { NavComponent } from '../../nav/nav.component';
import { OverlayRef } from '../../overlay/overlay-ref';
import type { SelectOption } from '../../select/select-option';
import type { SelectSource } from '../../select/select-source';
import { SelectComponent } from '../../select/select.component';
import { selectSourceOf } from '../column/grid-column-select-source';
import { GridService } from '../grid.service';
import { FilterTreeComponent } from './filter-tree.component';
import { fieldOf, referenceFilterOf } from './grid-filter-set';

/** A reference or a select column's multiselect: the key it filters and the select over the column's filter service or its options. */
interface ReferencePick {
  readonly field: string;
  readonly source: SelectSource;
}

/**
 * The general filters, opened from the nav's `Filters`: the bar — `Apply`, the title, the close control — then a
 * multiselect per reference column with a filter service and per select column, over its options, hidden columns too,
 * the picked of each a draft until `Apply`; below them the filter tree
 * (`ui-filter-tree`) over the other filterable columns, each condition a column,
 * an operator and a value typed in the library's control for the column's type, joined in groups by AND or OR, the
 * tree joined to the multiselects by AND. A reference column is never a condition: its key is filtered by its
 * multiselect, with the rule its header posts (`referenceFilterOf`). `Apply` replaces the grid's general filters with
 * the picked, the filters of the reference columns without a multiselect, and the tree, in one request. `ClearAll`
 * empties the draft, the multiselects, the tree and the filters of the reference columns without a multiselect, and
 * keeps the drawer open; `Apply` commits it; closing discards the draft, a cleared one too.
 */
@Component({
  selector: 'ui-grid-filters-overlay',
  imports: [BtnComponent, FilterTreeComponent, NavComponent, SelectComponent],
  template: `
    <ui-nav class="shrink-0 border-b border-line bg-surface">
      <ui-btn start [label]="texts.of('Apply')" icon="apply" color="emerald" (click)="apply()" />
      <h2 class="m-0 min-w-0 truncate text-base font-semibold">{{ texts.of('Filters') }}</h2>
      <ui-btn end icon="close" variant="ghost" color="slate" [title]="texts.of('Close')" (click)="ref?.dismiss()" />
    </ui-nav>

    <div class="flex flex-col gap-3 p-3 min-w-0 min-h-0 overflow-y-auto overflow-x-hidden">
      @if (references().length > 0) {
        <div class="@container">
          <div class="grid grid-cols-1 gap-2 @md:grid-cols-2 @3xl:grid-cols-3">
            @for (reference of references(); track reference.field) {
              <div class="flex flex-col gap-1 min-w-0">
                <span class="text-sm text-muted truncate" aria-hidden="true">{{ reference.source.label }}</span>
                <ui-select multiple [model]="reference.source" [picked]="draftOf(reference)" (pickedChange)="setDraft(reference, $event)" />
              </div>
            }
          </div>
        </div>
      }

      <ui-filter-tree [filter]="opening" [fields]="columns()" (applied)="apply()" (cleared)="clearAll()" />
    </div>
  `,
  host: { class: 'flex flex-col min-w-0 min-h-0 max-h-full overflow-hidden' },
})
export class GridFiltersOverlayComponent {
  protected readonly texts = inject(TextsService);
  private readonly grid = inject(GridService, { optional: true });
  protected readonly ref = inject(OverlayRef, { optional: true });

  /** The reference columns that name a filter service and the select columns, hidden ones included: one multiselect each. */
  protected readonly references = computed<readonly ReferencePick[]>(() =>
    (this.grid?.allColumns() ?? []).flatMap(column => referencePickOf(column) ?? []),
  );

  /** The columns a condition may filter: the filterable ones filtered by their own path, so no reference column. */
  protected readonly columns = computed<readonly GridColumnDto[]>(() => (this.grid?.allColumns() ?? []).filter(takesConditions));

  /** What each multiselect holds until `Apply`, by the key it filters, starting from the grid's filters. */
  private readonly drafts = signal<Readonly<Record<string, readonly SelectOption[]>>>(
    Object.fromEntries(this.references().map(reference => [reference.field, this.grid?.referencePicksOf(reference.field) ?? []])),
  );

  /** Whether `ClearAll` emptied the draft, so `Apply` drops the filters of the reference columns without a multiselect. */
  private readonly isCleared = signal(false);

  /** The grid's tree as the drawer opens: a reference column's filter is its multiselect's to edit, the tree holds the typed ones. */
  protected readonly opening = this.grid?.filterTree();

  private readonly tree = viewChild.required(FilterTreeComponent);

  protected draftOf(reference: ReferencePick): readonly SelectOption[] {
    return this.drafts()[reference.field] ?? [];
  }

  protected setDraft(reference: ReferencePick, picked: readonly SelectOption[]): void {
    this.drafts.update(drafts => ({ ...drafts, [reference.field]: picked }));
  }

  protected apply(): void {
    if (!this.grid) {
      return;
    }

    const references = this.references();
    const picked = references.flatMap(reference => referenceFilterOf(reference.field, this.draftOf(reference)) ?? []);
    // A reference column without a filter service keeps what its cell's link picked, unless Clear all emptied the draft
    const fields = new Set(references.map(reference => reference.field));
    const kept = this.isCleared() ? [] : this.grid.filterPicks().filter(filter => !fields.has(fieldOf(filter)));
    const tree = this.tree().node();
    this.grid.setFilters(tree ? [...picked, ...kept, tree] : [...picked, ...kept]);
    this.ref?.dismiss();
  }

  protected clearAll(): void {
    this.drafts.set(Object.fromEntries(this.references().map(reference => [reference.field, []])));
    this.tree().clear();
    this.isCleared.set(true);
  }
}

/** The multiselect of a reference column that names a filter service, or of a select column over its options; none for any other column. */
function referencePickOf(column: GridColumnDto): ReferencePick | null {
  const source = selectSourceOf(column);
  return source && column.filterPath ? { field: column.filterPath, source } : null;
}

/** Whether a condition may filter the column: filterable, and filtered by its own path — a reference column filters its key. */
function takesConditions(column: GridColumnDto): boolean {
  const isOwnPath = !!column.path && (column.filterPath ?? column.path) === column.path;
  return column.isFilterable && isOwnPath;
}

import { computed, linkedSignal, type Signal } from '@angular/core';
import type { GridModel, GridPageDto, SortDirection, SortRule, UserProfileDto } from '../core/generated/contracts';
import type { SelectOption } from '../select/select-option';
import { optionOfPickedValue } from '../select/select-option';
import type { SelectService } from '../select/select.service';
import {
  conjunctsOf,
  fieldOf,
  type GridFilter,
  GridFilterSet,
  initialFiltersOf,
  isEmptyFilter,
  isPick,
  normalizedFilter,
  referenceFilterOf,
  rulesOf,
  toFilterNode,
} from './filter/grid-filter-set';
import { GridNamedFilters } from './grid-named-filters';
import type { GridQueryState as GridRequestSnapshot } from './grid-request';
import type { GridRow } from './grid-row';

export interface GridGeneration {
  readonly model: GridModel | null;
  readonly invalidations: number;
}

export interface GridHeld {
  readonly generation: GridGeneration;
  readonly opening: GridPageDto<GridRow> | null;
}

export interface GridQueryStateOptions {
  readonly model: Signal<GridModel | null>;
  readonly generation: Signal<GridGeneration>;
  readonly held: Signal<GridHeld>;
  readonly user: () => UserProfileDto | null;
  readonly select: SelectService | null;
}

/**
 * Holds the reactive query state of one grid: page number, sort rules, search text, general reference picks and filter
 * tree, per-column filters, and named filters.
 */
export class GridQueryState {
  private readonly filterSet: GridFilterSet<GridGeneration>;
  private readonly columnFilterSet: GridFilterSet<GridGeneration>;
  private readonly named: GridNamedFilters;
  private readonly select: SelectService | null;
  private readonly _page: ReturnType<typeof linkedSignal<GridHeld, number>>;
  private readonly _sort: ReturnType<typeof linkedSignal<unknown, readonly SortRule[]>>;
  private readonly _search: ReturnType<typeof linkedSignal<GridGeneration, string>>;
  private readonly _filterTree: ReturnType<typeof linkedSignal<{ readonly model: GridModel | null }, GridFilter | undefined>>;

  readonly page: Signal<number>;
  readonly sort: Signal<readonly SortRule[]>;
  readonly search: Signal<string>;
  readonly filterPicks: Signal<readonly GridFilter[]>;
  readonly filterTree: Signal<GridFilter | undefined>;
  readonly filters: Signal<readonly GridFilter[]>;
  readonly columnFilters: Signal<readonly GridFilter[]>;
  readonly namedFilters: Signal<readonly string[]>;

  constructor(options: GridQueryStateOptions) {
    const { model, generation, held, user, select } = options;
    this.select = select;

    this.filterSet = new GridFilterSet(generation, gen => initialFiltersOf(gen.model).filter(isPick));
    this.columnFilterSet = new GridFilterSet(generation);
    this.named = new GridNamedFilters({ model, resetBy: generation, user });

    this._page = linkedSignal({ source: held, computation: current => current.opening?.page ?? 1 });
    this.page = this._page.asReadonly();

    this._sort = linkedSignal<unknown, readonly SortRule[]>({ source: generation, computation: () => [] });
    this.sort = this._sort.asReadonly();

    this._search = linkedSignal({ source: generation, computation: gen => gen.model?.initialSearch ?? '' });
    this.search = this._search.asReadonly();

    this.filterPicks = this.filterSet.state;
    this._filterTree = linkedSignal<{ readonly model: GridModel | null }, GridFilter | undefined>({
      source: generation,
      computation: gen => toFilterNode(initialFiltersOf(gen.model).filter(filter => !isPick(filter))),
    });
    this.filterTree = this._filterTree.asReadonly();

    this.filters = computed<readonly GridFilter[]>(() => {
      const tree = this.filterTree();
      const refFilter = select?.model().referenceFilter;
      const base = tree ? [...this.filterPicks(), tree] : this.filterPicks();
      return refFilter ? [...base, refFilter] : base;
    });

    this.columnFilters = this.columnFilterSet.state;
    this.namedFilters = this.named.chosen;
  }

  startsApart(): boolean {
    return this.named.startsApart();
  }

  snapshot(pageSize: number, isDataOnly: boolean): GridRequestSnapshot {
    return {
      page: this.page(),
      pageSize,
      sort: this.sort(),
      filters: this.filters(),
      columnFilters: this.columnFilters(),
      search: this.search(),
      namedFilters: this.namedFilters(),
      isDataOnly,
      referenceLabelPaths: this.select?.model().labelPaths ?? null,
      isReferencePicker: this.select?.model().isPicker,
    };
  }

  setPage(page: number): void {
    this._page.set(page);
  }

  applySort(field: string, direction: SortDirection | null, multi: boolean): void {
    if (!multi) {
      this._sort.set(direction ? [{ field, direction }] : []);
      return;
    }
    const current = [...this.sort()];
    const index = current.findIndex(s => s.field === field);
    if (direction === null) {
      if (index >= 0) {
        current.splice(index, 1);
      }
    } else if (index >= 0) {
      current[index] = { ...current[index], direction };
    } else {
      current.push({ field, direction });
    }
    this._sort.set(current);
  }

  sortOf(field: string | null | undefined): SortDirection | null {
    return this.sort().find(s => s.field === field)?.direction ?? null;
  }

  sortPositionOf(field: string | null | undefined): number | null {
    const sort = this.sort();
    const index = sort.findIndex(s => s.field === field);
    return sort.length > 1 && index >= 0 ? index + 1 : null;
  }

  setSearchValue(next: string): void {
    this._search.set(next);
  }

  applyFilters(filters: readonly GridFilter[]): void {
    this.filterSet.set(filters.filter(isPick));
    this._filterTree.set(toFilterNode(filters.filter(filter => !isPick(filter))));
  }

  applyColumnFilter(filter: GridFilter): void {
    if (isEmptyFilter(filter)) {
      this.columnFilterSet.removeByField(fieldOf(filter));
    } else {
      this.columnFilterSet.add(filter);
    }
  }

  clearColumnFilter(field: string): void {
    this.columnFilterSet.removeByField(field);
  }

  columnFilterOf(field: string | null | undefined): GridFilter | undefined {
    return field ? this.columnFilterSet.get(field) : undefined;
  }

  applyReferenceFilter(field: string, options: readonly SelectOption[]): void {
    const filter = referenceFilterOf(field, options);
    if (filter) {
      this.filterSet.add(filter);
    } else {
      this.filterSet.removeByField(field);
    }
  }

  referencePicksOf(field: string | null | undefined): readonly SelectOption[] {
    if (!field) {
      return [];
    }
    const filter = this.filterPicks().find(f => fieldOf(f) === field);
    return (filter ? (rulesOf(filter)[0]?.picked ?? []) : []).map(optionOfPickedValue);
  }

  removeFilter(field: string): void {
    this.filterSet.removeByField(field);
  }

  removeFilterItem(index: number): void {
    const tree = this.filterTree();
    if (!tree) {
      return;
    }
    const items = conjunctsOf(tree).filter((_, i) => i !== index);
    this._filterTree.set(normalizedFilter({ kind: 'FilterGroup', logic: 'and', items }));
  }

  clearGeneralFilters(): void {
    this.filterSet.clear();
    this._filterTree.set(undefined);
  }

  isNamedFilterOn(key: string): boolean {
    return this.named.isOn(key);
  }

  pressNamedFilter(key: string): boolean {
    return this.named.press(key);
  }
}

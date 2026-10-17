import type { GridRequest, SortRule } from '../core/generated/contracts';
import { type GridFilter, toFilterNode } from './filter/grid-filter-set';
import { type GridRow, type GridRowKey, keyOf } from './grid-row';

/** What the grid service holds that the server is asked with. */
export interface GridQueryState {
  readonly page: number;
  readonly pageSize: number;
  readonly sort: readonly SortRule[];
  readonly filters: readonly GridFilter[];
  readonly columnFilters: readonly GridFilter[];
  readonly search: string;
  /** The keys of the pressed named filters, in the nav's order. */
  readonly namedFilters: readonly string[];
  readonly isDataOnly: boolean;
  /** The label columns a reference picker asks for; absent for a grid on a screen. */
  readonly referenceLabelPaths?: readonly string[] | null;
  /** The grid is the list of a picker: it is asked for its own columns, matched by what the source states. */
  readonly isReferencePicker?: boolean;
}

/** The request a grid posts to its service's `get-data`: only what is set travels. */
export function buildGridRequest(state: GridQueryState): GridRequest {
  const request: GridRequest = {
    page: state.page,
    pageSize: state.pageSize,
    isDataOnly: state.isDataOnly,
  };

  if (state.sort.length > 0) {
    request.sort = [...state.sort];
  }
  const filter = toFilterNode(state.filters);
  if (filter) {
    request.filter = filter;
  }
  const columnFilter = toFilterNode(state.columnFilters);
  if (columnFilter) {
    request.columnFilter = columnFilter;
  }
  if (state.referenceLabelPaths && state.referenceLabelPaths.length > 0) {
    request.referenceLabelPaths = [...state.referenceLabelPaths];
  }
  if (state.isReferencePicker) {
    request.isReferencePicker = true;
  }
  const search = state.search.trim();
  if (search) {
    request.search = search;
  }
  if (state.namedFilters.length > 0) {
    request.namedFilters = [...state.namedFilters];
  }

  return request;
}

/**
 * `params` with the selection an action posts: the selected rows' ids and, when every one of them carries its `ver`,
 * their versions in the same order, which a deletion and a batch compare with the records.
 */
export function withSelection(params: GridRequest, selected: readonly GridRow[]): GridRequest {
  const keyed = selected.flatMap(row => {
    const key = keyOf(row);
    return key === null ? [] : [{ key, ver: row['ver'] }];
  });
  const selectedIds: GridRowKey[] = keyed.map(row => row.key);
  const versions = keyed.map(row => row.ver);
  const selectedVersions = versions.length > 0 && versions.every(isVersion) ? versions : undefined;
  return {
    ...params,
    selectedIds,
    ...(selectedVersions ? { selectedVersions } : {}),
  };
}

function isVersion(value: unknown): value is number {
  return Number.isInteger(value);
}

import type { RowDto, UiColor } from '../core/generated/contracts';
import { isRecord, valueAt } from '../core/json';
import { UI_COLORS } from '../theme/ui-color';

/** A row as the server answers it: typed metadata (`id`, `ver`, `open`, `flags`, `colors`) and its `cells`. */
export type GridRow = RowDto;

export type GridRowKey = number;

/** The row's key, its `id`; `null` when the row has no numeric id. */
export function keyOf(row: GridRow | null | undefined): GridRowKey | null {
  return typeof row?.id === 'number' ? row.id : null;
}

/** Whether an unknown value has the wire shape of a `RowDto` (`cells` record). */
export function isRowDto(value: unknown): value is RowDto {
  return isRecord(value) && isRecord(value['cells']);
}

/** Reads a cell value from `row.cells` at `path` (supports nested paths like `status.color`). */
export function cellValueAt(row: GridRow | null | undefined, path: string | null | undefined): unknown {
  return valueAt(row?.cells, path);
}

/**
 * Resolves a cell's semantic color string (`row.colors[colorPath]`, `row.colors[columnPath]`,
 * or nested `row.cells` at `colorPath` such as `status.color`), falling back to `null`.
 */
export function rowColorAt(
  row: GridRow | null | undefined,
  columnPath: string | null | undefined,
  colorPath?: string | null,
): string | null {
  if (!row) {
    return null;
  }
  if (colorPath) {
    const fromMap = row.colors?.[colorPath];
    if (typeof fromMap === 'string' && fromMap.length > 0) {
      return fromMap;
    }
    if (colorPath !== columnPath) {
      const fromCell = valueAt(row.cells, colorPath);
      if (typeof fromCell === 'string' && fromCell.length > 0) {
        return fromCell;
      }
    }
  }
  if (columnPath) {
    const fromColMap = row.colors?.[columnPath];
    if (typeof fromColMap === 'string' && fromColMap.length > 0) {
      return fromColMap;
    }
  }
  return null;
}

/** Narrows a color string (case-insensitive) to a valid `UiColor` token, or `null`. */
export function asRowUiColor(color: string | null | undefined): UiColor | null {
  if (!color) {
    return null;
  }
  const lower = color.toLowerCase();
  return UI_COLORS.find(c => c === lower) ?? null;
}

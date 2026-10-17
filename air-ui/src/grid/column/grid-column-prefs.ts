import type { GridColumnDto } from '../../core/generated/contracts';
import { boundedWidth } from './grid-column-width';

/** What the user changed about a column: the width dragged, whether it is hidden. */
export interface GridColumnPref {
  readonly width?: number;
  readonly isHidden?: boolean;
}

export type GridColumnPrefs = Readonly<Record<string, GridColumnPref>>;

/** The server's columns with the user's widths and hiding applied; the server wins for a column the prefs do not know. */
export function applyColumnPrefs(columns: readonly GridColumnDto[], prefs: GridColumnPrefs): GridColumnDto[] {
  return columns.map(column => {
    const pref = column.path ? prefs[column.path] : undefined;
    if (!pref) {
      return column;
    }

    return {
      ...column,
      ...(pref.width !== undefined ? { width: boundedWidth(pref.width, column) } : {}),
      ...(pref.isHidden !== undefined ? { isHidden: pref.isHidden } : {}),
    };
  });
}

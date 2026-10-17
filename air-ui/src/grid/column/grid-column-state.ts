import { computed, linkedSignal } from '@angular/core';
import type { GridColumnDto } from '../../core/generated/contracts';
import { applyColumnPrefs, type GridColumnPrefs } from './grid-column-prefs';
import { boundedWidth } from './grid-column-width';

/**
 * Manages client-side column state: user widths, opening widths, fit-to-content, and hiding; all of it starts over
 * whenever `resetBy` changes. The opening fit is taken once, and only a picker's list raises an opening width after it,
 * to the wider fit of a later page (`setOpeningWidth` with `keepsWider`): no other grid asks it.
 */
export class GridColumnState {
  /** What the user changed about the columns this session. */
  private readonly prefs = linkedSignal<unknown, GridColumnPrefs>({ source: () => this.resetBy(), computation: () => ({}) });

  /**
   * The widths the opening fit gave the columns the server left without one, raised in a picker's list by the wider fit
   * of a later page: their declared widths from then on.
   */
  private readonly openingWidths = linkedSignal<unknown, Readonly<Record<string, number>>>({
    source: () => this.resetBy(),
    computation: () => ({}),
  });

  /** The columns whose width is a fit to their content, so the next double click restores the declared width. */
  private readonly fitted = linkedSignal<unknown, Readonly<Record<string, true>>>({
    source: () => this.resetBy(),
    computation: () => ({}),
  });

  /**
   * The declared columns with the widths the opening fit gave: built again when a fit or a raise lands, not when the
   * user changes a column.
   */
  private readonly openedColumns = computed<readonly GridColumnDto[]>(() => {
    const opening = this.openingWidths();
    return this.declaredColumns().map(column => {
      if (!column.path) {
        return column;
      }
      return column.path in opening && (column.width === null || column.width === undefined)
        ? { ...column, width: opening[column.path] }
        : column;
    });
  });

  /**
   * Every declared column with the opening fit and the user's widths and hiding applied, in the server's order. A
   * column the user did not change is the same object until a fit lands or the declared columns change.
   */
  readonly columns = computed<readonly GridColumnDto[]>(() => applyColumnPrefs(this.openedColumns(), this.prefs()));

  constructor(
    private readonly declaredColumns: () => readonly GridColumnDto[],
    private readonly resetBy: () => unknown = () => null,
  ) {}

  /** The width the user dragged a column to, held to its bounds and kept for the session. */
  setWidth(path: string, width: number): void {
    const column = this.columns().find(c => c.path === path);
    if (!column) {
      return;
    }
    const bounded = boundedWidth(width, column);
    this.prefs.update(prefs => ({ ...prefs, [path]: { ...prefs[path], width: bounded } }));
    this.fitted.update(({ [path]: _gone, ...rest }) => rest);
  }

  /**
   * The width the opening fit measured for a column the server left without one: its declared
   * width from then on, what the second double click returns to. Taken once; a column that has
   * one keeps it, unless `keepsWider`, which a picker's list passes at every later page: the
   * column then takes the width, within its bounds, where it is wider than the one it has, and a
   * width that is not wider writes nothing. A width the user set stays over it.
   */
  setOpeningWidth(path: string, width: number, keepsWider = false): void {
    const opening = this.openingWidths()[path];
    if (opening !== undefined) {
      const declared = keepsWider ? this.declaredColumns().find(c => c.path === path) : undefined;
      const bounded = declared ? boundedWidth(width, declared) : opening;
      if (bounded > opening) {
        this.openingWidths.update(widths => ({ ...widths, [path]: bounded }));
      }
      return;
    }
    const column = this.columns().find(c => c.path === path);
    if (!column || (column.width !== null && column.width !== undefined)) {
      return;
    }
    const bounded = boundedWidth(width, column);
    this.openingWidths.update(opening => ({ ...opening, [path]: bounded }));
  }

  hasOpeningWidth(path: string): boolean {
    return path in this.openingWidths();
  }

  /**
   * A double click on the column's edge: fits the column to its rendered content the first time,
   * and returns it to the declared width — the server's, or the opening fit's, or the kind's
   * default — the second. `measure` is asked for the content's width only when the column is
   * about to be fitted.
   */
  toggleFit(path: string, measure: () => number): void {
    if (this.fitted()[path]) {
      this.reset(path);
      return;
    }
    const column = this.columns().find(c => c.path === path);
    if (!column) {
      return;
    }
    const bounded = boundedWidth(measure(), column);
    this.prefs.update(prefs => ({ ...prefs, [path]: { ...prefs[path], width: bounded } }));
    this.fitted.update(fitted => ({ ...fitted, [path]: true }));
  }

  isFitted(path: string): boolean {
    return this.fitted()[path] === true;
  }

  setHidden(path: string, isHidden: boolean): void {
    this.prefs.update(prefs => ({ ...prefs, [path]: { ...prefs[path], isHidden } }));
  }

  /** Forgets what the user changed about one column: it takes the declared width again. */
  reset(path: string): void {
    this.prefs.update(({ [path]: _gone, ...rest }) => rest);
    this.fitted.update(({ [path]: _gone, ...rest }) => rest);
  }
}

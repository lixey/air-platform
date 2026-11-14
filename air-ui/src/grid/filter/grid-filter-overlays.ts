import type { GridColumnDto, OverlayDto } from '../../core/generated/contracts';
import { singlePanelLayout } from '../../panels/single-panel-layout';

/** The key of the column filter's layout, and the stem of its overlay's key. */
const GRID_FILTER_OVERLAY = 'grid.columnFilter';

/** The key of the date range menu's layout, and the stem of its overlay's key. */
const GRID_DATE_RANGE_OVERLAY = 'grid.dateRange';

/** A menu hanging from the header's button, whose one panel is the column's filter overlay. */
export function gridColumnFilterOverlay(column: GridColumnDto): OverlayDto {
  return {
    key: `${GRID_FILTER_OVERLAY}.${column.path ?? ''}`,
    presentation: 'menu',
    layout: singlePanelLayout(GRID_FILTER_OVERLAY, { kind: 'GridColumnFilterOverlay', column }),
  };
}

/** A menu hanging from a date header, whose one panel is the column's date range menu. */
export function gridDateRangeOverlay(column: GridColumnDto): OverlayDto {
  return {
    key: `${GRID_DATE_RANGE_OVERLAY}.${column.path ?? ''}`,
    presentation: 'menu',
    layout: singlePanelLayout(GRID_DATE_RANGE_OVERLAY, { kind: 'GridDateRangeOverlay', column }),
  };
}

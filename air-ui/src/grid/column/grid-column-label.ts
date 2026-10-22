import type { GridColumnDto } from '../../core/generated/contracts';
import type { GridCellContext } from '../cell/grid-cell-context';

/** The label a column's header draws: the server's label, or the column's path where it sent none. */
export function columnLabelOf(column: Pick<GridColumnDto, 'path' | 'label'>): string {
  return column.label ?? column.path ?? '';
}

/** The tooltip title of a column's header: the server's title if provided, otherwise the column's label. */
export function columnTitleOf(column: Pick<GridColumnDto, 'path' | 'label' | 'title'>): string {
  return column.title ?? columnLabelOf(column);
}

/** The label a cell of a header row draws: the header cell's own, or its column's where the cell stands for none. */
export function headerLabelOf(cell: Pick<GridCellContext, 'header' | 'column'>): string {
  return cell.header?.label ?? columnLabelOf(cell.column);
}

/** The tooltip title a cell of a header row draws: the header cell's title, or its column's title. */
export function headerTitleOf(cell: Pick<GridCellContext, 'header' | 'column'>): string {
  return cell.header?.title ?? cell.header?.label ?? columnTitleOf(cell.column);
}

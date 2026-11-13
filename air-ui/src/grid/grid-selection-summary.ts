import type { GridColumnDto, RowDto, SummaryKind } from '../core/generated/contracts';
import { formatCellValue, formatCount } from './cell/grid-cell-format';
import { cellValueAt, rowColorAt } from './grid-row';

export interface GridSelectionSummaryChip {
  readonly key: string;
  readonly symbol: string;
  readonly label: string | null;
  readonly hue: string;
  readonly text: string;
}

const SUMMARY_SYMBOLS: Record<SummaryKind, string> = {
  sum: 'Σ',
  avg: 'x̄',
  count: '#',
  min: '↓',
  max: '↑',
};

function isNonEmptyCellValue(val: unknown): boolean {
  if (val === null || val === undefined || val === '') {
    return false;
  }
  if (typeof val === 'object' && 'id' in val) {
    const id = (val as { id?: unknown }).id;
    return typeof id === 'number' ? id > 0 : id !== null && id !== undefined && id !== '';
  }
  return true;
}

function formatAverageValue(value: number, col: GridColumnDto, locale: string, zone: string): string {
  if (col.cell.kind === 'GridCellNumber' && col.cell.decimals === 0 && !Number.isInteger(value)) {
    return new Intl.NumberFormat(locale, {
      useGrouping: col.cell.isGrouped ?? true,
      minimumFractionDigits: 1,
      maximumFractionDigits: 2,
    }).format(value);
  }
  return formatCellValue(value, col, locale, zone);
}

export function computeColumnSummary(
  col: GridColumnDto,
  rows: readonly RowDto[],
  locale: string,
  zone: string,
): GridSelectionSummaryChip | null {
  const summary = col.selectionSummary;
  const path = col.path;
  if (!summary || !path || rows.length === 0) {
    return null;
  }

  const colorPath = col.cell && 'colorPath' in col.cell ? col.cell.colorPath : null;
  const excludeColor = summary.excludeColor ?? null;
  const primaryRows = excludeColor ? rows.filter(r => rowColorAt(r, path, colorPath) !== excludeColor) : rows;
  const activeRows = primaryRows.length > 0 ? primaryRows : rows;
  const isExcludedOnly = excludeColor !== null && primaryRows.length === 0;
  const kind = summary.kind;
  const symbol = SUMMARY_SYMBOLS[kind];

  if (kind === 'count') {
    let count = 0;
    for (const r of activeRows) {
      if (isNonEmptyCellValue(cellValueAt(r, path))) {
        count += 1;
      }
    }
    let hue = 'slate';
    if (isExcludedOnly && excludeColor) {
      hue = excludeColor;
    } else if (count > 0) {
      hue = 'emerald';
    }
    return {
      key: path,
      symbol,
      label: col.label ?? null,
      hue,
      text: formatCount(count, locale),
    };
  }

  const nums: number[] = [];
  for (const r of activeRows) {
    const val = cellValueAt(r, path);
    if (val !== null && val !== undefined && val !== '') {
      const num = typeof val === 'number' ? val : Number(val);
      if (Number.isFinite(num)) {
        nums.push(num);
      }
    }
  }

  if (nums.length === 0 && kind !== 'sum') {
    return null;
  }

  let agg = 0;
  switch (kind) {
    case 'sum':
      agg = nums.reduce((acc, n) => acc + n, 0);
      break;
    case 'avg':
      agg = nums.reduce((acc, n) => acc + n, 0) / nums.length;
      break;
    case 'min':
      agg = nums.reduce((acc, n) => (n < acc ? n : acc), nums[0]);
      break;
    case 'max':
      agg = nums.reduce((acc, n) => (n > acc ? n : acc), nums[0]);
      break;
  }

  const formatted = kind === 'avg' ? formatAverageValue(agg, col, locale, zone) : formatCellValue(agg, col, locale, zone);
  const unit = summary.unit ? ` ${summary.unit}` : '';
  let hue = 'slate';
  if (isExcludedOnly && excludeColor) {
    hue = excludeColor;
  } else if (agg < 0) {
    hue = 'rose';
  } else if (agg > 0) {
    hue = 'emerald';
  }

  return {
    key: path,
    symbol,
    label: col.label ?? null,
    hue,
    text: `${formatted}${unit}`,
  };
}

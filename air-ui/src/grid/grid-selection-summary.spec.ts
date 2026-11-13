import { describe, expect, it } from 'vitest';
import type { GridColumnDto, RowDto } from '../core/generated/contracts';
import { computeColumnSummary } from './grid-selection-summary';

function makeCol(overrides: Partial<GridColumnDto>): GridColumnDto {
  return {
    path: 'amount',
    label: 'Amount',
    type: 'number',
    isSortable: true,
    isFilterable: true,
    isResizable: true,
    isHidden: false,
    isDisabled: false,
    isRequired: false,
    isSticky: false,
    isStickyRight: false,
    cell: { kind: 'GridCellNumber', decimals: 2, isGrouped: true },
    ...overrides,
  };
}

function makeRow(id: number, cells: Record<string, unknown>): RowDto {
  return { id, cells };
}

describe('computeColumnSummary', () => {
  it('computes sum and respects excludeColor unless only excluded rows are selected', () => {
    const col = makeCol({
      cell: { kind: 'GridCellMoney', decimals: 2, colorPath: 'amountColor' },
      selectionSummary: { kind: 'sum', unit: '₴', excludeColor: 'blue' },
    });
    const rows = [
      makeRow(1, { amount: 100, amountColor: 'emerald' }),
      makeRow(2, { amount: 250, amountColor: 'blue' }),
      makeRow(3, { amount: 50, amountColor: 'emerald' }),
    ];

    const mixed = computeColumnSummary(col, rows, 'en-US', 'UTC');
    expect(mixed).toEqual({
      key: 'amount',
      symbol: 'Σ',
      label: 'Amount',
      hue: 'emerald',
      text: '150.00 ₴',
    });

    const excludedOnly = computeColumnSummary(col, [rows[1]], 'en-US', 'UTC');
    expect(excludedOnly?.hue).toBe('blue');
    expect(excludedOnly?.text).toBe('250.00 ₴');
  });

  it('preserves fractional average on integer columns instead of rounding to zero decimals', () => {
    const col = makeCol({
      cell: { kind: 'GridCellNumber', decimals: 0, isGrouped: true },
      selectionSummary: { kind: 'avg' },
    });
    const rows = [makeRow(1, { amount: 1 }), makeRow(2, { amount: 2 })];

    const avg = computeColumnSummary(col, rows, 'en-US', 'UTC');
    expect(avg?.symbol).toBe('x̄');
    expect(avg?.text).toBe('1.5');
  });

  it('computes min and max without spread stack overflow', () => {
    const minCol = makeCol({ selectionSummary: { kind: 'min', unit: '$' } });
    const maxCol = makeCol({ selectionSummary: { kind: 'max', unit: '$' } });
    const rows = [makeRow(1, { amount: 12 }), makeRow(2, { amount: -4 }), makeRow(3, { amount: 30 })];

    expect(computeColumnSummary(minCol, rows, 'en-US', 'UTC')).toMatchObject({
      symbol: '↓',
      hue: 'rose',
      text: '-4.00 $',
    });
    expect(computeColumnSummary(maxCol, rows, 'en-US', 'UTC')).toMatchObject({
      symbol: '↑',
      hue: 'emerald',
      text: '30.00 $',
    });
  });

  it('counts non-empty text and reference cells accurately', () => {
    const textCol = makeCol({
      path: 'note',
      type: 'text',
      cell: { kind: 'GridCellText' },
      selectionSummary: { kind: 'count' },
    });
    const rows = [
      makeRow(1, { note: 'Urgent' }),
      makeRow(2, { note: '' }),
      makeRow(3, { note: null }),
      makeRow(4, { note: { id: 7, name: 'VIP' } }),
    ];

    const counted = computeColumnSummary(textCol, rows, 'en-US', 'UTC');
    expect(counted).toEqual({
      key: 'note',
      symbol: '#',
      label: 'Amount',
      hue: 'emerald',
      text: '2',
    });
  });
});

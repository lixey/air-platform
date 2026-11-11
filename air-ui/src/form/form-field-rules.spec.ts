import { describe, expect, it } from 'vitest';
import type { FilterNode, FilterOperator } from '../core/generated/contracts';
import { evaluateFilterNode } from './form-field-rules';

function rule(field: string, operator: FilterOperator, value?: unknown): FilterNode {
  return { kind: 'FilterRule', field, operator, value };
}

describe('evaluateFilterNode', () => {
  it('reads nested dot-paths and case-insensitive keys safely', () => {
    const values = {
      customer: { type: 'VIP', tierId: 3 },
      DocStatusId: 10,
    };

    expect(evaluateFilterNode(rule('customer.type', 'equals', 'vip'), values)).toBe(true);
    expect(evaluateFilterNode(rule('customer.tierId', 'greaterThanOrEqual', 3), values)).toBe(true);
    expect(evaluateFilterNode(rule('docStatusId', 'equals', 10), values)).toBe(true);
    expect(evaluateFilterNode(rule('customer.missing.deep', 'isNull'), values)).toBe(true);
  });

  it('treats null, undefined, empty string, and empty array as empty in isNull / isNotNull', () => {
    const values = {
      nullVal: null,
      emptyStr: '',
      emptyArr: [] as number[],
      zeroVal: 0,
      falseVal: false,
      filledArr: [2],
    };

    expect(evaluateFilterNode(rule('nullVal', 'isNull'), values)).toBe(true);
    expect(evaluateFilterNode(rule('missingVal', 'isNull'), values)).toBe(true);
    expect(evaluateFilterNode(rule('emptyStr', 'isNull'), values)).toBe(true);
    expect(evaluateFilterNode(rule('emptyArr', 'isNull'), values)).toBe(true);

    expect(evaluateFilterNode(rule('zeroVal', 'isNotNull'), values)).toBe(true);
    expect(evaluateFilterNode(rule('falseVal', 'isNotNull'), values)).toBe(true);
    expect(evaluateFilterNode(rule('filledArr', 'isNotNull'), values)).toBe(true);
  });

  it('prevents JS loose coercion bugs in equals / notEquals', () => {
    const values = {
      amount: 0,
      empty: '',
      nullableRef: null,
      active: false,
      code: '001',
      qtyText: '42.5',
    };

    expect(evaluateFilterNode(rule('amount', 'equals', ''), values)).toBe(false);
    expect(evaluateFilterNode(rule('empty', 'equals', 0), values)).toBe(false);
    expect(evaluateFilterNode(rule('nullableRef', 'equals', 0), values)).toBe(false);
    expect(evaluateFilterNode(rule('active', 'equals', 0), values)).toBe(false);
    expect(evaluateFilterNode(rule('active', 'equals', ''), values)).toBe(false);
    expect(evaluateFilterNode(rule('code', 'equals', '1'), values)).toBe(false);
    expect(evaluateFilterNode(rule('qtyText', 'equals', 42.5), values)).toBe(true);
    expect(evaluateFilterNode(rule('nullableRef', 'notEquals', 5), values)).toBe(true);
  });

  it('evaluates numeric and ISO temporal ordering without coercing null/empty to 0', () => {
    const values = {
      unsetNum: null,
      blankNum: '',
      qty: 12,
      dueDate: '2026-09-15',
      postedAt: '2026-09-15T14:30:00+02:00',
    };

    expect(evaluateFilterNode(rule('unsetNum', 'lessThan', 5), values)).toBe(false);
    expect(evaluateFilterNode(rule('unsetNum', 'lessThanOrEqual', 0), values)).toBe(false);
    expect(evaluateFilterNode(rule('blankNum', 'lessThan', 5), values)).toBe(false);
    expect(evaluateFilterNode(rule('qty', 'greaterThan', 10), values)).toBe(true);
    expect(evaluateFilterNode(rule('dueDate', 'greaterThan', '2026-09-01'), values)).toBe(true);
    expect(evaluateFilterNode(rule('dueDate', 'lessThan', '2026-09-01'), values)).toBe(false);
    expect(evaluateFilterNode(rule('postedAt', 'equals', '2026-09-15T12:30:00Z'), values)).toBe(true);
  });

  it('supports arrays and null guards in contains / notContains / in / notIn', () => {
    const values = {
      tagIds: [1, 3, 7],
      emptyRef: null,
      status: 2,
      title: 'Express Delivery',
    };

    expect(evaluateFilterNode(rule('tagIds', 'contains', 3), values)).toBe(true);
    expect(evaluateFilterNode(rule('tagIds', 'contains', '7'), values)).toBe(true);
    expect(evaluateFilterNode(rule('tagIds', 'notContains', 99), values)).toBe(true);
    expect(evaluateFilterNode(rule('tagIds', 'in', [5, 7]), values)).toBe(true);
    expect(evaluateFilterNode(rule('status', 'in', [1, 2, 4]), values)).toBe(true);
    expect(evaluateFilterNode(rule('emptyRef', 'in', [null, 0, 1]), values)).toBe(false);
    expect(evaluateFilterNode(rule('emptyRef', 'notIn', [1, 2]), values)).toBe(true);
    expect(evaluateFilterNode(rule('title', 'contains', 'express'), values)).toBe(true);
  });

  it('evaluates matches / notMatches safely and fails closed on unknown operators', () => {
    const values = {
      sku: 'AB-4092',
      empty: null,
    };

    expect(evaluateFilterNode(rule('sku', 'matches', '^[a-z]{2}-\\d{4}$'), values)).toBe(true);
    expect(evaluateFilterNode(rule('sku', 'notMatches', '^ZZ-'), values)).toBe(true);
    expect(evaluateFilterNode(rule('sku', 'matches', '[invalid-regex'), values)).toBe(false);
    expect(evaluateFilterNode(rule('empty', 'matches', '.*'), values)).toBe(false);
    expect(evaluateFilterNode(rule('sku', 'unknownCustomOperator' as FilterOperator, 'AB-4092'), values)).toBe(false);
  });
});

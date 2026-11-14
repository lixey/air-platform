import { signal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { formModel } from '../../tests/form-model';
import { navModel } from '../../tests/nav-model';
import type { GridModel } from '../core/generated/contracts';
import { GridNamedFilters } from './grid-named-filters';

function makeGridModel(): GridModel {
  return {
    kind: 'Grid',
    service: 'app/change',
    columns: [],
    headers: [],
    nav: {
      ...navModel('app/change.nav'),
      elements: [
        {
          kind: 'GridNavNamedFilterGroup',
          key: 'Operation',
          label: 'Operation',
          isInline: true,
          isRemembered: true,
          items: [
            { kind: 'GridNavNamedFilter', key: 'Operation.All', label: 'All', isSelected: true },
            { kind: 'GridNavNamedFilter', key: 'Operation.Added', label: 'Added', isSelected: false },
            { kind: 'GridNavNamedFilter', key: 'Operation.Modified', label: 'Modified', isSelected: false },
          ],
        },
        {
          kind: 'GridNavNamedFilter',
          key: 'HighValue',
          label: 'High value',
          isSelected: false,
          isRemembered: true,
        },
      ],
    },
    actionNav: navModel('app/change.actions'),
    pageSize: 50,
    isSelectable: false,
    isReadOnly: true,
    mutationTexts: formModel().mutationTexts,
  };
}

describe('GridNamedFilters case-insensitive PascalCase compatibility', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('matches legacy lowercase localStorage group and toggle keys to canonical PascalCase keys', () => {
    localStorage.setItem('ui:u7:named-filters:app/change', JSON.stringify(['operation.modified', 'highvalue']));

    const model = signal<GridModel | null>(makeGridModel());
    const resetBy = signal(0);
    const user = signal({ id: 7 });
    const filters = new GridNamedFilters({ model, resetBy, user });

    expect(filters.chosen()).toEqual(['Operation.Modified', 'HighValue']);
    expect(filters.startsApart()).toBe(true);
    expect(filters.isOn('operation.modified')).toBe(true);
    expect(filters.isOn('Operation.Modified')).toBe(true);
    expect(filters.isOn('highvalue')).toBe(true);
  });

  it('treats legacy lowercase default key as matching server default without spurious startsApart', () => {
    localStorage.setItem('ui:u7:named-filters:app/change', JSON.stringify(['operation.all']));

    const model = signal<GridModel | null>(makeGridModel());
    const resetBy = signal(0);
    const user = signal({ id: 7 });
    const filters = new GridNamedFilters({ model, resetBy, user });

    expect(filters.chosen()).toEqual(['Operation.All']);
    expect(filters.startsApart()).toBe(false);

    expect(filters.press('operation.added')).toBe(true);
    expect(filters.chosen()).toEqual(['Operation.Added']);
    expect(JSON.parse(localStorage.getItem('ui:u7:named-filters:app/change') ?? '[]')).toEqual(['Operation.Added']);
  });
});

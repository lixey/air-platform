import type { NavModel } from '../src/core/generated/contracts';

export function navModel(key = 'nav'): NavModel {
  return {
    kind: 'Nav',
    key,
    start: [],
    elements: [],
    end: [],
    more: [],
    options: {
      direction: 'row',
      overflow: 'wrap',
    },
  };
}

import axe from 'axe-core';
import { expect } from 'vitest';

export async function expectAccessible(element: Element): Promise<void> {
  const result = await axe.run(element, { rules: { 'color-contrast': { enabled: false } } });
  expect(result.violations).toEqual([]);
}

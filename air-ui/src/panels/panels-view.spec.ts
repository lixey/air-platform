import { describe, expect, it } from 'vitest';
import type { PanelLayoutDto } from '../core/generated/contracts';
import {
  buildView,
  collapsiblePanels,
  DEFAULT_HIDE_UNDER,
  findView,
  pageOf,
  pagesOf,
  type PanelViewOptions,
  staleChoices,
  togglesFor,
} from './panels-view';

function baseOptions(overrides: Partial<PanelViewOptions> = {}): PanelViewOptions {
  return {
    prefs: {},
    sizes: {},
    widths: {},
    active: {},
    isNarrow: false,
    ...overrides,
  };
}

describe('panels-view', () => {
  const rowLayout: PanelLayoutDto = {
    key: 'workspace',
    root: {
      key: 'root',
      direction: 'row',
      children: [
        { key: 'nav', label: 'Navigation', size: '280px' },
        { key: 'main', label: 'Main', size: '1fr' },
        { key: 'details', label: 'Details', size: '320px', isCollapsed: true },
      ],
    },
  };

  it('auto-hides row secondary panels below DEFAULT_HIDE_UNDER and respects initial isCollapsed above it', () => {
    const wide = buildView(rowLayout, baseOptions({ sizes: { '': 1400 } }));
    expect(findView(wide, 'nav')?.isCollapsed).toBe(false);
    expect(findView(wide, 'main')?.isFiller).toBe(true);
    expect(findView(wide, 'details')?.isCollapsed).toBe(true);

    const compact = buildView(rowLayout, baseOptions({ sizes: { '': DEFAULT_HIDE_UNDER - 20 } }));
    expect(findView(compact, 'nav')?.isCollapsed).toBe(true);
    expect(findView(compact, 'details')?.isCollapsed).toBe(true);
  });

  it('honors user prefs on the same side of the threshold and marks choices stale once crossed', () => {
    const prefs = {
      nav: { isCollapsed: false, at: 1100 },
      details: { isCollapsed: false, at: 1500 },
    };

    const atCompact = buildView(rowLayout, baseOptions({ prefs, sizes: { '': 1150 } }));
    expect(findView(atCompact, 'nav')?.isCollapsed).toBe(false);
    // `details` was opened at 1500 (>= 1280), so at 1150 (< 1280) that choice is stale and falls back to closed.
    expect(findView(atCompact, 'details')?.isCollapsed).toBe(true);
    expect(staleChoices(atCompact, prefs, { '': 1150 }, rowLayout)).toEqual(['details']);
  });

  it('forces unpinned secondary panels closed on narrow screens and pushes untouched panels aside when space is tight', () => {
    const narrow = buildView(
      rowLayout,
      baseOptions({
        isNarrow: true,
        prefs: { nav: { isCollapsed: false, at: 900 } },
        sizes: { '': 900 },
      }),
    );
    expect(findView(narrow, 'nav')?.isCollapsed).toBe(true);

    // At 750px with hideSecondaryUnder: 600, nav (280) + main (min 280) + details (320) + gaps (8) = 888 > 750.
    // Opening `details` explicitly keeps `details` open and pushes untouched `nav` aside first.
    const tightLayout: PanelLayoutDto = { ...rowLayout, hideSecondaryUnder: 600 };
    const tight = buildView(
      tightLayout,
      baseOptions({
        prefs: { details: { isCollapsed: false, at: 750 } },
        sizes: { '': 750 },
      }),
    );
    expect(findView(tight, 'details')?.isCollapsed).toBe(false);
    expect(findView(tight, 'nav')?.isCollapsed).toBe(true);
  });

  it('exposes collapsible toggles on the filling panel and projects exclusive/paged containers', () => {
    const wide = buildView(rowLayout, baseOptions({ sizes: { '': 1400 } }));
    expect(collapsiblePanels(wide)).toEqual([
      { path: 'nav', name: 'Navigation', isCollapsed: false, side: 'left' },
      { path: 'details', name: 'Details', isCollapsed: true, side: 'right' },
    ]);
    expect(togglesFor(wide, 'main').map(t => t.path)).toEqual(['nav', 'details']);
    expect(togglesFor(wide, 'nav')).toEqual([]);

    const exclusiveLayout: PanelLayoutDto = {
      key: 'tabs',
      root: {
        key: 'root',
        isExclusive: true,
        children: [
          { key: 'summary', label: 'Summary', isPinned: true, pageUnder: 900 },
          { key: 'lines', label: 'Lines' },
          { key: 'history', label: 'History' },
        ],
      },
    };

    const unpaged = buildView(exclusiveLayout, baseOptions({ widths: { '': 1200 }, active: { '': 'history' } }));
    expect(unpaged.isExclusive).toBe(true);
    expect(unpaged.isPaged).toBe(false);
    expect(findView(unpaged, 'summary')?.isCollapsed).toBe(false);
    expect(pagesOf(unpaged, '')).toEqual([
      { path: 'lines', name: 'Lines', isOpen: false },
      { path: 'history', name: 'History', isOpen: true },
    ]);
    expect(pageOf(unpaged, 'history')).toEqual({
      container: '',
      page: { path: 'history', name: 'History', isOpen: true },
    });

    // Narrower than `pageUnder: 900`: pinned `summary` demotes to a page and the container becomes paged.
    const paged = buildView(exclusiveLayout, baseOptions({ widths: { '': 700 }, active: { '': null } }));
    expect(paged.isPaged).toBe(true);
    expect(pagesOf(paged, '').map(p => [p.path, p.isOpen])).toEqual([
      ['summary', true],
      ['lines', false],
      ['history', false],
    ]);
  });
});

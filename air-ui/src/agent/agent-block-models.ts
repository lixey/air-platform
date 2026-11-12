import { type EnvironmentProviders, InjectionToken, makeEnvironmentProviders, type Type } from '@angular/core';
import type {
  OpenTargetDto,
  ReportChartDto,
  ReportColumnDto,
  ReportKpiDto,
  ReportPieSliceDto,
  ReportSeriesDto,
  ReportTableDto,
  RowDto,
  UiColor,
} from '../core/generated/contracts';
import { asRowUiColor, isRowDto } from '../grid/grid-row';

export interface AgentMarkdownBlock {
  readonly id: string;
  readonly kind: 'markdown';
  readonly content: string;
}

export interface AgentChartBlock {
  readonly id: string;
  readonly kind: 'chart';
  readonly chart: ReportChartDto;
}

export interface AgentTableBlock {
  readonly id: string;
  readonly kind: 'table';
  readonly table: ReportTableDto;
}

export interface AgentKpiBlock {
  readonly id: string;
  readonly kind: 'kpi';
  readonly kpis: readonly ReportKpiDto[];
}

export interface AgentHtmlBlock {
  readonly id: string;
  readonly kind: 'html';
  readonly html: string;
}

export interface AgentCustomBlock {
  readonly id: string;
  readonly kind: string;
  readonly data: unknown;
  readonly component?: Type<unknown>;
}

export type AgentContentBlock = AgentMarkdownBlock | AgentChartBlock | AgentTableBlock | AgentKpiBlock | AgentHtmlBlock | AgentCustomBlock;

/**
 * A modular plugin that teaches the AI chat how to parse and render a structured block tag or code fence.
 */
export interface AgentBlockPlugin {
  readonly kind: string;
  /** XML-style tag name without angle brackets, e.g. `ui_chart`. */
  readonly tag: string;
  /** Optional markdown code-fence language identifiers, e.g. `['ui-chart', 'chart']`. */
  readonly fences?: readonly string[];
  /** Optional custom Angular component for rendering `AgentCustomBlock`. */
  readonly component?: Type<unknown>;
  /** Parses the raw inner content of the tag/fence into one or more `AgentContentBlock` items. */
  readonly parse: (rawInner: string, idPrefix: string) => readonly AgentContentBlock[];
}

function tryParseJson(raw: string): unknown {
  const trimmed = raw
    .trim()
    .replace(/^```(?:json)?\s*/i, '')
    .replace(/\s*```$/i, '')
    .trim();
  if (!trimmed) {
    return null;
  }
  try {
    return JSON.parse(trimmed);
  } catch {
    try {
      const cleaned = trimmed.replace(/,\s*([}\]])/g, '$1');
      return JSON.parse(cleaned);
    } catch {
      return null;
    }
  }
}

function normalizeChart(raw: Record<string, unknown>, id: string): ReportChartDto | null {
  const kindRaw = String(raw['kind'] ?? raw['type'] ?? 'bar').toLowerCase();
  const validKinds = ['bar', 'line', 'area', 'stacked-bar', 'horizontal-bar', 'donut', 'pie'] as const;
  const kind = (validKinds.find(k => k === kindRaw) ?? 'bar') as ReportChartDto['kind'];
  const title = String(raw['title'] ?? raw['name'] ?? '');

  const rawSlices = (raw['slices'] ?? (kind === 'donut' || kind === 'pie' ? raw['data'] : null)) as unknown[] | null;
  const slices: ReportPieSliceDto[] | undefined = Array.isArray(rawSlices)
    ? rawSlices
        .filter((s): s is Record<string, unknown> => !!s && typeof s === 'object')
        .map(s => ({
          name: String(s['name'] ?? s['label'] ?? s['category'] ?? ''),
          value: Number(s['value'] ?? s['amount'] ?? 0) || 0,
          color: typeof s['color'] === 'string' ? (s['color'] as ReportPieSliceDto['color']) : null,
        }))
    : undefined;

  const rawCategories = (raw['categories'] ?? raw['labels'] ?? raw['xAxis']) as unknown[] | null;
  const categories = Array.isArray(rawCategories) ? rawCategories.map(c => String(c ?? '')) : undefined;

  const rawSeries = raw['series'] as unknown[] | null;
  let series: ReportSeriesDto[] | undefined;
  if (Array.isArray(rawSeries)) {
    series = rawSeries
      .filter((s): s is Record<string, unknown> => !!s && typeof s === 'object')
      .map(s => ({
        name: String(s['name'] ?? s['label'] ?? title ?? ''),
        kind: (validKinds.find(k => k === String(s['kind'] ?? s['type'] ?? kind).toLowerCase()) ?? kind) as ReportSeriesDto['kind'],
        color: typeof s['color'] === 'string' ? (s['color'] as ReportSeriesDto['color']) : null,
        stack: typeof s['stack'] === 'string' ? s['stack'] : null,
        data: Array.isArray(s['data'] ?? s['values']) ? ((s['data'] ?? s['values']) as unknown[]).map(v => Number(v) || 0) : [],
      }));
  } else if (Array.isArray(raw['values'] ?? raw['data']) && kind !== 'donut' && kind !== 'pie') {
    const arr = (raw['values'] ?? raw['data']) as unknown[];
    if (arr.every(v => typeof v === 'number' || typeof v === 'string')) {
      series = [
        {
          name: title,
          kind,
          data: arr.map(v => Number(v) || 0),
        },
      ];
    }
  }

  if (!slices?.length && !series?.length) {
    return null;
  }

  return {
    key: String(raw['key'] ?? id),
    title,
    subtitle: typeof raw['subtitle'] === 'string' ? raw['subtitle'] : null,
    kind,
    currency: typeof raw['currency'] === 'string' ? raw['currency'] : null,
    height: typeof raw['height'] === 'number' ? raw['height'] : 240,
    categories,
    series,
    slices,
  };
}

function normalizeTable(raw: Record<string, unknown>): ReportTableDto | null {
  const rawCols = (raw['columns'] ?? raw['headers']) as unknown[] | null;
  const rawRows = (raw['rows'] ?? raw['data']) as unknown[] | null;
  if (!Array.isArray(rawCols) || !Array.isArray(rawRows) || rawCols.length === 0) {
    return null;
  }

  const columns: ReportColumnDto[] = rawCols.map((c, idx) => {
    if (typeof c === 'string') {
      return { key: `c${idx}`, label: c, kind: idx === 0 ? 'text' : 'number' };
    }
    const obj = (c ?? {}) as Record<string, unknown>;
    const kRaw = String(obj['kind'] ?? obj['type'] ?? (idx === 0 ? 'text' : 'number')).toLowerCase();
    const kind: ReportColumnDto['kind'] =
      kRaw === 'money' || kRaw === 'number' || kRaw === 'percent' || kRaw === 'badge' || kRaw === 'date' || kRaw === 'datetime'
        ? kRaw
        : 'text';
    return {
      key: String(obj['key'] ?? obj['field'] ?? `c${idx}`),
      label: String(obj['label'] ?? obj['title'] ?? obj['name'] ?? obj['key'] ?? ''),
      kind,
      currency: typeof obj['currency'] === 'string' ? obj['currency'] : null,
      decimals: typeof obj['decimals'] === 'number' ? obj['decimals'] : null,
      width: typeof obj['width'] === 'number' ? obj['width'] : null,
    };
  });

  const mapRow = (r: unknown): RowDto => {
    if (Array.isArray(r)) {
      const cells: Record<string, unknown> = {};
      columns.forEach((col, i) => {
        cells[col.key] = r[i];
      });
      return { cells };
    }
    if (isRowDto(r)) {
      return r;
    }
    if (!r || typeof r !== 'object') {
      return { cells: {} };
    }
    const obj = r as Record<string, unknown>;
    const cells: Record<string, unknown> = {};
    const colors: Record<string, UiColor> = {};
    const currencies: Record<string, string> = {};
    const defaultCurrency = typeof obj['currency'] === 'string' ? obj['currency'] : null;

    for (const col of columns) {
      if (col.key in obj) {
        cells[col.key] = obj[col.key];
      }
      const colColor = asRowUiColor(typeof obj[`${col.key}Color`] === 'string' ? (obj[`${col.key}Color`] as string) : null);
      if (colColor) {
        colors[col.key] = colColor;
      }
      const colCurrency = obj[`${col.key}Currency`] ?? (col.kind === 'money' ? defaultCurrency : null);
      if (typeof colCurrency === 'string' && colCurrency) {
        currencies[col.key] = colCurrency;
      }
    }

    const rowColor = asRowUiColor(
      typeof obj['rowColor'] === 'string' ? obj['rowColor'] : typeof obj['color'] === 'string' ? obj['color'] : null,
    );
    const extractRefreshServices = (val: unknown): string[] | undefined => {
      if (!Array.isArray(val)) {
        return undefined;
      }
      const list = val.filter((s): s is string => typeof s === 'string' && s.length > 0);
      return list.length > 0 ? list : undefined;
    };

    let open: OpenTargetDto | null = null;
    if (obj['open'] && typeof obj['open'] === 'object') {
      const rawOpen = obj['open'] as Record<string, unknown>;
      const service = rawOpen['service'];
      const id = typeof rawOpen['id'] === 'number' ? rawOpen['id'] : Number(rawOpen['id']);
      if (typeof service === 'string' && service && Number.isFinite(id) && id > 0) {
        const refreshServices = extractRefreshServices(rawOpen['refreshServices']);
        open = { service, id, ...(refreshServices ? { refreshServices } : {}) };
      }
    } else if (typeof obj['openForm'] === 'string' && obj['openForm']) {
      const id = typeof obj['openId'] === 'number' ? obj['openId'] : Number(obj['openId']);
      if (Number.isFinite(id) && id > 0) {
        const refreshServices = extractRefreshServices(obj['refreshServices']);
        open = { service: obj['openForm'], id, ...(refreshServices ? { refreshServices } : {}) };
      }
    }

    return {
      ...(typeof obj['id'] === 'number' ? { id: obj['id'] } : {}),
      cells,
      ...(rowColor ? { color: rowColor } : {}),
      ...(Object.keys(colors).length > 0 ? { colors } : {}),
      ...(Object.keys(currencies).length > 0 ? { currencies } : {}),
      ...(open ? { open } : {}),
    };
  };

  const rows = rawRows.map(mapRow);
  const rawBottom = (raw['bottomRows'] ?? raw['footer'] ?? raw['totals']) as unknown[] | null;
  const bottomRows = Array.isArray(rawBottom) ? rawBottom.map(mapRow) : null;

  return {
    title: typeof raw['title'] === 'string' ? raw['title'] : null,
    columns,
    rows,
    bottomRows,
  };
}

function normalizeKpis(raw: unknown): ReportKpiDto[] {
  const list = Array.isArray(raw)
    ? raw
    : raw && typeof raw === 'object' && Array.isArray((raw as Record<string, unknown>)['kpis'] ?? (raw as Record<string, unknown>)['items'])
      ? (((raw as Record<string, unknown>)['kpis'] ?? (raw as Record<string, unknown>)['items']) as unknown[])
      : raw && typeof raw === 'object'
        ? [raw]
        : [];

  return list
    .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
    .map(item => ({
      label: String(item['label'] ?? item['title'] ?? item['name'] ?? ''),
      value: Number(item['value'] ?? item['amount'] ?? 0) || 0,
      currency: typeof item['currency'] === 'string' ? item['currency'] : null,
      isPercent: Boolean(item['isPercent'] ?? item['percent']),
      isCount: Boolean(item['isCount'] ?? item['count']),
      subtitle: typeof item['subtitle'] === 'string' ? item['subtitle'] : null,
      color: typeof item['color'] === 'string' ? (item['color'] as ReportKpiDto['color']) : null,
    }))
    .filter(k => k.label.length > 0);
}

export const DEFAULT_AGENT_BLOCK_PLUGINS: readonly AgentBlockPlugin[] = [
  {
    kind: 'chart',
    tag: 'ui_chart',
    fences: ['ui-chart', 'ui_chart', 'chart'],
    parse: (raw, idPrefix) => {
      const parsed = tryParseJson(raw);
      if (!parsed) {
        return [];
      }
      const items = Array.isArray(parsed) ? parsed : [parsed];
      const blocks: AgentChartBlock[] = [];
      items.forEach((item, idx) => {
        if (item && typeof item === 'object') {
          const chart = normalizeChart(item as Record<string, unknown>, `${idPrefix}-c${idx}`);
          if (chart) {
            blocks.push({ id: `${idPrefix}-c${idx}`, kind: 'chart', chart });
          }
        }
      });
      return blocks;
    },
  },
  {
    kind: 'table',
    tag: 'ui_table',
    fences: ['ui-table', 'ui_table'],
    parse: (raw, idPrefix) => {
      const parsed = tryParseJson(raw);
      if (!parsed || typeof parsed !== 'object') {
        return [];
      }
      const items = Array.isArray(parsed) ? parsed : [parsed];
      const blocks: AgentTableBlock[] = [];
      items.forEach((item, idx) => {
        if (item && typeof item === 'object') {
          const table = normalizeTable(item as Record<string, unknown>);
          if (table) {
            blocks.push({ id: `${idPrefix}-t${idx}`, kind: 'table', table });
          }
        }
      });
      return blocks;
    },
  },
  {
    kind: 'kpi',
    tag: 'ui_kpi',
    fences: ['ui-kpi', 'ui_kpi', 'kpi'],
    parse: (raw, idPrefix) => {
      const parsed = tryParseJson(raw);
      const kpis = normalizeKpis(parsed);
      return kpis.length > 0 ? [{ id: `${idPrefix}-kpi`, kind: 'kpi', kpis }] : [];
    },
  },
  {
    kind: 'html',
    tag: 'ui_html',
    fences: ['ui-html', 'ui_html'],
    parse: (raw, idPrefix) => {
      const html = raw.trim();
      return html ? [{ id: `${idPrefix}-html`, kind: 'html', html }] : [];
    },
  },
];

export const AGENT_BLOCK_PLUGINS = new InjectionToken<readonly AgentBlockPlugin[]>('AGENT_BLOCK_PLUGINS', {
  providedIn: 'root',
  factory: () => DEFAULT_AGENT_BLOCK_PLUGINS,
});

/**
 * Registers additional structured block plugins for the built-in AI assistant chat.
 */
export function provideAgentBlocks(plugins: readonly AgentBlockPlugin[]): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: AGENT_BLOCK_PLUGINS,
      useValue: [...DEFAULT_AGENT_BLOCK_PLUGINS, ...plugins],
    },
  ]);
}

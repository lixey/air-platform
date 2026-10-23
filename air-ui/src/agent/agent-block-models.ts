import { type EnvironmentProviders, InjectionToken, makeEnvironmentProviders, type Type } from '@angular/core';
import type {
  ReportChartDto,
  ReportColumnDto,
  ReportKpiDto,
  ReportPieSliceDto,
  ReportSeriesDto,
  ReportTableDto,
} from '../core/generated/contracts';

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
  /** XML-style tag name without angle brackets, e.g. `air_chart`. */
  readonly tag: string;
  /** Optional markdown code-fence language identifiers, e.g. `['air-chart', 'chart']`. */
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
    const kind: ReportColumnDto['kind'] = kRaw === 'money' || kRaw === 'number' || kRaw === 'percent' ? kRaw : 'text';
    return {
      key: String(obj['key'] ?? obj['field'] ?? `c${idx}`),
      label: String(obj['label'] ?? obj['title'] ?? obj['name'] ?? obj['key'] ?? ''),
      kind,
      currency: typeof obj['currency'] === 'string' ? obj['currency'] : null,
      width: typeof obj['width'] === 'number' ? obj['width'] : null,
    };
  });

  const mapRow = (r: unknown): Record<string, unknown> => {
    if (Array.isArray(r)) {
      const out: Record<string, unknown> = {};
      columns.forEach((col, i) => {
        out[col.key] = r[i];
      });
      return out;
    }
    return r && typeof r === 'object' ? (r as Record<string, unknown>) : {};
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
    tag: 'air_chart',
    fences: ['air-chart', 'air_chart', 'chart'],
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
    tag: 'air_table',
    fences: ['air-table', 'air_table'],
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
    tag: 'air_kpi',
    fences: ['air-kpi', 'air_kpi', 'kpi'],
    parse: (raw, idPrefix) => {
      const parsed = tryParseJson(raw);
      const kpis = normalizeKpis(parsed);
      return kpis.length > 0 ? [{ id: `${idPrefix}-kpi`, kind: 'kpi', kpis }] : [];
    },
  },
  {
    kind: 'html',
    tag: 'air_html',
    fences: ['air-html', 'air_html'],
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

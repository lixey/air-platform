import { Service } from '@angular/core';
import type * as EChartsCore from 'echarts/core';
import type { EChartsCoreOption, EChartsType } from 'echarts/core';

export interface ChartHandle {
  setOptions(opts: EChartsCoreOption, notMerge?: boolean): void;
  dispose(): void;
  getInstance(): EChartsType | null;
}

const LIGHT_HUES: Readonly<Record<string, string>> = {
  primary: '#2563eb',
  brand: '#2563eb',
  emerald: '#10b981',
  green: '#16a34a',
  teal: '#14b8a6',
  cyan: '#06b6d4',
  sky: '#0ea5e9',
  blue: '#3b82f6',
  indigo: '#6366f1',
  violet: '#8b5cf6',
  purple: '#a855f7',
  fuchsia: '#d946ef',
  pink: '#ec4899',
  rose: '#f43f5e',
  red: '#ef4444',
  orange: '#f97316',
  amber: '#f59e0b',
  yellow: '#eab308',
  lime: '#84cc16',
  slate: '#64748b',
  gray: '#6b7280',
  zinc: '#71717a',
  neutral: '#737373',
  stone: '#78716c',
};

const DARK_HUES: Readonly<Record<string, string>> = {
  primary: '#60a5fa',
  brand: '#60a5fa',
  emerald: '#34d399',
  green: '#4ade80',
  teal: '#2dd4bf',
  cyan: '#22d3ee',
  sky: '#38bdf8',
  blue: '#60a5fa',
  indigo: '#818cf8',
  violet: '#a78bfa',
  purple: '#c084fc',
  fuchsia: '#e879f9',
  pink: '#f472b6',
  rose: '#fb7185',
  red: '#f87171',
  orange: '#fb923c',
  amber: '#fbbf24',
  yellow: '#facc15',
  lime: '#a3e635',
  slate: '#94a3b8',
  gray: '#9ca3af',
  zinc: '#a1a1aa',
  neutral: '#a3a3a3',
  stone: '#a8a29e',
};

export const DEFAULT_PALETTE_KEYS: readonly string[] = [
  'blue',
  'emerald',
  'amber',
  'rose',
  'violet',
  'cyan',
  'orange',
  'indigo',
  'teal',
  'pink',
  'lime',
  'sky',
];

export interface ChartSemanticColors {
  readonly text: string;
  readonly muted: string;
  readonly splitLine: string;
  readonly surface: string;
  readonly border: string;
}

export function oklchToRgb(raw: string): string | null {
  const match = /^oklch\(\s*([^\s/]+)\s+([^\s/]+)\s+([^\s/)]+)(?:\s*\/\s*([^\s)]+))?\s*\)$/i.exec(raw.trim());
  if (!match) {
    return null;
  }
  const [, lStr, cStr, hStr, aStr] = match;
  const l = lStr.endsWith('%') ? Number.parseFloat(lStr) / 100 : Number.parseFloat(lStr);
  const c = cStr.endsWith('%') ? (Number.parseFloat(cStr) / 100) * 0.4 : Number.parseFloat(cStr);
  const hLower = hStr.toLowerCase();
  const h =
    hLower === 'none'
      ? 0
      : hLower.endsWith('rad')
        ? (Number.parseFloat(hLower) * 180) / Math.PI
        : hLower.endsWith('turn')
          ? Number.parseFloat(hLower) * 360
          : hLower.endsWith('grad')
            ? Number.parseFloat(hLower) * 0.9
            : Number.parseFloat(hLower);
  const alpha = !aStr || aStr === 'none' ? 1 : aStr.endsWith('%') ? Number.parseFloat(aStr) / 100 : Number.parseFloat(aStr);
  if (!Number.isFinite(l) || !Number.isFinite(c) || !Number.isFinite(h) || !Number.isFinite(alpha)) {
    return null;
  }

  const hRad = (h * Math.PI) / 180;
  const a = c * Math.cos(hRad);
  const b = c * Math.sin(hRad);

  const l1 = l + 0.3963377774 * a + 0.2158037573 * b;
  const m1 = l - 0.1055613458 * a - 0.0638541728 * b;
  const s1 = l - 0.0894841775 * a - 1.291485548 * b;

  const l3 = l1 * l1 * l1;
  const m3 = m1 * m1 * m1;
  const s3 = s1 * s1 * s1;

  const toByte = (lin: number): number => {
    const clamped = Math.min(1, Math.max(0, lin));
    const srgb = clamped <= 0.0031308 ? 12.92 * clamped : 1.055 * Math.pow(clamped, 1 / 2.4) - 0.055;
    return Math.round(srgb * 255);
  };

  const r = toByte(4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3);
  const g = toByte(-1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3);
  const bl = toByte(-0.0041960863 * l3 - 0.7034186147 * m3 + 1.707614701 * s3);
  const clampedAlpha = Math.min(1, Math.max(0, alpha));
  return clampedAlpha < 1 ? `rgba(${r}, ${g}, ${bl}, ${Number(clampedAlpha.toFixed(3))})` : `rgb(${r}, ${g}, ${bl})`;
}

export function resolveCssColor(style: CSSStyleDeclaration | null | undefined, varName: string, fallback: string): string {
  let raw = style?.getPropertyValue(varName)?.trim() ?? '';
  for (let depth = 0; depth < 3 && raw.startsWith('var('); depth++) {
    const inner = /^var\(\s*(--[\w-]+)\s*\)$/.exec(raw)?.[1];
    raw = inner ? (style?.getPropertyValue(inner)?.trim() ?? '') : '';
  }
  if (!raw || raw.startsWith('var(') || raw.startsWith('light-dark(') || raw.startsWith('color-mix(')) {
    return fallback;
  }
  if (raw.toLowerCase().startsWith('oklch(')) {
    return oklchToRgb(raw) ?? fallback;
  }
  return raw;
}

export function resolveChartSemanticColors(isDark: boolean, style?: CSSStyleDeclaration | null): ChartSemanticColors {
  return {
    text: resolveCssColor(style, '--panel-ink', isDark ? '#cbd5e1' : '#334155'),
    muted: resolveCssColor(style, '--panel-muted', isDark ? '#94a3b8' : '#64748b'),
    splitLine: resolveCssColor(style, '--panel-line', isDark ? '#1e293b' : '#f1f5f9'),
    surface: resolveCssColor(style, '--panel-surface', isDark ? '#0f172a' : '#ffffff'),
    border: resolveCssColor(style, '--panel-field-edge', isDark ? '#334155' : '#e2e8f0'),
  };
}

export function resolveHueHex(
  color: string | null | undefined,
  isDark: boolean,
  fallbackIndex = 0,
  style?: CSSStyleDeclaration | null,
): string {
  const palette = isDark ? DARK_HUES : LIGHT_HUES;
  const key = color?.toLowerCase();
  const knownKey = key && key in palette ? key : DEFAULT_PALETTE_KEYS[fallbackIndex % DEFAULT_PALETTE_KEYS.length];
  const fallback = palette[knownKey] ?? '#3b82f6';
  const cssHue = knownKey === 'primary' ? 'brand' : knownKey;
  const step = isDark ? '400' : '500';
  return resolveCssColor(style, `--color-${cssHue}-${step}`, fallback);
}

@Service()
export class ChartService {
  private echartsReady: Promise<{ echarts: typeof EChartsCore }> | null = null;

  private isBrowser(): boolean {
    return typeof window !== 'undefined' && typeof document !== 'undefined';
  }

  private ensureEcharts(): Promise<{ echarts: typeof EChartsCore }> {
    if (!this.echartsReady) {
      this.echartsReady = Promise.all([
        import('echarts/core'),
        import('echarts/charts'),
        import('echarts/components'),
        import('echarts/renderers'),
      ])
        .then(([echarts, charts, components, renderers]) => {
          echarts.use([
            charts.BarChart,
            charts.LineChart,
            charts.PieChart,
            components.TitleComponent,
            components.TooltipComponent,
            components.LegendComponent,
            components.GridComponent,
            renderers.CanvasRenderer,
          ]);
          return { echarts };
        })
        .catch((err: unknown) => {
          this.echartsReady = null;
          throw err;
        });
    }
    return this.echartsReady;
  }

  initLazy(container: HTMLElement, initialOptions: EChartsCoreOption): ChartHandle {
    let chartInstance: EChartsType | null = null;
    let resizeObserver: ResizeObserver | null = null;
    let intersectionObserver: IntersectionObserver | null = null;
    let disposed = false;
    let initializing = false;
    let lastOptions = initialOptions;
    let rafId = 0;

    const setup = async () => {
      if (disposed || initializing || chartInstance || !container.isConnected) {
        return;
      }
      initializing = true;
      intersectionObserver?.disconnect();
      intersectionObserver = null;
      try {
        const { echarts } = await this.ensureEcharts();
        if (disposed || chartInstance || !container.isConnected) {
          return;
        }
        const initChart = () => {
          if (disposed || chartInstance || !container.isConnected) {
            return;
          }
          if (container.clientWidth <= 0 || container.clientHeight <= 0) {
            return;
          }
          chartInstance = echarts.init(container, undefined, {
            renderer: 'canvas',
            devicePixelRatio: this.isBrowser() ? window.devicePixelRatio || 1 : 1,
          });
          chartInstance.setOption(lastOptions, {
            notMerge: true,
            lazyUpdate: true,
          });
        };
        initChart();
        if (this.isBrowser() && 'ResizeObserver' in window) {
          resizeObserver?.disconnect();
          resizeObserver = new ResizeObserver(() => {
            if (disposed) {
              return;
            }
            if (!chartInstance) {
              initChart();
              return;
            }
            cancelAnimationFrame(rafId);
            rafId = requestAnimationFrame(() => {
              if (!disposed) {
                chartInstance?.resize();
              }
            });
          });
          resizeObserver.observe(container);
        }
      } catch (err) {
        console.error('ECharts init error', err);
      } finally {
        initializing = false;
      }
    };

    if (this.isBrowser() && 'IntersectionObserver' in window) {
      intersectionObserver = new IntersectionObserver(
        entries => {
          if (entries.some(e => e.isIntersecting)) {
            void setup();
          }
        },
        { root: null, rootMargin: '0px', threshold: 0.01 },
      );
      intersectionObserver.observe(container);
    } else if (this.isBrowser()) {
      void setup();
    }

    return {
      setOptions: (opts, notMerge = true) => {
        lastOptions = opts;
        if (chartInstance && !disposed) {
          chartInstance.setOption(lastOptions, {
            notMerge,
            lazyUpdate: true,
          });
        }
      },
      dispose: () => {
        if (disposed) {
          return;
        }
        disposed = true;
        if (rafId) {
          cancelAnimationFrame(rafId);
          rafId = 0;
        }
        resizeObserver?.disconnect();
        resizeObserver = null;
        intersectionObserver?.disconnect();
        intersectionObserver = null;
        chartInstance?.dispose();
        chartInstance = null;
      },
      getInstance: () => chartInstance,
    };
  }
}

import { Service } from '@angular/core';
import type * as EChartsCore from 'echarts/core';
import type { EChartsCoreOption, EChartsType } from 'echarts/core';

export interface ChartHandle {
  setOptions(opts: EChartsCoreOption, notMerge?: boolean): void;
  dispose(): void;
  getInstance(): EChartsType | null;
}

const LIGHT_HUES: Readonly<Record<string, string>> = {
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
};

const DARK_HUES: Readonly<Record<string, string>> = {
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

export function resolveHueHex(color: string | null | undefined, isDark: boolean, fallbackIndex = 0): string {
  const palette = isDark ? DARK_HUES : LIGHT_HUES;
  const key = color?.toLowerCase();
  if (key && palette[key]) {
    return palette[key];
  }
  const fallbackKey = DEFAULT_PALETTE_KEYS[fallbackIndex % DEFAULT_PALETTE_KEYS.length];
  return palette[fallbackKey] ?? '#3b82f6';
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
    let lastOptions = initialOptions;
    let rafId = 0;

    const setup = async () => {
      if (disposed || chartInstance || !container.isConnected) {
        return;
      }
      try {
        const { echarts } = await this.ensureEcharts();
        if (disposed || chartInstance || !container.isConnected) {
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
        if (this.isBrowser() && 'ResizeObserver' in window) {
          resizeObserver = new ResizeObserver(() => {
            if (!chartInstance || disposed) {
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

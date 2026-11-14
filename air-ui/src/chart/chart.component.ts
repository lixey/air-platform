import { afterRenderEffect, Component, DestroyRef, ElementRef, inject, input, viewChild } from '@angular/core';
import type { EChartsCoreOption } from 'echarts/core';
import { LocaleService } from '../app/locale.service';
import type { ReportChartDto } from '../core/generated/contracts';
import { formatCellAmount, formatCellNumber, moneyFormatOf } from '../grid/cell/grid-cell-format';
import { ThemeService } from '../theme/theme.service';
import { type ChartHandle, ChartService, resolveChartSemanticColors, resolveHueHex } from './chart.service';

@Component({
  selector: 'ui-chart',
  template: `
    <div class="flex flex-col w-full min-w-0 p-4 gap-2 bg-surface border border-line rounded-xl">
      <div class="flex items-baseline justify-between gap-2 shrink-0">
        <h3 class="m-0 text-sm font-semibold text-ink truncate">{{ chart().title }}</h3>
        @if (chart().subtitle; as subtitle) {
          <span class="text-xs text-muted truncate">{{ subtitle }}</span>
        }
      </div>
      <div #canvasHost class="w-full" [style.height.px]="chart().height ?? 280"></div>
    </div>
  `,
  host: { class: 'block min-w-0' },
})
export class ChartComponent {
  private readonly hostEl = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly chartService = inject(ChartService);
  private readonly theme = inject(ThemeService);
  private readonly locale = inject(LocaleService).current;
  private readonly destroyRef = inject(DestroyRef);

  readonly chart = input.required<ReportChartDto>();

  private readonly canvasHost = viewChild.required<ElementRef<HTMLDivElement>>('canvasHost');
  private handle: ChartHandle | null = null;

  private readonly syncChart = afterRenderEffect(() => {
    const chart = this.chart();
    const isDark = this.theme.isDark();
    const locale = this.locale();
    const style = typeof getComputedStyle === 'function' ? getComputedStyle(this.hostEl) : null;
    const opts = buildEChartsOptions(chart, isDark, locale, style);
    if (!this.handle) {
      this.handle = this.chartService.initLazy(this.canvasHost().nativeElement, opts);
    } else {
      this.handle.setOptions(opts, true);
    }
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.handle?.dispose();
      this.handle = null;
    });
  }
}

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function formatChartNumber(val: number, locale: string, currency?: string | null): string {
  const decimals = Number.isInteger(val) ? 0 : 2;
  if (currency) {
    try {
      return formatCellAmount(val, locale, moneyFormatOf({ decimals, currency }));
    } catch {
      return `${formatCellNumber(val, locale, decimals, true)} ${currency}`;
    }
  }
  return formatCellNumber(val, locale, Number.isInteger(val) ? 0 : null, true);
}

function formatCompactAxisValue(val: number, locale: string, currency?: string | null): string {
  const abs = Math.abs(val);
  if (currency) {
    try {
      const digits = abs >= 1_000_000 ? 1 : abs >= 1 || val === 0 ? 0 : 2;
      return new Intl.NumberFormat(locale, {
        style: 'currency',
        currency,
        notation: abs >= 1_000 ? 'compact' : 'standard',
        maximumFractionDigits: digits,
      }).format(val);
    } catch {
      // Fall through to compact numeric + code suffix if currency is non-ISO
    }
  }
  let base: string;
  if (abs >= 1_000_000) {
    base = `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(val / 1_000_000)}M`;
  } else if (abs >= 1_000) {
    base = `${new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(val / 1_000)}k`;
  } else {
    base = new Intl.NumberFormat(locale, { maximumFractionDigits: Number.isInteger(val) ? 0 : 2 }).format(val);
  }
  return currency ? `${base} ${currency}` : base;
}

export function buildEChartsOptions(
  chart: ReportChartDto,
  isDark: boolean,
  locale: string,
  style?: CSSStyleDeclaration | null,
): EChartsCoreOption {
  const {
    text: textColor,
    muted: mutedColor,
    splitLine: splitLineColor,
    surface: surfaceColor,
    border: borderColor,
  } = resolveChartSemanticColors(isDark, style);
  const currency = chart.currency;
  const formatValue = (val: number): string => formatChartNumber(val, locale, currency);
  const compactFormat = (val: number): string => formatCompactAxisValue(val, locale, currency);

  if (chart.kind === 'donut' || chart.kind === 'pie') {
    const slices = chart.slices ?? [];
    const data = slices.map((s, idx) => ({
      name: s.name,
      value: s.value,
      itemStyle: { color: resolveHueHex(s.color, isDark, idx, style) },
    }));

    return {
      tooltip: {
        trigger: 'item',
        backgroundColor: surfaceColor,
        borderColor,
        textStyle: { color: textColor, fontSize: 12 },
        formatter: (params: unknown) => {
          const p = params as { name: string; value: number; percent: number; marker: string };
          return `${p.marker} <b>${escapeHtml(String(p.name ?? ''))}</b><br/>${formatValue(p.value)} (${p.percent?.toFixed(1) ?? 0}%)`;
        },
      },
      legend: {
        orient: 'vertical',
        right: 0,
        top: 'middle',
        type: 'scroll',
        textStyle: { color: textColor, fontSize: 12 },
      },
      series: [
        {
          type: 'pie',
          radius: chart.kind === 'donut' ? ['46%', '76%'] : '72%',
          center: ['36%', '50%'],
          avoidLabelOverlap: true,
          itemStyle: {
            borderRadius: 5,
            borderColor: surfaceColor,
            borderWidth: 2,
          },
          label: { show: false },
          emphasis: {
            label: {
              show: true,
              fontSize: 12,
              fontWeight: 'bold',
              color: textColor,
            },
          },
          data,
        },
      ],
    };
  }

  const categories = chart.categories ?? [];
  const rawSeries = chart.series ?? [];
  const isHorizontal = chart.kind === 'horizontal-bar';

  const series = rawSeries.map((s, idx) => {
    const color = resolveHueHex(s.color, isDark, idx, style);
    const isLine = s.kind === 'line' || s.kind === 'area' || chart.kind === 'line' || chart.kind === 'area';
    const isArea = s.kind === 'area' || chart.kind === 'area';
    const stack = s.stack || (chart.kind === 'stacked-bar' ? 'total' : undefined);

    if (isLine) {
      return {
        name: s.name,
        type: 'line',
        smooth: true,
        symbolSize: 6,
        data: s.data,
        itemStyle: { color },
        lineStyle: { width: 2.5, color },
        areaStyle: isArea
          ? {
              color,
              opacity: isDark ? 0.22 : 0.16,
            }
          : undefined,
      };
    }

    return {
      name: s.name,
      type: 'bar',
      stack,
      barMaxWidth: 36,
      data: s.data,
      itemStyle: {
        color,
        borderRadius: isHorizontal ? [0, 4, 4, 0] : stack ? 0 : [4, 4, 0, 0],
      },
    };
  });

  const categoryAxis = {
    type: 'category',
    data: categories,
    inverse: isHorizontal,
    axisLine: { lineStyle: { color: borderColor } },
    axisTick: { show: false },
    axisLabel: {
      color: mutedColor,
      fontSize: 11,
      width: isHorizontal ? 130 : undefined,
      overflow: isHorizontal ? 'truncate' : undefined,
    },
  };

  const valueAxis = {
    type: 'value',
    splitLine: { lineStyle: { color: splitLineColor } },
    axisLabel: {
      color: mutedColor,
      fontSize: 11,
      formatter: (v: number) => compactFormat(v),
    },
  };

  return {
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      backgroundColor: surfaceColor,
      borderColor,
      textStyle: { color: textColor, fontSize: 12 },
      valueFormatter: (v: unknown) => (typeof v === 'number' ? formatValue(v) : String(v ?? '')),
    },
    legend: {
      show: series.length > 1,
      top: 0,
      right: 0,
      textStyle: { color: textColor, fontSize: 12 },
    },
    grid: {
      top: series.length > 1 ? 32 : 12,
      left: isHorizontal ? 12 : 8,
      right: 16,
      bottom: 8,
      containLabel: true,
    },
    xAxis: isHorizontal ? valueAxis : categoryAxis,
    yAxis: isHorizontal ? categoryAxis : valueAxis,
    series,
  };
}

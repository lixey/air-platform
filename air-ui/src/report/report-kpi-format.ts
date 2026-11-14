import { declaredDigits } from '../controls/amount-text';
import type { ReportKpiDto } from '../core/generated/contracts';
import { formatCellAmount, formatCellNumber, moneyFormatOf, percentFormatOf } from '../grid/cell/grid-cell-format';

const REPORT_NUMBERS = new Map<string, Intl.NumberFormat>();
const MAX_REPORT_FRACTION_DIGITS = 6;

export function formatReportNumber(value: unknown, locale: string, decimals?: number | null): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const num = typeof value === 'number' ? value : Number(value);
  if (!Number.isFinite(num)) {
    return String(value);
  }
  const digits = declaredDigits(decimals);
  const key = `${locale}|${digits ?? `<=${MAX_REPORT_FRACTION_DIGITS}`}`;
  let formatter = REPORT_NUMBERS.get(key);
  if (!formatter) {
    formatter = new Intl.NumberFormat(
      locale,
      digits === null
        ? { maximumFractionDigits: MAX_REPORT_FRACTION_DIGITS }
        : { minimumFractionDigits: digits, maximumFractionDigits: digits },
    );
    REPORT_NUMBERS.set(key, formatter);
  }
  return formatter.format(num);
}

export function formatReportKpi(kpi: ReportKpiDto, locale: string): string {
  if (kpi.isPercent) {
    const hasExtraPrecision = Math.abs(Math.round(kpi.value * 10) - kpi.value * 10) > 1e-9;
    return formatCellAmount(kpi.value, locale, percentFormatOf({ decimals: hasExtraPrecision ? null : 1 }));
  }
  if (kpi.currency) {
    const decimals = kpi.isCount || Number.isInteger(kpi.value) ? 0 : 2;
    try {
      return formatCellAmount(kpi.value, locale, moneyFormatOf({ decimals, currency: kpi.currency }));
    } catch {
      return `${formatCellNumber(kpi.value, locale, decimals, true)} ${kpi.currency}`;
    }
  }
  return formatReportNumber(kpi.value, locale, kpi.isCount || Number.isInteger(kpi.value) ? 0 : null);
}

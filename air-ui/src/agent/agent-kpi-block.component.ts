import { Component, inject, input } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import type { ReportKpiDto } from '../core/generated/contracts';
import { formatReportKpi } from '../report/report-kpi-format';
import { asAgentUiColor } from './agent-ui-helpers';

@Component({
  selector: 'ui-agent-kpi-block',
  template: `
    <div class="grid grid-cols-2 gap-2 w-full min-w-0">
      @for (kpi of kpis(); track kpi.label) {
        <div class="bg-surface border border-line rounded-xl p-3 flex flex-col gap-0.5 min-w-0">
          <span class="text-xs font-medium text-muted truncate">{{ kpi.label }}</span>
          <span class="text-lg font-semibold tabular-nums truncate" [class]="hueTextClass(kpi.color)">
            {{ formatKpi(kpi) }}
          </span>
          @if (kpi.subtitle; as sub) {
            <span class="text-[11px] text-muted truncate">{{ sub }}</span>
          }
        </div>
      }
    </div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentKpiBlockComponent {
  private readonly locale = inject(LocaleService).current;
  readonly kpis = input.required<readonly ReportKpiDto[]>();

  protected formatKpi(kpi: ReportKpiDto): string {
    return formatReportKpi(kpi, this.locale());
  }

  protected hueTextClass(color: string | null | undefined): string {
    const hue = asAgentUiColor(color);
    return hue ? `hue-${hue} text-(--hue-ink)` : 'text-ink';
  }
}

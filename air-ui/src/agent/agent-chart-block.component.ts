import { Component, inject, Injector, input } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { ChartComponent } from '../chart/chart.component';
import type { ReportChartDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { OverlayService } from '../overlay/overlay.service';
import { singlePanelLayout } from '../panels/single-panel-layout';

@Component({
  selector: 'ui-agent-chart-block',
  imports: [ChartComponent, BtnComponent],
  template: `
    <div class="relative w-full min-w-0">
      <ui-chart [chart]="chart()" />
      @if (expandable()) {
        <div class="absolute top-2.5 right-2.5 z-10">
          <ui-btn size="small" variant="ghost" icon="fullscreen" [title]="texts.of('FitToScreen')" (click)="openExpanded()" />
        </div>
      }
    </div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentChartBlockComponent {
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  protected readonly texts = inject(TextsService);

  readonly chart = input.required<ReportChartDto>();
  readonly expandable = input(true);

  protected openExpanded(): void {
    const c = this.chart();
    const expandedChart: ReportChartDto = { ...c, height: 440 };
    this.overlay.open(
      {
        key: `agent.chart.${c.key}`,
        presentation: 'dialog',
        width: '920px',
        closesOnEscape: true,
        closesOnOutside: true,
        layout: singlePanelLayout('main', {
          kind: 'AgentBlockOverlay',
          title: c.title || this.texts.of('AgentTitle'),
          chart: expandedChart,
        }),
      },
      null,
      this.injector,
    );
  }
}

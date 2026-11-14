import { Component, computed, inject, Injector, input } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { ReportTableDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { OverlayService } from '../overlay/overlay.service';
import { singlePanelLayout } from '../panels/single-panel-layout';
import { ReportTableComponent } from '../report/report-table.component';

@Component({
  selector: 'ui-agent-table-block',
  imports: [BtnComponent, ReportTableComponent],
  template: `
    @if (reportTable(); as rt) {
      <ui-report-table [table]="rt">
        @if (expandable()) {
          <ui-btn actions size="small" variant="ghost" icon="fullscreen" [title]="texts.of('FitToScreen')" (click)="openExpanded()" />
        }
      </ui-report-table>
    }
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentTableBlockComponent {
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  protected readonly texts = inject(TextsService);

  readonly table = input.required<ReportTableDto>();
  readonly expandable = input(true);

  protected readonly reportTable = computed<ReportTableDto>(() => {
    const t = this.table();
    return {
      ...t,
      title: t.title || this.texts.of('RowCount', t.rows.length),
      compact: t.compact ?? true,
      sortable: t.sortable ?? true,
      maxHeight: this.expandable() ? (t.maxHeight ?? 384) : null,
    };
  });

  protected openExpanded(): void {
    const t = this.table();
    this.overlay.open(
      {
        key: 'agent.table.expanded',
        presentation: 'dialog',
        width: '960px',
        closesOnEscape: true,
        closesOnOutside: true,
        layout: singlePanelLayout('main', {
          kind: 'AgentBlockOverlay',
          title: t.title || this.texts.of('AgentTitle'),
          table: t,
        }),
      },
      null,
      this.injector,
    );
  }
}

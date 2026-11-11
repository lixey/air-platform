import { Component, inject, input } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { NavComponent } from '../nav/nav.component';
import { OverlayRef } from '../overlay/overlay-ref';
import type { AgentBlockOverlayModel } from './agent-block-overlay.model';
import { AgentChartBlockComponent } from './agent-chart-block.component';
import { AgentTableBlockComponent } from './agent-table-block.component';

@Component({
  selector: 'ui-agent-block-overlay',
  imports: [NavComponent, BtnComponent, AgentChartBlockComponent, AgentTableBlockComponent],
  template: `
    @if (model(); as m) {
      <ui-nav class="shrink-0 border-b border-line bg-surface-2">
        <ng-container ngProjectAs="[start]">
          <span class="pl-2 font-semibold text-ink truncate">{{ m.title }}</span>
        </ng-container>
        <ng-container ngProjectAs="[end]">
          <ui-btn variant="ghost" icon="close" [title]="texts.of('Close')" (click)="close()" />
        </ng-container>
      </ui-nav>
      <div class="flex-1 min-h-0 overflow-auto p-4 bg-field">
        @if (m.chart; as c) {
          <ui-agent-chart-block [chart]="c" [expandable]="false" />
        }
        @if (m.table; as t) {
          <ui-agent-table-block [table]="t" [expandable]="false" />
        }
      </div>
    }
  `,
  host: { class: 'flex flex-col w-full h-full min-w-0 min-h-0 overflow-hidden bg-surface' },
})
export class AgentBlockOverlayComponent {
  protected readonly texts = inject(TextsService);
  private readonly overlayRef = inject(OverlayRef, { optional: true });

  readonly model = input<AgentBlockOverlayModel>();

  protected close(): void {
    this.overlayRef?.close();
  }
}

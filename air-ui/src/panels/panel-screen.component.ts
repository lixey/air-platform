import { Component, inject, input } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { PanelScreenModel } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { SCREEN_ADDRESS, SCREEN_HOST } from '../screen/screen-address';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { screenLoader } from '../screen/screen-loader';
import { PANEL_DENSITY } from './panels-tokens';
import { PanelsComponent } from './panels.component';

/**
 * An embedded standalone screen inside a composite multi-panel layout: loads its own layout and first data from
 * `/api/{service}/get-data` while sharing the outer screen's lifetime and mutation scope so sibling panels react to
 * committed changes together.
 */
@Component({
  selector: 'ui-panel-screen',
  imports: [BtnComponent, PanelsComponent],
  providers: [
    { provide: SCREEN_HOST, useFactory: () => inject(PanelScreenComponent).screen },
    { provide: SCREEN_ADDRESS, useFactory: () => inject(PanelScreenComponent).address },
    { provide: PANEL_DENSITY, useValue: 'flush' },
  ],
  template: `
    @if (screen.layout(); as layout) {
      <ui-panels [layout]="layout" [data]="screen.data()" class="flex-1 min-h-0" />
    } @else if (screen.error()) {
      <div role="alert" class="p-3 flex items-center gap-3 text-sm text-warning">
        {{ screen.errorTitle() }}
        <ui-btn [label]="texts.of('Retry')" (click)="screen.reload()" />
      </div>
    }
  `,
  host: { class: 'flex flex-col flex-1 w-full h-full min-w-0 min-h-0 overflow-hidden' },
})
export class PanelScreenComponent {
  readonly model = input.required<PanelScreenModel>();
  protected readonly texts = inject(TextsService);
  private readonly parentLifetime = inject(SCREEN_LIFETIME, { optional: true });

  readonly screen = screenLoader(() => {
    const service = this.model().service;
    return service ? { service } : null;
  }, this.parentLifetime);

  readonly address = this.screen.address;
}

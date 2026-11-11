import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { PanelScreenModel, ScreenDto } from '../core/generated/contracts';
import { problemOf } from '../core/problem';
import { TextsService } from '../core/texts.service';
import { SCREEN_ADDRESS, SCREEN_HOST, type ScreenAddress, type ScreenHost } from '../screen/screen-address';
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
    { provide: SCREEN_HOST, useExisting: PanelScreenComponent },
    { provide: SCREEN_ADDRESS, useFactory: () => inject(PanelScreenComponent).address },
    { provide: PANEL_DENSITY, useValue: 'flush' },
  ],
  template: `
    @if (screen.layout(); as layout) {
      <ui-panels [layout]="layout" [data]="screen.data()" class="flex-1 min-h-0" />
    } @else if (screen.error()) {
      <div role="alert" class="p-3 flex items-center gap-3 text-sm text-warning">
        {{ errorTitle() }}
        <ui-btn [label]="texts.of('Retry')" (click)="screen.reload()" />
      </div>
    }
  `,
  host: { class: 'flex flex-col flex-1 w-full h-full min-w-0 min-h-0 overflow-hidden' },
})
export class PanelScreenComponent implements ScreenHost {
  readonly model = input.required<PanelScreenModel>();
  protected readonly texts = inject(TextsService);
  private readonly parentLifetime = inject(SCREEN_LIFETIME, { optional: true });

  private readonly patch = linkedSignal<string, Record<string, unknown>>({
    source: () => this.model().service,
    computation: () => ({}),
  });

  readonly address = computed<ScreenAddress | null>(() => {
    const service = this.model().service;
    if (!service) {
      return null;
    }
    const patch = this.patch();
    const params = Object.keys(patch).length > 0 ? patch : undefined;
    return { service, params };
  });

  protected readonly screen = screenLoader(this.address, this.parentLifetime);

  protected readonly errorTitle = computed(() => problemOf(this.screen.error())?.title ?? this.texts.of('ScreenLoadFailed'));

  reload(patch: Record<string, unknown>): void {
    this.patch.update(current => ({ ...current, ...patch }));
  }

  show(screen: ScreenDto): void {
    this.screen.show(screen);
  }
}

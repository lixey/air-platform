import { Component, inject, input } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { Lifetime } from '../core/lifetime';
import { TextsService } from '../core/texts.service';
import { PanelsComponent } from '../panels/panels.component';
import { SCREEN_ADDRESS, SCREEN_HOST } from '../screen/screen-address';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { screenLoader } from '../screen/screen-loader';
import { OverlayRef } from './overlay-ref';

/**
 * What an overlay shows when its model names a screen instead of carrying a layout: the
 * screen loaded by address, drawn as its panels, with the same loader a route uses. Mounted
 * through the overlay's injector, so the panels inside reach the overlay as any do.
 */
@Component({
  selector: 'ui-overlay-screen',
  imports: [BtnComponent, PanelsComponent],
  providers: [
    { provide: SCREEN_ADDRESS, useFactory: () => inject(OverlayScreenComponent).address },
    { provide: SCREEN_HOST, useFactory: () => inject(OverlayScreenComponent).screen },
    {
      // The screen's lifetime: the opener's, and only while this overlay is open
      provide: SCREEN_LIFETIME,
      useFactory: () => {
        const ref = inject(OverlayRef);
        return (inject(SCREEN_LIFETIME, { optional: true, skipSelf: true }) ?? new Lifetime()).child(() => !ref.isClosed);
      },
    },
  ],
  template: `
    @if (screen.error()) {
      <div role="alert" class="m-3 p-4 flex items-center gap-3 text-sm text-warning">
        {{ screen.errorTitle() }}
        <ui-btn [label]="texts.of('Retry')" (click)="screen.reload()" />
      </div>
    } @else if (layout(); as l) {
      <ui-panels [layout]="l" [data]="screen.data()" />
    }
  `,
  host: {
    class: 'flex flex-col grow shrink overflow-hidden min-h-0 min-w-0',
  },
})
export class OverlayScreenComponent {
  protected readonly texts = inject(TextsService);
  private readonly ref = inject(OverlayRef);

  /** Route segment of the screen service, as in `/api/{service}/get-data`. */
  readonly service = input.required<string>();

  /** What that screen's `get-data` is sent. */
  readonly params = input<unknown>();

  readonly screen = screenLoader(() => ({ service: this.service(), params: this.params() }));
  readonly address = this.screen.address;
  protected readonly layout = this.screen.layout;

  constructor() {
    // The card's size is the host's to resolve, from the ref: it reads the screen's ask where the screen holds it
    this.ref.ask(this.screen.asked);
  }
}

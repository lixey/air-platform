import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { ScreenDto } from '../core/generated/contracts';
import { Lifetime } from '../core/lifetime';
import { TextsService } from '../core/texts.service';
import { PanelsComponent } from '../panels/panels.component';
import { SCREEN_ADDRESS, SCREEN_HOST, type ScreenHost } from '../screen/screen-address';
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
    { provide: SCREEN_HOST, useExisting: OverlayScreenComponent },
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
        {{ texts.of('ScreenLoadFailed') }}
        <ui-btn [label]="texts.of('Retry')" (click)="screen.reload()" />
      </div>
    } @else if (layout(); as l) {
      <ui-panels [layout]="l" [data]="screen.data()" [attr.inert]="screen.isStale() || null" />
    }
  `,
  host: {
    class: 'flex flex-col grow shrink overflow-hidden min-h-0 min-w-0',
    '[attr.aria-busy]': 'screen.isLoading() || null',
  },
})
export class OverlayScreenComponent implements ScreenHost {
  protected readonly texts = inject(TextsService);
  private readonly ref = inject(OverlayRef);

  /** The parameters a panel asked to load the screen with; another service or other params start without them. */
  private readonly patch = linkedSignal<readonly unknown[], Record<string, unknown>>({
    source: () => [this.service(), this.params()],
    computation: () => ({}),
  });

  /** Route segment of the screen service, as in `/api/{service}/get-data`. */
  readonly service = input.required<string>();

  /** What that screen's `get-data` is sent. */
  readonly params = input<unknown>();

  readonly address = computed(() => {
    const baseParams = typeof this.params() === 'object' && this.params() !== null ? (this.params() as Record<string, unknown>) : {};
    const patch = this.patch();
    const effectiveParams = Object.keys(patch).length > 0 ? { ...baseParams, ...patch } : this.params();

    return { service: this.service(), params: effectiveParams };
  });
  protected readonly screen = screenLoader(this.address);
  protected readonly layout = this.screen.layout;

  constructor() {
    // The card's size is the host's to resolve, from the ref: it reads the screen's ask where the screen holds it
    this.ref.ask(this.screen.asked);
  }

  reload(patch: Record<string, unknown>): void {
    this.patch.update(current => ({ ...current, ...patch }));
  }

  show(screen: ScreenDto): void {
    this.screen.show(screen);
  }
}

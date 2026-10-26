import { Component, computed, DestroyRef, effect, inject, input, untracked } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { FormChildScreenModel } from '../core/generated/contracts';
import { Lifetime } from '../core/lifetime';
import { problemOf } from '../core/problem';
import { TextsService } from '../core/texts.service';
import { NavComponent } from '../nav/nav.component';
import { OverlayRef } from '../overlay/overlay-ref';
import { exclusiveContainerOf, PANEL_DENSITY, PANEL_NODE } from '../panels/panels-tokens';
import { PanelsComponent } from '../panels/panels.component';
import { SCREEN_ADDRESS, SCREEN_OVERLAY_OWNER, type ScreenAddress } from '../screen/screen-address';
import { ScreenLifecycle } from '../screen/screen-lifecycle';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { screenLoader } from '../screen/screen-loader';
import { ScreenMutations } from '../screen/screen-mutations';
import { FormChildScope } from './form-child-scope';
import { injectForm } from './inject-form';

/**
 * A child screen declared beside its parent form: requests its layout and first data
 * with its parent's confirmed ID, owns its child overlays, tells the form of its layout of
 * every change in its scope, and participates in the parent overlay's close
 * and route leave. Above the screen it draws the strip of page tabs its
 * model carries, while the column around it is not paged — paged, the form's nav carries
 * the tabs. The strip keeps the height of its buttons however short the page; the screen
 * under it scrolls.
 */
@Component({
  selector: 'ui-form-child-screen',
  imports: [BtnComponent, NavComponent, PanelsComponent],
  providers: [
    { provide: SCREEN_LIFETIME, useFactory: () => inject(FormChildScreenComponent).scope.lifetime },
    { provide: ScreenMutations, useFactory: () => inject(FormChildScreenComponent).scope.mutations },
    { provide: SCREEN_OVERLAY_OWNER, useFactory: () => inject(FormChildScreenComponent).scope },
    { provide: SCREEN_ADDRESS, useFactory: () => inject(FormChildScreenComponent).address },
    { provide: PANEL_DENSITY, useValue: 'flush' },
  ],
  template: `
    @if (tabs(); as tabs) {
      <ui-nav [model]="tabs" class="shrink-0 border-b border-line" data-child-tabs />
    }
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
export class FormChildScreenComponent {
  readonly model = input.required<FormChildScreenModel>();
  protected readonly texts = inject(TextsService);
  private readonly container = exclusiveContainerOf(inject(PANEL_NODE, { optional: true }));

  /** The strip of page tabs, while the model carries one and the column around the page is not paged. */
  protected readonly tabs = computed(() => {
    const tabs = this.model().tabs;
    return tabs && this.container?.view().isPaged !== true ? tabs : null;
  });

  /** The lifetime of the form's screen; the child page's own is its child, a new epoch for every parent record. */
  private readonly parent = inject(SCREEN_LIFETIME, { optional: true, skipSelf: true }) ?? new Lifetime();
  readonly scope = new FormChildScope(this.parent);

  /** The child's screen for the parent record the model names. */
  readonly address = computed<ScreenAddress>(() => {
    const model = this.model();
    return { service: model.service, params: { parentService: model.parentService, parentId: model.parentId } };
  });

  /** Loads under the parent's lifetime: the scope's reset for a new parent record does not drop the load it starts. */
  protected readonly screen = screenLoader(this.address, this.parent);

  protected readonly errorTitle = computed(() => problemOf(this.screen.error())?.title ?? this.texts.of('ScreenLoadFailed'));

  constructor() {
    const overlayRef = inject(OverlayRef, { optional: true });
    const lifecycle = inject(ScreenLifecycle, { optional: true });

    const unregisterOverlay = overlayRef?.registerCloseParticipant(request => this.scope.prepareClose(request));
    const overlayClosedSub = overlayRef?.closed.subscribe(() => this.scope.reset());
    const unregisterLifecycle = lifecycle?.registerOwner({
      closeParticipant: request => this.scope.prepareClose(request),
      needsProtection: () => this.scope.hasOverlays(),
      reset: () => this.scope.reset(),
    });

    // A new model, which a read in place does not hand a page it leaves as it is, closes what the child opened for the
    // former one and starts the child's new epoch
    effect(() => {
      this.model();
      untracked(() => this.scope.reset());
    });

    // The form of the layout reads its screen again for a change under this page
    const form = injectForm();
    const unregisterChange = this.scope.onChange(() => form()?.refresh());

    inject(DestroyRef).onDestroy(() => {
      unregisterChange();
      overlayClosedSub?.unsubscribe();
      unregisterOverlay?.();
      unregisterLifecycle?.();
      this.scope.destroy();
    });
  }
}

import { Component, computed, DestroyRef, inject, Injector, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { NavMenuModel, OverlayModelDto } from '../../core/generated/contracts';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { OverlayService } from '../../overlay/overlay.service';
import { singlePanelLayout } from '../../panels/single-panel-layout';

/** The key of the menu's layout, and the stem of its overlay's key. */
const NAV_MENU_OVERLAY = 'nav.menu';

/**
 * A button that opens a menu of its model's elements, hung from itself and named by its own label: the button with the
 * look of its model and a caret, saying whether the menu is open and which card it controls. The menu's body is the
 * More menu's, so each element is drawn by the component its kind names and the arrows, `Home` and `End` move through
 * them; it is mounted through this element's injector, so an element here reaches what it would reach in the nav. An
 * element whose work outlasts a click closes the menu as it starts (`NAV_MENU`). The menu closes with its nav. A model
 * without elements draws nothing, unless it is disabled: then its button stands, disabled, with the title that says why
 * the menu has nothing to offer, and opens nothing.
 */
@Component({
  selector: 'ui-nav-menu',
  imports: [BtnComponent, UiIconDirective],
  template: `
    @if (isShown()) {
      <ui-btn
        [model]="model()"
        aria-haspopup="dialog"
        [attr.aria-expanded]="menu.isOpen()"
        [attr.aria-controls]="menu.id()"
        (click)="open($event)"
        (keydown.arrowdown)="open($event)"
      >
        <i icon="chevron-bottom" aria-hidden="true"></i>
      </ui-btn>
    }
  `,
})
export class NavMenuComponent {
  private readonly injector = inject(Injector);
  /** The menu, what the button says of it; it closes with this element. */
  protected readonly menu = inject(OverlayService).menu();

  readonly model = model<NavMenuModel | null>();

  /** The menu's elements. */
  protected readonly elements = computed(() => this.model()?.elements ?? []);

  /** The button stands while there is an element, and while a disabled model says why there is none. */
  protected readonly isShown = computed(() => this.elements().length > 0 || !!this.model()?.isDisabled);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.menu.close());
  }

  /**
   * Opens the menu from the button, over the model's elements as they are now; an open menu stays as it is. A click
   * reaches the element although its button is inert, so a disabled model returns at once.
   */
  protected open(event: Event): void {
    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const model = this.model();
    if (model?.isDisabled) {
      return;
    }
    event.preventDefault();
    if (!anchor || !model || this.menu.isOpen()) {
      return;
    }

    const overlay: OverlayModelDto = {
      key: `${NAV_MENU_OVERLAY}.${model.key}`,
      presentation: 'menu',
      layout: singlePanelLayout(NAV_MENU_OVERLAY, { kind: 'NavMenuBody', elements: this.elements(), closesOnAction: true }),
    };
    this.menu.open(overlay, anchor, this.injector, model.label ?? model.title ?? '');
  }
}

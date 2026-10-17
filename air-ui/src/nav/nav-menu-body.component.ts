import { Component, inject, input } from '@angular/core';
import { focusables } from '../overlay/overlay-focus';
import { OverlayRef } from '../overlay/overlay-ref';
import type { NavMenuBodyModel } from '../ui-components';
import { NavElementOutletComponent } from './nav-element-outlet.component';
import { NAV_MENU, type NavMenuHandle } from './nav-menu';

/** The keys that move focus through the menu, and where each one sends it from the focused item, `-1` for none. */
const MOVES: Readonly<Record<string, (index: number, count: number) => number>> = {
  ArrowDown: (index, count) => (index + 1) % count,
  ArrowUp: (index, count) => (index <= 0 ? count : index) - 1,
  Home: () => 0,
  End: (_, count) => count - 1,
};

/**
 * The body of a nav's menu, the More menu's and a `NavMenu`'s: the elements it was opened over in declared order, a
 * column, each drawn by the component its kind names, as in the nav itself. The menu is mounted through its opener's
 * injector, so an element here reaches what it would reach in the nav — a grid's service, for its Export.
 *
 * `ArrowDown` and `ArrowUp` move focus to the next and the previous item, wrapping at either end, `Home` and `End` to
 * the first and the last; `Tab` stays inside the card and `Escape` closes it, as in every overlay. It gives itself to
 * its elements as `NAV_MENU`: an element whose work outlasts a click asks it to close, and it closes when its opener
 * said so. A `NavMenu` says so; the More menu does not and stays open while an element acts.
 */
@Component({
  selector: 'ui-nav-menu-body',
  imports: [NavElementOutletComponent],
  providers: [{ provide: NAV_MENU, useExisting: NavMenuBodyComponent }],
  template: `
    <div class="flex flex-col items-stretch gap-1 p-(--ui-panel-gap) [&_ui-btn]:w-full">
      @for (element of model()?.elements ?? []; track element.key) {
        <ui-nav-element-outlet [element]="element" />
      }
    </div>
  `,
  host: { '(keydown)': 'move($event)' },
})
export class NavMenuBodyComponent implements NavMenuHandle {
  private readonly ref = inject(OverlayRef);

  /** The menu's elements as they were when it was opened, and whether an element that asks closes it. */
  readonly model = input<NavMenuBodyModel | null>();

  /** Closes the menu for an element that asks, when the opener said so. */
  close(): void {
    if (this.model()?.closesOnAction) {
      void this.ref.dismiss();
    }
  }

  /** A key of `MOVES` sends focus to another item; any other key, and every key typed into a field, is the item's own. */
  protected move(event: KeyboardEvent): void {
    const next = MOVES[event.key];
    const list = event.currentTarget instanceof HTMLElement ? focusables(event.currentTarget) : [];
    if (!next || list.length === 0 || isField(event.target)) {
      return;
    }
    event.preventDefault();
    const index = list.findIndex(item => item.contains(document.activeElement));
    list[next(index, list.length)]?.focus();
  }
}

/** Whether a key went to a field, whose arrows, `Home` and `End` move its caret or its value. */
function isField(target: EventTarget | null): boolean {
  return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement;
}

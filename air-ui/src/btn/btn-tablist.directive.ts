import { computed, Directive, input, type Signal, signal } from '@angular/core';

/** What a tab tells the tablist around it: its element, whether it is selected and whether it takes no input now. */
export interface BtnTab {
  readonly element: HTMLElement;
  readonly isSelected: Signal<boolean>;
  readonly isInert: Signal<boolean>;
}

/** Tabs in the order a reader meets them, whatever order they registered in. */
function inDocumentOrder(tabs: readonly BtnTab[]): readonly BtnTab[] {
  return [...tabs].sort((a, b) => (a.element.compareDocumentPosition(b.element) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1));
}

/**
 * A tablist of the APG tabs pattern around buttons written `<ui-btn role="tab">`.
 *
 * Each tab registers here and leaves its Tab stop to the list: the list is one stop, on its selected tab, or on its
 * first while none is selected, and a disabled or loading tab is never it. `←` and `→` (`↑` and `↓` when vertical)
 * move to the previous and next tab, wrapping around, `Home` and `End` to the first and last, and moving activates the
 * tab — a click, as a pointer would — since what it shows is already mounted. The stop is a signal the tabs read, so
 * the list writes nothing to the DOM itself.
 */
@Directive({
  selector: '[uiTablist]',
  host: {
    role: 'tablist',
    '[attr.aria-orientation]': "orientation() === 'vertical' ? 'vertical' : null",
    '(keydown)': 'onKeyDown($event)',
  },
})
export class BtnTablistDirective {
  /** The axis the arrows move along: the direction the tabs are laid out in. */
  readonly orientation = input<'horizontal' | 'vertical'>('horizontal');

  private readonly tabs = signal<readonly BtnTab[]>([]);

  /** The one Tab stop of the list: the selected tab that takes input, else the first that does. */
  readonly stop = computed<BtnTab | null>(() => {
    const live = inDocumentOrder(this.tabs().filter(tab => !tab.isInert()));
    return live.find(tab => tab.isSelected()) ?? live[0] ?? null;
  });

  /** Adds a tab to the list; the answer removes it again. */
  registerTab(tab: BtnTab): () => void {
    this.tabs.update(tabs => [...tabs, tab]);
    return () => this.tabs.update(tabs => tabs.filter(other => other !== tab));
  }

  protected onKeyDown(event: KeyboardEvent): void {
    const live = inDocumentOrder(this.tabs().filter(tab => !tab.isInert()));
    const from = live.findIndex(tab => tab.element === event.target);
    const to = from < 0 ? -1 : this.indexFor(event.key, from, live.length);

    if (to < 0) {
      return;
    }

    event.preventDefault();

    // The tab already showing is not pressed again: a paged container would close its page
    if (to !== from) {
      live[to].element.focus();
      live[to].element.click();
    }
  }

  /** Where a key moves from the tab at `from` among `count`; -1 for a key the list does not answer. */
  private indexFor(key: string, from: number, count: number): number {
    const isVertical = this.orientation() === 'vertical';

    switch (key) {
      case isVertical ? 'ArrowUp' : 'ArrowLeft':
        return (from - 1 + count) % count;
      case isVertical ? 'ArrowDown' : 'ArrowRight':
        return (from + 1) % count;
      case 'Home':
        return 0;
      case 'End':
        return count - 1;
      default:
        return -1;
    }
  }
}

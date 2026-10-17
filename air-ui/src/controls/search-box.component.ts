import { afterNextRender, Component, computed, DestroyRef, ElementRef, inject, input, model, output, viewChild } from '@angular/core';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { FIELD_FOCUS, FIELD_HOVER, INNER_CONTROL_HOVER, SEGMENT_LAST } from '../theme/css-classes';
import { RECORD_AUTOCOMPLETE, RECORD_NAME } from './record-autocomplete';

const DEBOUNCE_MS = 250;

/** The popup a box opens as a combobox (WAI-ARIA APG): its role and whether it is open. */
export interface SearchBoxPopup {
  readonly kind: 'grid' | 'listbox';
  readonly isOpen: boolean;
}

/**
 * The one search field of the library: the icon, the box, the debounce, and the clear control while
 * it holds text. It holds no opinion about what is searched — it says what was typed, a moment after
 * the typing stops — so the nav's search, the grid's own search, the picker's list, the menu and the
 * select's bar are the same field with different owners. In its box of 36 pixels the search icon and
 * the clear control's icon are 16 pixels, each 10 pixels from its near side edge and centred, the text
 * 10 pixels after the one and before the other; below `sm` the icon is not drawn and the text starts
 * at the box's own padding. The clear control stands 1 pixel inside the box's border, hovering inside it with the
 * `hover` ground and the `link-hover` icon. The width is the caller's, through the host class.
 */
@Component({
  selector: 'ui-search-box',
  imports: [UiIconDirective],
  template: `
    <label class="relative inline-flex items-center w-full min-w-0">
      <i icon="search" class="absolute left-2.5 text-muted pointer-events-none max-sm:hidden [&>svg]:h-4 [&>svg]:w-4"></i>
      <input
        #box
        type="search"
        autocomplete="${RECORD_AUTOCOMPLETE}"
        name="${RECORD_NAME}"
        class="w-full min-h-9 pl-9 max-sm:pl-2 rounded-lg border border-field-edge bg-field text-ink text-sm ${FIELD_HOVER} [&::-webkit-search-cancel-button]:hidden ${FIELD_FOCUS}"
        [class]="text() ? 'pr-9' : 'pr-2'"
        [placeholder]="placeholder() ?? texts.of('Search')"
        [attr.aria-label]="label() ?? placeholder() ?? texts.of('Search')"
        [attr.role]="popup() ? 'combobox' : null"
        [attr.aria-haspopup]="popup()?.kind ?? null"
        [attr.aria-expanded]="expanded()"
        [attr.aria-controls]="controls() ?? null"
        [attr.aria-activedescendant]="activeDescendant() ?? null"
        [value]="text()"
        (input)="onInput($event)"
      />
      @if (text()) {
        <button
          type="button"
          class="absolute inset-y-px right-px flex w-8.5 items-center justify-center ${SEGMENT_LAST} text-muted ${INNER_CONTROL_HOVER} cursor-pointer [&>i>svg]:h-4 [&>i>svg]:w-4"
          [attr.aria-label]="texts.of('ClearSearch')"
          (click)="clear()"
        >
          <i icon="close" aria-hidden="true"></i>
        </button>
      }
    </label>
  `,
  host: { class: 'inline-flex min-w-0' },
})
export class SearchBoxComponent {
  protected readonly texts = inject(TextsService);
  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('box');
  private timer: ReturnType<typeof setTimeout> | undefined;

  /** The text in the box; the owner sets what it starts with and follows what is typed. */
  readonly text = model<string>('');

  readonly placeholder = input<string | null>();
  readonly label = input<string | null>();

  /** The list the box drives, and its active option, when the box drives one from the keyboard. */
  readonly controls = input<string | null>();
  readonly activeDescendant = input<string | null>();

  /** The popup the box opens, which makes it a combobox; none for a box that opens nothing. */
  readonly popup = input<SearchBoxPopup | null>(null);

  /** Milliseconds of quiet before the text is said out loud. */
  readonly debounce = input<number>(DEBOUNCE_MS);

  /** Whether the box takes the caret as soon as it is drawn. */
  readonly autofocus = input<boolean>(false);

  /** What was typed, a moment after the typing stopped, or the empty text the clear control wrote. */
  readonly searched = output<string>();

  /** Whether the combobox's popup is open, as `aria-expanded` says it; null for a box that opens nothing. */
  protected readonly expanded = computed(() => {
    const popup = this.popup();
    return popup ? String(popup.isOpen) : null;
  });

  constructor() {
    afterNextRender(() => {
      if (this.autofocus()) {
        this.focus();
      }
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  focus(): void {
    this.box().nativeElement.focus();
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.text.set(value);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.searched.emit(value), this.debounce());
  }

  protected clear(): void {
    clearTimeout(this.timer);
    this.text.set('');
    this.searched.emit('');
    this.focus();
  }
}

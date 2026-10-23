import { Component, computed, DestroyRef, ElementRef, HostAttributeToken, inject, input, model } from '@angular/core';
import type { ButtonSize, ButtonVariant, NavButtonModel } from '../core/generated/contracts';
import { resolveModel } from '../core/resolve-model';
import type { UiIcon } from '../icon/ui-icon';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { FOCUS_RING } from '../theme/css-classes';
import type { UiColor } from '../theme/ui-color';
import { BtnTablistDirective, type BtnTab } from './btn-tablist.directive';
import { BTN_DEFAULTS, type BtnView } from './btn-view';

/**
 * The classes a variant carries. Every one of them is hue-agnostic: the hue arrives as a
 * class of its own, and the roles it fills are chosen in the stylesheet — see the hue
 * rules in `styles.css`. That is what lets Tailwind generate these six utilities once
 * instead of a rule set per colour, and what makes the dark theme one table.
 */
const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  soft: 'bg-(--btn-ground) border-(--btn-edge) hover:border-(--btn-edge-hover) text-(--btn-ink) hover:bg-(--btn-hover) active:bg-(--btn-active)',
  solid:
    'bg-(--hue) border-(--btn-solid-edge) hover:border-(--btn-solid-edge-hover) text-(--btn-on-hue) hover:bg-(--btn-solid-hover) active:bg-(--btn-solid-hover)',
  ghost: 'bg-transparent border-transparent text-(--btn-ink) hover:bg-(--btn-hover) active:bg-(--btn-active)',
};

/** A small button is the height of a tab in the strip; a medium one, of a nav control. */
const SIZE_CLASSES: Record<ButtonSize, { normal: string; square: string }> = {
  small: {
    normal: 'min-h-7 min-w-7 px-1.5 text-sm leading-4.5',
    square: 'w-7 h-7 justify-center',
  },
  medium: {
    normal: 'min-h-9 min-w-9 px-2.5 text-base leading-6',
    square: 'w-9 h-9 justify-center',
  },
};

/** What decides the classes of a button: its hue, variant and size, whether it shows an icon alone and whether it is drawn dimmed. */
interface BtnLook {
  readonly color: UiColor;
  readonly variant: ButtonVariant;
  readonly size: ButtonSize;
  readonly isSquare: boolean;
  readonly isInert: boolean;
}

/** The classes `ui-btn` draws on its host for the look given, before the caller's own. */
function btnClassesOf(look: BtnLook): string {
  const size = SIZE_CLASSES[look.size];
  return [
    'inline-flex items-center gap-1.5 shrink-0 select-none font-medium rounded-lg border',
    FOCUS_RING,
    `hue-${look.color}`,
    look.isSquare ? size.square : size.normal,
    VARIANT_CLASSES[look.variant],
    look.isInert ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer',
  ].join(' ');
}

/** The classes `ui-btn` draws for a medium soft slate button with a label: for an element drawn as one, a link among them. */
export const SLATE_SOFT_BTN_CLASSES = btnClassesOf({ color: 'slate', variant: 'soft', size: 'medium', isSquare: false, isInert: false });

/** The classes `ui-btn` draws for a medium ghost slate button with its icon alone: for an element drawn as one. */
export const SLATE_GHOST_ICON_BTN_CLASSES = btnClassesOf({
  color: 'slate',
  variant: 'ghost',
  size: 'medium',
  isSquare: true,
  isInert: false,
});

/**
 * A button.
 *
 * It follows the library's rule for server-described components: a `model` carrying the
 * whole `NavButtonModel`, an input per field that wins over it, and the defaults beneath both.
 * A nav element hands it the server's model whole; a template writes the inputs.
 *
 * Its colour is a hue, not a class map: the host carries `hue-<color>` and the stylesheet
 * picks the palette steps for ground, edge, ink and hover through the document's colour
 * scheme, so the same button reads correctly in both themes with no theme-conditional class
 * anywhere.
 *
 * It takes the inner radius and nothing else: a button is drawn inside a card. A caller
 * placing one on the page passes the outer radius through `class`.
 *
 * Written `<ui-btn role="tab">` inside a `uiTablist`, it is a tab: it says whether it is selected
 * rather than pressed, and its Tab stop is the tablist's to give.
 *
 * It names itself: by its label, or by its title when it shows an icon alone. A caller that names it otherwise writes
 * `aria-label` (`[aria-label]` when bound), which is an input here; an `[attr.aria-label]` never reaches the DOM, for
 * the host binding writes that attribute.
 *
 * Given a `reason` it is unavailable and stays in reach: dimmed and `aria-disabled` as a disabled button is, but it keeps
 * its tab stop and its pointer events, so the reason is read on hover and on focus, and it swallows every press.
 */
@Component({
  selector: 'ui-btn, [ui-btn], [uiBtn]',
  imports: [UiIconDirective],
  template: `
    @if (loading()) {
      <i icon="spinner" class="flex items-center justify-center shrink-0"></i>
    } @else if (view().icon; as icon) {
      <i [icon]="icon" class="flex items-center justify-center shrink-0"></i>
    }

    @if (view().label; as label) {
      <span class="min-w-0 truncate">{{ label }}</span>
    }

    <ng-content />

    @if (hasBadge()) {
      <span [class]="badgeClasses()">{{ view().badge }}</span>
    }
  `,
  host: {
    '[class]': 'hostClasses()',
    '[attr.role]': 'isTab ? "tab" : isNative ? null : "button"',
    '[attr.tabindex]': 'tabIndex()',
    '[attr.title]': 'reason() || view().title || null',
    '[attr.aria-label]': 'accessibleName()',
    '[attr.aria-pressed]': 'isTab ? null : pressed()',
    '[attr.aria-selected]': 'isTab ? (view().selected ? "true" : "false") : null',
    '[attr.aria-busy]': 'loading() ? "true" : null',
    '[attr.aria-disabled]': 'view().disabled || reason() ? "true" : null',
    '[attr.aria-description]': 'reason() || null',
    '[class.pointer-events-none]': 'isInert()',
    '(click)': 'onClick($event)',
    '(keydown.enter)': 'onKeyDown($event)',
    '(keydown.space)': 'onKeyDown($event)',
  },
})
export class BtnComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /**
   * On a native `<button>` the element already is one: it has the role, the tab stop and
   * the keys, and the library adds none of them. As an element of its own it has none, and
   * gets all three here.
   */
  protected readonly isNative = this.host.tagName === 'BUTTON';

  /** A tab of a tablist: told by the static attribute `role="tab"`, which no binding can change. */
  protected readonly isTab = inject(new HostAttributeToken('role'), { optional: true }) === 'tab';

  /** The tablist that owns a tab's Tab stop; a tab outside one keeps a stop of its own. */
  private readonly tablist = this.isTab ? inject(BtnTablistDirective, { optional: true }) : null;

  /**
   * The whole look, as a server describes it. Each field below wins over it when set.
   * Writable signals, so a template may bind either way and a test may set them outright.
   */
  readonly model = model<NavButtonModel | null>();

  readonly label = model<string | null>();
  readonly title = model<string | null>();
  readonly icon = model<UiIcon | null>();
  readonly color = model<UiColor | null>();
  readonly variant = model<ButtonVariant | null>();
  readonly size = model<ButtonSize | null>();
  readonly badge = model<string | number | null>();
  readonly disabled = model<boolean | null>();
  readonly selected = model<boolean | null>();

  /** Why the button is unavailable: dimmed and aria-disabled, yet in the tab order and open to hover, deaf to a press. */
  readonly reason = model<string | null>();

  /** Shows a spinner in place of the icon and swallows clicks until it ends. */
  readonly loading = model(false);

  /** The badge's own hue. */
  readonly badgeColor = model<UiColor>('orange');

  /** Extra classes, for the caller that puts a button somewhere unusual. */
  readonly class = input('');

  /**
   * The accessible name, when the label or the title does not say what the button does: a chip that shows a filter and
   * removes it on a click. It wins over the name the button gives itself; empty or null leaves that name.
   */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });

  readonly view = computed<BtnView>(() => {
    const model = this.model();

    return resolveModel(BTN_DEFAULTS, model && { ...model, disabled: model.isDisabled, selected: model.isSelected }, {
      label: this.label(),
      title: this.title(),
      icon: this.icon(),
      color: this.color(),
      variant: this.variant(),
      size: this.size(),
      badge: this.badge(),
      disabled: this.disabled(),
      selected: this.selected(),
    });
  });

  /** A toggle — a button told whether it is selected — says whether it is pressed, `false` too; a plain button says nothing. */
  protected readonly pressed = computed(() => {
    const selected = this.selected() ?? this.model()?.isSelected;
    return selected === null || selected === undefined ? null : String(selected);
  });

  protected readonly hasBadge = computed(() => {
    const badge = this.view().badge;
    return badge !== null && badge !== '';
  });

  /** An icon and nothing beside it. Square by that fact alone; no variant says so. */
  protected readonly isSquare = computed(() => !!this.view().icon && !this.view().label && !this.hasBadge());

  protected readonly isInert = computed(() => this.view().disabled || this.loading());

  /** Whether a press does nothing: inert, or given a reason, which leaves the button in reach. */
  protected readonly isDeaf = computed(() => this.isInert() || !!this.reason());

  /** This button as its tablist knows it; null for a button that is not a tab of one. */
  private readonly tab: BtnTab | null = this.tablist
    ? { element: this.host, isSelected: computed(() => !!this.view().selected), isInert: this.isInert }
    : null;

  /** Out of the order while inert; a tab only while it is its tablist's stop; a native button's own. */
  protected readonly tabIndex = computed(() => {
    if (this.isInert()) {
      return -1;
    }
    if (this.tab) {
      return this.tablist?.stop() === this.tab ? 0 : -1;
    }
    return this.isNative ? null : 0;
  });

  /** The caller's name first; else an icon-only button is named by its title, and one with a label needs nothing more. */
  protected readonly accessibleName = computed(() => {
    const { label, title } = this.view();
    return this.ariaLabel() || (!label && title ? title : null);
  });

  readonly hostClasses = computed(() => {
    const view = this.view();
    const classes = btnClassesOf({
      color: view.color,
      variant: view.selected ? 'solid' : view.variant,
      size: view.size,
      isSquare: this.isSquare(),
      isInert: this.isDeaf(),
    });
    const own = this.class();
    return own ? `${classes} ${own}` : classes;
  });

  protected readonly badgeClasses = computed(
    () => `hue-${this.badgeColor()} bg-(--hue) text-(--btn-on-hue) shrink-0 rounded-full px-1.5 text-xs font-semibold leading-4`,
  );

  constructor() {
    // The template that hosts the button hears a click before the button's own listener does, so a press against a
    // reason is swallowed in the capture phase, ahead of every handler of the host.
    this.host.addEventListener('click', event => this.onReasonedClick(event), { capture: true });

    if (this.tab && this.tablist) {
      inject(DestroyRef).onDestroy(this.tablist.registerTab(this.tab));
    }
  }

  /** A click against a reason reaches no handler: of the host's template, of the host or of what stands around it. */
  protected onReasonedClick(event: Event): void {
    if (this.reason()) {
      event.stopImmediatePropagation();
      event.preventDefault();
    }
  }

  protected onClick(event: MouseEvent): void {
    if (this.isInert()) {
      event.stopImmediatePropagation();
      event.preventDefault();
    }
  }

  /** `Enter` and `Space` press the element form; a native button presses itself. */
  protected onKeyDown(event: Event): void {
    if (this.isNative) {
      return;
    }

    event.preventDefault();

    if (!this.isDeaf()) {
      this.host.click();
    }
  }
}

import { Component, computed, input } from '@angular/core';
import type { UiColor } from '../theme/ui-color';

/** The badge's box: a pill of a line of small semibold text, the badge's ink on the badge's ground of its hue. */
const BADGE =
  'inline-flex items-center min-w-0 max-w-full rounded-full px-2 text-xs font-semibold leading-5 select-none bg-(--badge-ground) text-(--badge-ink)';

/**
 * A short text in a hue: the name of a status or of an option, on its hue's ground. The label is written as text,
 * never HTML, on one line, cut where the badge is narrower than it and whole in the title. A badge without a colour is
 * slate. It is no control: no role, no tab stop and no click of its own; what stands around it may act on a click.
 */
@Component({
  selector: 'ui-badge',
  template: `
    <span class="min-w-0 truncate">{{ label() }}</span>
  `,
  host: { '[class]': 'classes()', '[attr.title]': 'label() || null' },
})
export class BadgeComponent {
  readonly label = input.required<string>();
  readonly color = input<UiColor | null>();

  protected readonly classes = computed(() => `hue-${this.color() ?? 'slate'} ${BADGE}`);
}

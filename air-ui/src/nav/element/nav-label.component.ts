import { Component, computed, inject, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { NavLabelModel } from '../../core/generated/contracts';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { OverlayRef } from '../../overlay/overlay-ref';
import { PANELS } from '../../panels/panels-tokens';

/**
 * A caption in a nav: a label with an optional icon and title in the model's colour,
 * styled at the height of a button (`min-h-9`) without a tab stop or a click action.
 *
 * A caption whose model names a `path` is that panel's tab while the panel is a page of an
 * exclusive container — the form's caption on a phone, where the form and its child tables
 * are one paged strip — and the plain caption again the moment the panel is pinned.
 */
@Component({
  selector: 'ui-nav-label',
  imports: [BtnComponent, UiIconDirective],
  template: `
    @if (page(); as found) {
      <ui-btn [attr.id]="id" [model]="model()" [selected]="found.page.isOpen" (click)="open()" />
    } @else {
      <span
        class="inline-flex items-center gap-1.5 min-h-9 px-2.5 text-sm font-semibold select-none text-(--btn-ink)"
        [class]="colorClass()"
        [attr.id]="id"
        [attr.title]="model()?.title || null"
      >
        @if (model()?.icon; as icon) {
          <i [icon]="icon" class="shrink-0"></i>
        }
        @if (model()?.label; as label) {
          <span class="min-w-0 truncate">{{ label }}</span>
        }
      </span>
    }
  `,
})
export class NavLabelComponent {
  private static next = 0;

  private readonly panels = inject(PANELS, { optional: true });

  /** The caption's id: inside an overlay it names the overlay's card, a form's caption naming the form's dialog. */
  protected readonly id = `ui-nav-label-${++NavLabelComponent.next}`;

  readonly model = model<NavLabelModel | null>();

  /** The page the caption stands for, while the panel its model names is one. */
  protected readonly page = computed(() => {
    const path = this.model()?.path;
    return path ? this.panels?.pageOf(path) : undefined;
  });

  protected readonly colorClass = computed(() => {
    const color = this.model()?.color;
    return color ? `hue-${color}` : 'hue-slate';
  });

  constructor() {
    inject(OverlayRef, { optional: true })?.name({ labelledBy: this.id });
  }

  protected open(): void {
    const path = this.model()?.path;

    if (path && this.page()) {
      this.panels?.open(path);
    }
  }
}

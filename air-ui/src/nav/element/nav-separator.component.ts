import { Component, computed, inject } from '@angular/core';
import { NavComponent } from '../nav.component';

/** A hairline across the nav's cross axis. */
@Component({
  selector: 'ui-nav-separator',
  template: '<div [class]="classes()"></div>',
  host: { class: 'contents' },
})
export class NavSeparatorComponent {
  private readonly nav = inject(NavComponent, { optional: true });

  protected readonly classes = computed(() => (this.nav?.isColumn() ? 'w-full h-px bg-line' : 'h-5 w-px bg-line'));
}

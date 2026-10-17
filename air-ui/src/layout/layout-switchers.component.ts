import { booleanAttribute, Component, inject, input } from '@angular/core';
import { AppService } from '../app/app.service';
import type { ButtonSize } from '../core/generated/contracts';
import { LayoutSwitcherComponent } from './layout-switcher.component';

/**
 * The session's switchers, the tenant's and then the language's, as the frame draws them: in the navigation's footer on a
 * row of their own, in the tab strip and in the mega menu's nav. A switcher the metadata does not carry draws nothing.
 */
@Component({
  selector: 'ui-layout-switchers',
  imports: [LayoutSwitcherComponent],
  template: `
    <ui-layout-switcher [onPage]="onPage()" [size]="size()" [fluid]="fluid()" [model]="app.tenant()" />
    <ui-layout-switcher [onPage]="onPage()" [size]="size()" [fluid]="fluid()" [model]="app.language()" />
  `,
  host: { class: 'contents' },
})
export class LayoutSwitchersComponent {
  protected readonly app = inject(AppService);

  /** Whether the switchers lie on the page, as in the tab strip, rather than in a card. */
  readonly onPage = input(false, { transform: booleanAttribute });

  /** The switchers' size: small in the navigation and the strip, medium in the mega menu's nav. */
  readonly size = input<ButtonSize>('small');

  /** Whether each switcher takes an equal share of the row around them and truncates its name to it. */
  readonly fluid = input(false, { transform: booleanAttribute });
}

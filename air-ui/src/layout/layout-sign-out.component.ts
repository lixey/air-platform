import { booleanAttribute, Component, computed, inject, input } from '@angular/core';
import { AuthService } from '../app/auth/auth.service';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { ON_PAGE } from './on-page';

/**
 * Sign-out: the one button that ends the session, in the navigation's footer and at the end of the tab strip while the
 * navigation is hidden.
 *
 * `onPage` sets it for the strip, which lies on the page rather than in a card: the page's tones and the outer radius.
 */
@Component({
  selector: 'ui-layout-sign-out',
  imports: [BtnComponent],
  template: `
    <ui-btn size="small" variant="ghost" icon="logout" [title]="texts.of('SignOut')" [class]="classes()" (click)="auth.logout()" />
  `,
  host: {
    class: 'contents',
  },
})
export class LayoutSignOutComponent {
  protected readonly texts = inject(TextsService);
  protected readonly auth = inject(AuthService);

  /** Whether the button lies on the page, as in the tab strip, rather than in a card. */
  readonly onPage = input(false, { transform: booleanAttribute });

  protected readonly classes = computed(() => (this.onPage() ? ON_PAGE : ''));
}

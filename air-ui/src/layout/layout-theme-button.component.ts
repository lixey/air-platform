import { Component, inject, input } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { ThemeService } from '../theme/theme.service';

/**
 * The control that switches the theme, as the navigation's footer and the mega menu's nav both carry it: the
 * library's ghost button, showing the theme it switches to and named by it. It is small in the footer and medium in the
 * mega menu's nav, the height of the filter beside it.
 */
@Component({
  selector: 'ui-layout-theme-button',
  imports: [BtnComponent],
  template: `
    <ui-btn
      variant="ghost"
      [size]="size()"
      [icon]="theme.isDark() ? 'sun' : 'moon'"
      [title]="theme.isDark() ? texts.of('ThemeLight') : texts.of('ThemeDark')"
      (click)="theme.toggle()"
    />
  `,
  host: {
    class: 'contents',
  },
})
export class LayoutThemeButtonComponent {
  readonly size = input<'small' | 'medium'>('small');

  protected readonly texts = inject(TextsService);
  protected readonly theme = inject(ThemeService);
}

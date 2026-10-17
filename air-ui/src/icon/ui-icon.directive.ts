import { afterRenderEffect, Directive, ElementRef, inject, input } from '@angular/core';
import { ICON_SET } from './icon-set';
import type { UiIcon } from './ui-icon';

@Directive({
  selector: 'uiIcon, [uiIcon], i[icon]',
  exportAs: 'uiIcon',
})
export class UiIconDirective {
  private readonly el = inject(ElementRef<HTMLElement>);
  readonly icon = input<UiIcon | null>();

  private readonly iconFx = afterRenderEffect(() => {
    const name = this.icon();
    if (!name) {
      this.el.nativeElement.innerHTML = '';
      return;
    }
    const svgMarkup = ICON_SET[name];
    if (svgMarkup) {
      this.el.nativeElement.innerHTML = `<!-- ${name} -->${svgMarkup}`;
    } else {
      this.el.nativeElement.innerHTML = '';
    }
  });
}

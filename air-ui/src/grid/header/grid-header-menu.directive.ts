import { computed, Directive, ElementRef, inject, Injector, input } from '@angular/core';
import type { GridColumnDto } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { OverlayService } from '../../overlay/overlay.service';
import { GRID_HEADER_BUTTON } from '../../theme/css-classes';
import { columnLabelOf } from '../column/grid-column-label';
import { gridColumnFilterOverlay } from '../filter/grid-column-filter-overlay.component';

/**
 * The `⋮` button of a column header: a menu button that names itself by the column, says whether its menu is open
 * and which card it controls, and opens the column's filter overlay from itself.
 */
@Directive({
  selector: 'button[uiGridHeaderMenu]',
  host: {
    type: 'button',
    class: GRID_HEADER_BUTTON,
    '[attr.aria-label]': "texts.of('ColumnMenu', label())",
    'aria-haspopup': 'dialog',
    '[attr.aria-expanded]': 'menu.isOpen()',
    '[attr.aria-controls]': 'menu.id()',
    '(click)': 'open($event)',
  },
})
export class GridHeaderMenuDirective {
  /** The column whose menu the button opens. */
  readonly column = input.required<GridColumnDto>({ alias: 'uiGridHeaderMenu' });

  protected readonly texts = inject(TextsService);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** The column menu the button opens: what the button says of it. */
  protected readonly menu = inject(OverlayService).menu();

  protected readonly label = computed(() => columnLabelOf(this.column()));

  protected open(event: MouseEvent): void {
    event.stopPropagation();
    this.menu.open(
      gridColumnFilterOverlay(this.column()),
      this.element.nativeElement,
      this.injector,
      this.texts.of('FilterBy', this.label()),
    );
  }
}

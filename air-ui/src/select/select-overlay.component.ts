import { Component, computed, inject } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { GridComponent } from '../grid/grid.component';
import { screenLoader } from '../screen/screen-loader';
import { SelectBarComponent } from './select-bar.component';
import { LIST_PADDING, OPTION, OPTION_GAP, OPTION_MIN_HEIGHT } from './select-list';
import { isPicked, labelPathsOf, type SelectOption } from './select-option';
import { SelectPickedComponent } from './select-picked.component';
import { SelectService } from './select.service';

/**
 * The body of the select overlay, `select.grid`: the bar, then the list — static options as
 * a listbox the search box drives, or the grid of the service the model names, asked for its label columns and drawn
 * here from the answer's model and page; the grid finds this select by injection and picks on
 * a row click — and, in multiple mode, the panel of the picked beside it. The list is as tall
 * as its rows up to the select's height, and shrinks with the card when the host has less
 * room, the rows scrolling under the bar. A card that stands above its field keeps the height
 * the select states, so the room under a short list is the card's and the bar keeps its place.
 */
@Component({
  selector: 'ui-select-overlay',
  imports: [SelectBarComponent, SelectPickedComponent, GridComponent, BtnComponent],
  template: `
    <ui-select-bar />
    <div class="flex flex-1 min-h-0 overflow-hidden">
      @if (select.model().options) {
        <div
          role="listbox"
          class="flex flex-col flex-1 min-w-0 min-h-0 max-h-96 overflow-auto"
          [style.padding.px]="listPadding"
          [style.gap.px]="optionGap"
          [id]="select.listId"
          [attr.aria-label]="select.model().label"
          [attr.aria-multiselectable]="select.isMultiple() || null"
        >
          @for (option of select.options(); track option.id; let index = $index) {
            <button
              type="button"
              role="option"
              tabindex="-1"
              class="${OPTION}"
              [style.min-height.px]="optionMinHeight"
              [class.bg-row-active]="index === select.activeIndex()"
              [id]="select.listId + '-' + index"
              [attr.aria-selected]="isPicked(option)"
              [title]="option.title || option.label"
              (click)="select.pick(option)"
            >
              <span class="truncate">{{ option.label }}</span>
            </button>
          } @empty {
            <p class="m-0 p-3 text-sm text-muted">{{ texts.of('NothingToPick') }}</p>
          }
        </div>
      } @else if (grid(); as grid) {
        <div class="flex flex-col flex-1 min-w-0 min-h-0 max-h-96" [attr.inert]="screen.isStale() || null">
          <ui-grid [model]="grid.model" [data]="grid.data" />
        </div>
      } @else if (screen.error()) {
        <div role="alert" class="m-3 p-4 flex items-center gap-3 text-sm text-warning">
          {{ texts.of('OptionsLoadFailed') }}
          <ui-btn [label]="texts.of('Retry')" (click)="screen.reload()" />
        </div>
      }
      @if (select.isMultiple()) {
        <ui-select-picked />
      }
    </div>
  `,
  host: {
    class: 'flex flex-col min-w-0 max-h-full overflow-hidden',
    '[attr.aria-busy]': 'screen.isLoading() || null',
  },
})
export class SelectOverlayComponent {
  protected readonly texts = inject(TextsService);
  protected readonly select = inject(SelectService);

  /** The first page of the service's grid, asked for the label columns; the grid inside asks the rest itself. */
  protected readonly screen = screenLoader(() => {
    const model = this.select.model();
    return model.service && !model.options
      ? {
          service: model.service,
          params: {
            page: 1,
            referenceLabelPaths: [...labelPathsOf(model.labelPaths)],
            ...(model.patchSourceColumns?.length ? { patchSourceColumns: [...model.patchSourceColumns] } : {}),
            ...(model.formDraft ? { formDraft: model.formDraft } : {}),
            ...(model.referenceFilter ? { filter: model.referenceFilter } : {}),
          },
        }
      : null;
  });

  /** The grid the answer's one panel carries, kept only when the panel's kind says it is one, and the page that came with it. */
  protected readonly grid = computed(() => {
    const model = this.screen.layout()?.root.children?.[0]?.model;
    return model?.kind === 'Grid' ? { model, data: this.screen.data() } : null;
  });

  /** The numbers that draw a static list's rows, the ones `SelectService` sizes a short list by. */
  protected readonly listPadding = LIST_PADDING;
  protected readonly optionGap = OPTION_GAP;
  protected readonly optionMinHeight = OPTION_MIN_HEIGHT;

  /** Whether an option is the one picked; in multiple mode the picked are not listed. */
  protected isPicked(option: SelectOption): boolean {
    return isPicked(option, this.select.draft());
  }
}

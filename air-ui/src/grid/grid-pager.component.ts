import { Component, computed, inject, input } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { FOCUS_RING } from '../theme/css-classes';
import { formatCount } from './cell/grid-cell-format';
import { GridService } from './grid.service';

/** One control of the pager: a flat cell of the strip, no radius of its own. */
const CELL = `flex items-center justify-center h-8 min-w-8 px-1.5 text-sm tabular-nums ${FOCUS_RING}`;
const BUTTON = `${CELL} cursor-pointer text-ink hover:bg-hover disabled:cursor-default disabled:text-muted disabled:hover:bg-transparent`;
const CURRENT = `${CELL} bg-active text-on-active`;

/**
 * The pager alone, floating over the table's bottom-right corner — above
 * the rows the page pins there — so the table keeps every pixel it can. The
 * total as a mark, in the session's culture — `—` with a recount when its
 * count failed —, the previous arrow, the first page, a window
 * of pages around the current one with an ellipsis where pages are skipped, the last page, the
 * next arrow. The window holds nine pages where the panel is wide and three where it is narrow,
 * decided by the panel's container query, so the strip fits a narrow panel without measuring.
 */
@Component({
  selector: 'ui-grid-pager',
  imports: [UiIconDirective],
  template: `
    <nav
      class="flex items-center overflow-hidden rounded-lg border border-line bg-surface shadow-sm pointer-events-auto"
      [attr.aria-label]="texts.of('Pages')"
    >
      @if (grid.isCountFailed()) {
        <!-- The count failed: the total is unknown, never 0, and a click counts again -->
        <button type="button" class="${BUTTON} text-mark" [title]="texts.of('Retry')" (click)="grid.recount()">—</button>
      } @else {
        <span class="${CELL} text-mark" [attr.aria-label]="texts.of('RowCount', total())">
          @if (grid.isCountPending()) {
            …
          } @else {
            {{ total() }}
          }
        </span>
      }

      @if (grid.pageCount() > 1 || grid.hasMore()) {
        <button
          type="button"
          class="${BUTTON}"
          [attr.aria-label]="texts.of('PreviousPage')"
          [disabled]="page() <= 1"
          (click)="grid.goToPage(page() - 1)"
        >
          <i icon="chevron-left" aria-hidden="true"></i>
        </button>

        <span class="contents @4xl/panel:hidden">
          @for (item of narrowItems(); track $index) {
            @if (item === null) {
              <span class="${CELL} text-muted" aria-hidden="true">…</span>
            } @else {
              <button
                type="button"
                [class]="item === page() ? '${CURRENT}' : '${BUTTON}'"
                [attr.aria-current]="item === page() ? 'page' : null"
                (click)="grid.goToPage(item)"
              >
                {{ item }}
              </button>
            }
          }
        </span>
        <span class="hidden @4xl/panel:contents">
          @for (item of wideItems(); track $index) {
            @if (item === null) {
              <span class="${CELL} text-muted" aria-hidden="true">…</span>
            } @else {
              <button
                type="button"
                [class]="item === page() ? '${CURRENT}' : '${BUTTON}'"
                [attr.aria-current]="item === page() ? 'page' : null"
                (click)="grid.goToPage(item)"
              >
                {{ item }}
              </button>
            }
          }
        </span>

        <button
          type="button"
          class="${BUTTON}"
          [attr.aria-label]="texts.of('NextPage')"
          [disabled]="!grid.hasMore() && page() >= grid.pageCount()"
          (click)="grid.goToPage(page() + 1)"
        >
          <i icon="chevron-right" aria-hidden="true"></i>
        </button>
      }
    </nav>
  `,
  host: {
    class: 'absolute right-2 z-30 pointer-events-none',
    '[style.bottom.px]': 'inset() + 8',
  },
})
export class GridPagerComponent {
  protected readonly grid = inject(GridService);
  protected readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;

  /** What the table keeps at its bottom edge — the pinned bottom rows — that the pager floats above. */
  readonly inset = input(0);

  protected readonly page = this.grid.page;

  /** The total, counted in the session's culture. */
  protected readonly total = computed(() => formatCount(this.grid.total(), this.locale()));

  protected readonly wideItems = computed(() => pageItems(this.page(), this.grid.pageCount(), 9));
  protected readonly narrowItems = computed(() => pageItems(this.page(), this.grid.pageCount(), 3));
}

/**
 * The page numbers to show: the first, a window of `windowSize` pages around the current one,
 * the last, with `null` where pages are skipped; every page when they all fit.
 */
export function pageItems(current: number, total: number, windowSize = 9): (number | null)[] {
  if (total <= 1) {
    return [1];
  }

  const middleCount = total - 2;
  let middle: number[];
  if (middleCount <= windowSize) {
    middle = Array.from({ length: Math.max(0, middleCount) }, (_, i) => i + 2);
  } else {
    const half = Math.floor(windowSize / 2);
    let start = current - half;
    let end = current + half;
    if (start < 2) {
      end += 2 - start;
      start = 2;
    }
    if (end > total - 1) {
      start = Math.max(2, start - (end - (total - 1)));
      end = total - 1;
    }
    if (end - start + 1 < windowSize) {
      start = Math.max(2, start - (windowSize - (end - start + 1)));
    }
    middle = Array.from({ length: end - start + 1 }, (_, i) => start + i);
  }

  const items: (number | null)[] = [1];
  if (middle.length > 0 && middle[0] > 2) {
    items.push(null);
  }
  items.push(...middle);
  if (middle.length > 0 && middle[middle.length - 1] < total - 1) {
    items.push(null);
  }
  items.push(total);
  return items;
}

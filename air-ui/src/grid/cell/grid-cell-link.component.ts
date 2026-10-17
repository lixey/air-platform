import { Component, computed, inject, input } from '@angular/core';
import { TextsService } from '../../core/texts.service';
import { isWebAddress } from './grid-cell-href';

/**
 * The link a link cell draws: its text as a link to `href` in the grid's link ink, or as plain text without one. A
 * link to a page of the web opens a new browsing context with neither opener nor referrer and says so to a screen
 * reader; a call or a letter goes to the application the system names. A click on it acts on the link, not the row.
 */
@Component({
  selector: 'ui-grid-cell-link',
  template: `
    @if (href(); as href) {
      <a
        class="block min-w-0 truncate text-link hover:text-link-hover hover:underline hover:underline-offset-4"
        [attr.target]="opensNewTab() ? '_blank' : null"
        [attr.rel]="opensNewTab() ? 'noopener noreferrer' : null"
        [href]="href"
        [title]="text()"
        (click)="$event.stopPropagation()"
      >
        {{ text() }}
        @if (opensNewTab()) {
          <span class="sr-only">({{ texts.of('OpensInNewTab') }})</span>
        }
      </a>
    } @else {
      <span class="block truncate" [title]="text()">{{ text() }}</span>
    }
  `,
  host: { class: 'flex min-w-0' },
})
export class GridCellLinkComponent {
  protected readonly texts = inject(TextsService);

  readonly text = input.required<string>();

  /** Where the link leads; null draws the text alone. */
  readonly href = input<string | null>(null);

  protected readonly opensNewTab = computed(() => isWebAddress(this.href() ?? ''));
}

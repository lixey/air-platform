import { Component, inject } from '@angular/core';
import { ProgressService } from './progress.service';

/**
 * The smart request progress bar across the top of the page: stays hidden for fast (< 400 ms) requests,
 * creeps smoothly in saturated orange for longer ones, and fills and fades when all requests finish.
 */
@Component({
  selector: 'ui-progress',
  template: `
    <div
      class="fixed z-60 h-0.75 bg-transparent overflow-hidden pointer-events-none"
      style="top: var(--sat); left: var(--sal); right: var(--sar)"
    >
      <div class="ui-progress-bar" [class.is-loading]="progress.phase() === 'loading'" [class.is-done]="progress.phase() === 'done'"></div>
    </div>
  `,
  host: {
    class: 'contents',
  },
})
export class ProgressComponent {
  protected readonly progress = inject(ProgressService);
}

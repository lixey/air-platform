import { Component, inject } from '@angular/core';
import { ProgressService } from './progress.service';

/** The request progress bar across the top of the page; it creeps while a request runs and completes when all end. */
@Component({
  selector: 'ui-progress',
  template: `
    <div class="fixed top-0 left-0 right-0 z-50 h-0.5 bg-transparent overflow-hidden pointer-events-none">
      <div class="ui-progress-bar" [class.is-loading]="progress.isLoading()"></div>
    </div>
  `,
  host: {
    class: 'contents',
  },
})
export class ProgressComponent {
  protected readonly progress = inject(ProgressService);
}

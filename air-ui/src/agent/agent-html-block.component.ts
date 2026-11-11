import { Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';
import { sanitizeAgentHtml } from './agent-markdown';

@Component({
  selector: 'ui-agent-html-block',
  template: `
    <div
      class="rounded-lg border border-line bg-surface-2 px-3.5 py-2.5 text-sm text-ink break-words overflow-x-auto"
      [innerHTML]="safeHtml()"
    ></div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentHtmlBlockComponent {
  private readonly sanitizer = inject(DomSanitizer);
  readonly html = input.required<string>();

  protected readonly safeHtml = computed<SafeHtml>(() => this.sanitizer.bypassSecurityTrustHtml(sanitizeAgentHtml(this.html())));
}

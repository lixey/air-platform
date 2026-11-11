import { Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';
import { renderMarkdownToSafeHtml } from './agent-markdown';

@Component({
  selector: 'ui-agent-markdown-block',
  template: `
    <div
      class="rounded-lg border border-line bg-surface-2 px-3.5 py-2.5 text-sm text-ink break-words space-y-1.5"
      [innerHTML]="safeHtml()"
    ></div>
  `,
  host: { class: 'block w-full min-w-0' },
})
export class AgentMarkdownBlockComponent {
  private readonly sanitizer = inject(DomSanitizer);
  readonly content = input.required<string>();

  protected readonly safeHtml = computed<SafeHtml>(() => this.sanitizer.bypassSecurityTrustHtml(renderMarkdownToSafeHtml(this.content())));
}

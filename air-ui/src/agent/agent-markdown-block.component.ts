import { Component, computed, input } from '@angular/core';
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
  readonly content = input.required<string>();

  protected readonly safeHtml = computed<string>(() => renderMarkdownToSafeHtml(this.content()));
}

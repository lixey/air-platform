import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
  untracked,
  type WritableSignal,
} from '@angular/core';
import { DynamicOutletDirective } from '../core/dynamic-component/dynamic-outlet.directive';
import type { FormElementDto, FormFilesModel, FormModel, MessageDto } from '../core/generated/contracts';
import { focusables } from '../overlay/overlay-focus';
import { PanelsContext } from '../panels/panels-context';
import { PANELS } from '../panels/panels-tokens';
import { FormCloseGuard } from './form-close-guard';
import { FormElementOutletComponent } from './form-element-outlet.component';
import { FormOutcomeComponent } from './form-outcome.component';
import { FORM, FORM_TAB, RECORD_TAB } from './form-panel-values';
import { spanClassOf } from './form-span';
import { FormService, isFormData } from './form.service';

/**
 * The `form` panel: the outcome and the form elements laid out on a slate canvas with
 * low-noise horizontal dividers. It draws no nav: the nav is a sibling panel the server
 * composed, and its elements reach this form through the layout's shared value.
 *
 * A form with tabs shows the elements of the tab the shared value names — the elements
 * declared before the first tab on the record tab, whose button is the caption — and keeps
 * the others mounted and hidden, so a value typed on one tab is there when the tab is back
 * and the draft is one draft.
 *
 * The record's files a form declares in its body (`FormFiles`) stand after all the elements, each the whole row wide
 * and shown with the tab it names, so they come after the elements of that tab.
 */
@Component({
  selector: 'ui-form',
  imports: [DynamicOutletDirective, FormElementOutletComponent, FormOutcomeComponent],
  providers: [FormService, FormCloseGuard],
  template: `
    <ui-form-outcome />
    <div class="flex-1 min-h-0 overflow-y-auto w-full p-3 bg-page @container" data-form-body>
      <div class="grid grid-cols-12 gap-x-4 gap-y-3 content-start">
        @for (element of form.elements(); track element.path) {
          <div
            class="min-w-0"
            [class]="spanOf(element)"
            [hidden]="!isShown(element)"
            [attr.data-tab]="element.tab || null"
            [attr.data-focus]="element.isFocused ? '' : null"
          >
            <ui-form-element-outlet [element]="element" />
          </div>
          @if (element.hasSeparatorAfter) {
            <div
              class="col-span-full flex items-center gap-2 my-2 select-none"
              role="separator"
              data-separator
              [hidden]="!isShown(element)"
            >
              @if (element.separatorLabel; as label) {
                <span class="text-xs font-semibold text-ink" data-separator-label>
                  {{ label }}
                </span>
              }
              <div class="flex-1 border-t border-edge"></div>
            </div>
          }
        }
        @for (files of files(); track files.key) {
          <div class="col-span-full min-w-0" [hidden]="!isTabShown(files.tab)" [attr.data-tab]="files.tab || null" data-form-files>
            <ng-container [uiDynamicOutlet]="files.kind" [uiDynamicOutletInputs]="{ model: files }" />
          </div>
        }
      </div>
    </div>
  `,
  host: { class: 'flex flex-col w-full h-full min-w-0 min-h-0 overflow-hidden', '[attr.aria-busy]': 'form.isBusy()' },
})
export class FormComponent {
  protected readonly form = inject(FormService);
  private readonly guard = inject(FormCloseGuard);
  private readonly context = inject(PanelsContext, { optional: true });
  /** The panels host of the layout the form stands in: where a child page a press asked for is opened. */
  private readonly panels = inject(PANELS, { optional: true });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);

  /** The tab showing, shared with the nav's tabs; a form mounted outside a layout keeps one of its own. */
  private readonly tab: WritableSignal<string> = this.context?.of(FORM_TAB) ?? signal('');

  readonly model = input<FormModel | null>();
  readonly data = input<unknown>();

  /** The tab whose elements show: the one chosen, or the record tab while nothing has been chosen. */
  private readonly showing = computed(() => this.tab() || RECORD_TAB);

  /** The record's files drawn in the body, in declared order. */
  protected readonly files = computed<readonly FormFilesModel[]>(() => this.model()?.files ?? []);
  private focusedData: unknown = null;

  constructor() {
    const published = this.context?.of(FORM);
    published?.set(this.form);
    inject(DestroyRef).onDestroy(() => {
      if (published?.() === this.form) {
        published.set(null);
      }
    });

    effect(() => {
      const model = this.model() ?? null;
      const data = this.data();
      untracked(() => {
        this.guard.replace(model, isFormData(data) ? data : null);
        if (model && isFormData(data) && !data.id && this.focusedData !== data) {
          this.focusedData = data;
          afterNextRender(() => this.focusInitialField(), { injector: this.injector });
        }
      });
    });

    // The page a child's button asked for opens once the layout read after the commit holds it
    effect(() => {
      const page = this.form.pageToOpen();
      if (page && this.panels?.pageOf(page)) {
        untracked(() => {
          this.panels?.open(page);
          this.form.pageOpened();
        });
      }
    });

    const stopRefusals = this.form.onRefused(messages => {
      this.bringForward(messages);
      afterNextRender(() => this.focusFirstInvalid(), { injector: this.injector });
    });
    inject(DestroyRef).onDestroy(stopRefusals);
  }

  protected spanOf(element: FormElementDto): string {
    return spanClassOf(element);
  }

  /** Every element of a form without tabs; with tabs, the elements of the showing one. */
  protected isShown(element: FormElementDto): boolean {
    return this.isTabShown(element.tab);
  }

  /** Whether what belongs to `tab`, the record tab when it names none, shows: always in a form without tabs. */
  protected isTabShown(tab: string | null | undefined): boolean {
    return !this.model()?.tabs?.length || (tab ?? RECORD_TAB) === this.showing();
  }

  /**
   * A refusal puts on fields the messages their elements can show; the first tab in nav order holding one — the record
   * tab, then the declared ones — comes forward unless the showing tab holds one already. A message the alert says, of
   * a hidden, disabled or server-owned element, is not among `messages` and brings no tab forward.
   */
  private bringForward(messages: readonly MessageDto[]): void {
    const model = this.model();
    const tabs = model?.tabs ?? [];
    if (!model || !tabs.length) {
      return;
    }

    const flagged = new Set(messages.map(message => message.path).filter(Boolean));
    const holding = new Set(model.elements.filter(element => flagged.has(element.path)).map(tabOf));
    if (holding.has(this.showing())) {
      return;
    }

    const first = [RECORD_TAB, ...tabs.map(tab => tab.key)].find(key => holding.has(key));
    if (first) {
      this.tab.set(first);
    }
  }

  /** After a refusal the first invalid field of the tab showing takes focus, so the user starts where the server did. */
  private focusFirstInvalid(): void {
    const invalid = [...this.host.querySelectorAll<HTMLElement>('[aria-invalid="true"]')].find(field => !field.closest('[hidden]'));
    invalid?.focus();
  }

  /** On a new record, the first visible element marked `.Focus()` takes focus once rendered. */
  private focusInitialField(): void {
    const marked = [...this.host.querySelectorAll<HTMLElement>('[data-focus]')].find(field => !field.closest('[hidden]'));
    if (!marked) {
      return;
    }
    focusables(marked)[0]?.focus();
  }
}

/** The tab an element belongs to: the one it names, or the record tab when it names none. */
function tabOf(element: FormElementDto): string {
  return element.tab ?? RECORD_TAB;
}

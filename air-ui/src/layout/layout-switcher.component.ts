import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext } from '@angular/common/http';
import { booleanAttribute, Component, computed, DestroyRef, inject, InjectionToken, Injector, input, signal } from '@angular/core';
import { AlertService } from '../app/alert/alert.service';
import { SKIP_GLOBAL_ERROR } from '../app/alert/error-alert.interceptor';
import { BtnComponent } from '../btn/btn.component';
import { serviceUrl } from '../core/api';
import type { ButtonSize, SwitcherDto, SwitchRequest } from '../core/generated/contracts';
import { problemOf } from '../core/problem';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { optionOfReference, type SelectOption } from '../select/select-option';
import { SelectService } from '../select/select.service';
import { ON_PAGE } from './on-page';

/** What a successful switch does to the page: reloads it. A token, so a test observes it instead of reloading. */
export const PAGE_RELOAD = new InjectionToken<() => void>('PAGE_RELOAD', {
  providedIn: 'root',
  factory: () => {
    const view = inject(DOCUMENT).defaultView;
    return () => view?.location.reload();
  },
});

/** The label a switcher without options draws is as high, as padded and as large as a button of the same size. */
const LABEL_SIZE: Record<ButtonSize, string> = {
  small: 'min-h-7 px-1.5 text-sm leading-4.5',
  medium: 'min-h-9 px-2.5 text-base leading-6',
};

/**
 * One session switcher of the metadata — the tenant's or the language's — as the layout draws
 * it: in the navigation's footer, in the tab strip beside sign-out, and in the mega menu's nav.
 *
 * With options it is the library's button, ghost and of the caller's size, reading the current choice
 * and titled by the switcher's label with that choice, that opens the library's select with the
 * options; its popover reads the scale of where the button stands. Picking another one
 * posts `{ id }` to the switcher's service and method and reloads the page, so the profile, the
 * texts and the metadata follow the new choice; a refusal shows the error, keeps the page and the
 * button reads the current choice again. Without options it is a label with the current name and
 * offers nothing; a null switcher draws nothing. What it offers comes from the metadata alone.
 */
@Component({
  selector: 'ui-layout-switcher',
  imports: [BtnComponent, UiIconDirective],
  providers: [SelectService],
  template: `
    @if (model(); as switcher) {
      @if (canSwitch()) {
        <ui-btn [size]="size()" variant="ghost" [title]="title()" [loading]="isBusy()" [class]="buttonClasses()" (click)="open($event)">
          <span class="sr-only">{{ switcher.label + ': ' }}</span>
          <span [class]="nameClasses()">{{ switcher.current.name }}</span>
          <i icon="chevron-bottom" class="shrink-0 opacity-60" aria-hidden="true"></i>
        </ui-btn>
      } @else {
        <span [class]="labelClasses()" [class.text-on-active]="onPage()" [title]="title()">
          <span class="sr-only">{{ switcher.label + ': ' }}</span>
          <span [class]="nameClasses()">{{ switcher.current.name }}</span>
        </span>
      }
    }
  `,
  host: { '[class]': 'hostClasses()' },
})
export class LayoutSwitcherComponent {
  private readonly select = inject(SelectService);
  private readonly http = inject(HttpClient);
  private readonly alert = inject(AlertService);
  private readonly injector = inject(Injector);
  private readonly reloadPage = inject(PAGE_RELOAD);

  /** The switcher as the metadata carries it; null draws nothing. */
  readonly model = input<SwitcherDto | null>(null);

  /** Drawn on the page, in the strip: the outer radius, the strip's light tone and the selection colour under the pointer. */
  readonly onPage = input(false, { transform: booleanAttribute });

  /** The button's size, and the height of the label drawn instead of it: small in the navigation, medium in a nav. */
  readonly size = input<ButtonSize>('small');

  /**
   * Takes an equal share of its row, as its row decides, and truncates the name to it: the row's width never depends on
   * a tenant's or a language's name. Otherwise the name stops at a fixed width.
   */
  readonly fluid = input(false, { transform: booleanAttribute });

  protected readonly isBusy = signal(false);

  /** The label and the current choice in full, which a truncated name still offers under the pointer. */
  protected readonly title = computed(() => {
    const switcher = this.model();
    return switcher ? `${switcher.label}: ${switcher.current.name}` : '';
  });

  protected readonly hostClasses = computed(() => (this.model() && this.fluid() ? 'flex min-w-0 flex-1' : 'contents'));
  protected readonly buttonClasses = computed(() =>
    [this.onPage() ? ON_PAGE : '', this.fluid() ? 'w-full max-w-full' : ''].join(' ').trim(),
  );
  protected readonly nameClasses = computed(() => (this.fluid() ? 'min-w-0 flex-1 text-left truncate' : 'min-w-0 max-w-40 truncate'));
  protected readonly labelClasses = computed(() => `inline-flex min-w-0 max-w-full items-center font-medium ${LABEL_SIZE[this.size()]}`);

  /** Options and the endpoint come together, and only to a principal who may switch. */
  protected readonly canSwitch = computed(() => {
    const switcher = this.model();
    return !!switcher?.options?.length && !!switcher.service && !!switcher.method;
  });

  constructor() {
    const subscription = this.select.committed.subscribe(options => this.switchTo(options[0]));
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  protected open(event: Event): void {
    const switcher = this.model();
    if (!switcher?.options || !this.canSwitch() || this.isBusy()) {
      return;
    }
    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const currentOption = switcher.options.find(o => o.id === switcher.current.id) ?? switcher.current;
    this.select.open(
      { model: { label: switcher.label, options: switcher.options.map(optionOfReference) }, picked: [optionOfReference(currentOption)] },
      anchor,
      this.injector,
    );
  }

  /** Posts the picked option unless it is the current one; reloads on success, reports and keeps the page on a refusal. */
  private switchTo(option: SelectOption | undefined): void {
    const switcher = this.model();
    if (!switcher?.service || !switcher.method || !option || option.id === switcher.current.id || this.isBusy()) {
      return;
    }
    const request: SwitchRequest = { id: option.id };
    this.isBusy.set(true);
    this.http
      .post(serviceUrl(switcher.service, switcher.method), request, {
        context: new HttpContext().set(SKIP_GLOBAL_ERROR, true),
      })
      .subscribe({
        next: () => this.reloadPage(),
        error: (error: unknown) => {
          this.isBusy.set(false);
          this.select.setPicked([optionOfReference(switcher.current)]);
          const problem = problemOf(error);
          this.alert.error(problem?.title ?? switcher.label, problem?.detail);
        },
      });
  }
}

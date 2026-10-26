import { afterNextRender, Component, computed, DestroyRef, ElementRef, inject, Injector, input, linkedSignal } from '@angular/core';
import {
  form,
  FormField,
  type LimitKey,
  MAX,
  MAX_NUMBER,
  metadata,
  MIN,
  MIN_NUMBER,
  readonly,
  REQUIRED,
  type SchemaPath,
  validate,
} from '@angular/forms/signals';
import { LocaleService } from '../../app/locale.service';
import { AmountBoxComponent } from '../../controls/amount-box.component';
import { FIELD_DESCRIPTION } from '../../controls/field-description';
import { FIELD_LOOK } from '../../controls/field-look';
import { NumberBoxComponent } from '../../controls/number-box.component';
import { TextBoxComponent } from '../../controls/text-box.component';
import type { GridNumberEditDto } from '../../core/generated/contracts';
import { TemporalBoxComponent } from '../../datepicker/temporal-box.component';
import type { SelectOption } from '../../select/select-option';
import { optionOfReference } from '../../select/select-option';
import type { SelectSource } from '../../select/select-source';
import { SelectComponent } from '../../select/select.component';
import { editKindOf, type GridCellEdit } from '../grid-cell-edits';
import { keyOf } from '../grid-row';
import { GridService } from '../grid.service';
import type { GridCellContext } from './grid-cell-context';

/** The element of a cell's editor that takes the focus when it opens: a box, or the combobox of a temporal or a select. */
const CONTROL = 'input, [role="combobox"]';

/**
 * The editor of a cell edited in place: one control of the form's own, bound by `[formField]` to a field over the
 * edit's value — a text box for text, e-mail and url, a number box for a number, an amount box for money and percent,
 * the temporal box for a date, a date and time or a time, and the select over the column's options — required when the
 * column is, drawn in the cell's look: filling the cell with no box of its own. A number box takes the step, the bounds
 * and the stepper of the column's `numberEdit`, the box the grid's form declares at its path; its presses and its arrow
 * keys change the value the editor holds and post nothing. It is named by the column's label,
 * takes the focus when it opens, typing the key that started the edit into its box, and gives it back to the table when
 * the edit ends. `Enter` commits and moves down, `Tab` and `Shift+Tab` commit and edit the row's next or previous
 * editable cell, `Escape` cancels: the editor takes these keys before its control and the table do. A refused save
 * draws its messages under the cell, the control invalid and described by them; the control takes no input while the
 * save runs.
 */
@Component({
  selector: 'ui-grid-cell-editor',
  imports: [AmountBoxComponent, FormField, NumberBoxComponent, SelectComponent, TemporalBoxComponent, TextBoxComponent],
  providers: [
    { provide: FIELD_DESCRIPTION, useFactory: () => inject(GridCellEditorComponent).describedBy },
    { provide: FIELD_LOOK, useValue: 'cell' },
  ],
  template: `
    @switch (kind()) {
      @case ('select') {
        <ui-select
          [model]="selectSource()"
          [picked]="picked()"
          [clearable]="!column().isRequired"
          [formField]="keyField"
          (valueChange)="keep($event)"
        />
      }
      @case ('number') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-number-box
          [id]="id"
          [step]="numberEdit()?.step"
          [steppable]="numberEdit()?.hasStepper ?? false"
          [formField]="numberField"
          (valueChange)="keep($event)"
        />
      }
      @case ('money') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-amount-box
          [id]="id"
          kind="money"
          [locale]="locale()"
          [currency]="currency()"
          [digits]="digits()"
          [formField]="numberField"
          (valueChange)="keep($event)"
        />
      }
      @case ('percent') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-amount-box
          [id]="id"
          kind="percent"
          [locale]="locale()"
          [digits]="digits()"
          [formField]="numberField"
          (valueChange)="keep($event)"
        />
      }
      @case ('date') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-temporal-box
          [id]="id"
          kind="date"
          [temporalType]="column().temporalType ?? null"
          [clearable]="!column().isRequired"
          [formField]="textField"
          (valueChange)="keep($event)"
        />
      }
      @case ('datetime') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-temporal-box
          [id]="id"
          kind="datetime"
          [temporalType]="column().temporalType ?? null"
          [clearable]="!column().isRequired"
          [formField]="textField"
          (valueChange)="keep($event)"
        />
      }
      @case ('time') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-temporal-box [id]="id" kind="time" [clearable]="!column().isRequired" [formField]="textField" (valueChange)="keep($event)" />
      }
      @case ('email') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-text-box [id]="id" kind="email" [formField]="textField" (valueChange)="keep($event)" />
      }
      @case ('url') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-text-box [id]="id" kind="url" [formField]="textField" (valueChange)="keep($event)" />
      }
      @case ('text') {
        <label class="sr-only" [for]="id">{{ label() }}</label>
        <ui-text-box [id]="id" [formField]="textField" (valueChange)="keep($event)" />
      }
    }
    @if (messages().length > 0) {
      <div
        class="absolute left-0 top-full z-30 min-w-full w-max max-w-80 mt-px px-2 py-1 rounded border border-line bg-surface whitespace-normal shadow"
      >
        @for (message of messages(); track $index) {
          <p class="hue-red m-0 text-xs text-(--hue-ink)" role="alert" [id]="id + '-error-' + $index">{{ message }}</p>
        }
      </div>
    }
  `,
  host: { class: 'relative block h-full', '[attr.aria-busy]': 'isSaving() ? "true" : null' },
})
export class GridCellEditorComponent {
  private static next = 0;

  private readonly grid = inject(GridService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  protected readonly locale = inject(LocaleService).current;

  /** The cell being edited. */
  readonly cell = input.required<GridCellContext>();

  protected readonly id = `ui-grid-cell-editor-${++GridCellEditorComponent.next}`;
  protected readonly column = computed(() => this.cell().column);
  protected readonly kind = computed(() => editKindOf(this.column()));
  protected readonly label = computed(() => this.column().label ?? this.column().path ?? '');
  /** The number box of a number cell as the grid's form declares its element; none for another column. */
  protected readonly numberEdit = computed<GridNumberEditDto | null>(() => this.column().numberEdit ?? null);

  /** The edit of this cell, none once it ended. */
  private readonly edit = computed<GridCellEdit | null>(() => {
    const edit = this.grid.edits.current();
    return edit && edit.key === keyOf(this.cell().row) && edit.path === this.column().path ? edit : null;
  });
  protected readonly isSaving = computed(() => this.edit()?.isSaving ?? false);

  /** What the refused save said at the column's path, a line each. */
  protected readonly messages = computed(() =>
    (this.edit()?.messages ?? []).flatMap(message => {
      const said = message.title ?? message.detail;
      return said ? [said] : [];
    }),
  );

  /** The ids of the messages under the cell, which describe the control; null while there is none. */
  readonly describedBy = computed(() =>
    this.messages().length
      ? this.messages()
          .map((_, index) => `${this.id}-error-${index}`)
          .join(' ')
      : null,
  );

  /** The field of a text, e-mail, url or temporal cell. */
  protected readonly textField = form(
    linkedSignal(() => {
      const value = this.edit()?.value;
      return typeof value === 'string' ? value : null;
    }),
    root => this.fieldRules(root),
  );
  /** The field of a number, money or percent cell. */
  protected readonly numberField = form(
    linkedSignal(() => {
      const value = this.edit()?.value;
      return typeof value === 'number' ? value : null;
    }),
    root => this.numberRules(root),
  );
  /** The field of a select's key. */
  protected readonly keyField = form(
    linkedSignal<unknown>(() => this.edit()?.value ?? null),
    root => this.fieldRules(root),
  );

  protected readonly currency = computed(() => {
    const cell = this.column().cell;
    return cell.kind === 'GridCellMoney' ? (cell.currency ?? null) : null;
  });
  protected readonly digits = computed(() => {
    const cell = this.column().cell;
    return cell.kind === 'GridCellMoney' || cell.kind === 'GridCellPercent' ? cell.decimals : undefined;
  });

  private readonly options = computed<readonly SelectOption[]>(() => (this.column().options ?? []).map(optionOfReference));
  protected readonly selectSource = computed<SelectSource>(() => ({ label: this.label(), options: this.options() }));
  protected readonly picked = computed<readonly SelectOption[]>(() => {
    const key = this.keyField().value();
    const option = this.options().find(o => o.id === key);
    return option ? [option] : [];
  });

  constructor() {
    // The table the focus returns to, found while the editor stands in it
    let table: HTMLElement | null = null;
    const onKeyDown = (event: KeyboardEvent) => this.onKeyDown(event);
    // Capturing: the editor's keys are taken before its control opens a picker on `Enter` and before the table moves
    this.host.addEventListener('keydown', onKeyDown, true);
    afterNextRender(
      () => {
        table = this.host.closest<HTMLElement>('[role="grid"]');
        this.focusControl();
      },
      { injector: inject(Injector) },
    );
    inject(DestroyRef).onDestroy(() => {
      this.host.removeEventListener('keydown', onKeyDown, true);
      const focused = this.host.ownerDocument.activeElement;
      if (!focused || focused === this.host.ownerDocument.body || this.host.contains(focused)) {
        table?.focus({ preventScroll: true });
      }
    });
  }

  /** What the control holds now, for the edit to keep. */
  protected keep(value: unknown): void {
    this.grid.edits.update(value);
  }

  private onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      if (!this.hasParseErrors()) {
        void this.grid.edits.commit('down');
      }
    } else if (event.key === 'Tab') {
      if (!this.hasParseErrors()) {
        void this.grid.edits.commit(event.shiftKey ? 'previous' : 'next');
      }
    } else if (event.key === 'Escape') {
      this.grid.edits.cancel();
    } else {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
  }

  private hasParseErrors(): boolean {
    return (
      this.numberField()
        .errors()
        .some(err => err.kind === 'parse') ||
      this.textField()
        .errors()
        .some(err => err.kind === 'parse')
    );
  }

  /** Focuses the control and types into its box the key that started the edit, the caret after it. */
  private focusControl(): void {
    const control = this.host.querySelector<HTMLElement>(CONTROL);
    control?.focus({ preventScroll: true });
    const typed = this.edit()?.typed;
    if (typed === undefined || !(control instanceof HTMLInputElement)) {
      return;
    }
    control.value = typed;
    control.dispatchEvent(new Event('input', { bubbles: true }));
    if (control.type !== 'number') {
      control.setSelectionRange(typed.length, typed.length);
    }
  }

  /**
   * The rules of the field over the edit's value, narrowed to what its control takes: read only while the save runs,
   * required when the column is, and invalid by the messages of a refused save.
   */
  private fieldRules(root: SchemaPath<unknown>): void {
    readonly(root, { when: () => this.isSaving() });
    metadata(root, REQUIRED, () => this.column().isRequired);
    validate(root, () => this.messages().map(message => ({ kind: 'server', message })));
  }

  /** The number field's rules: the field's own, and the column's bounds as a form's number field takes its element's. */
  private numberRules(root: SchemaPath<unknown>): void {
    this.fieldRules(root);
    metadata(root, MIN_NUMBER, () => numberBound(this.numberEdit()?.min));
    metadata(root, MAX_NUMBER, () => numberBound(this.numberEdit()?.max));
    // Limit selection keys need a cast over an unknown-typed field
    metadata(root, MIN, () => MIN_NUMBER as LimitKey<unknown>);
    metadata(root, MAX, () => MAX_NUMBER as LimitKey<unknown>);
  }
}

/** A bound of the column's box as the field takes it: none unless it is a number. */
function numberBound(value: unknown): number | undefined {
  return typeof value === 'number' ? value : undefined;
}

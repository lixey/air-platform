import { afterNextRender, Component, computed, DestroyRef, ElementRef, inject, Injector, input, viewChild } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import { AmountBoxComponent } from '../../controls/amount-box.component';
import { FIELD_DESCRIPTION } from '../../controls/field-description';
import { FIELD_LOOK } from '../../controls/field-look';
import { NumberBoxComponent } from '../../controls/number-box.component';
import { PhoneBoxComponent } from '../../controls/phone-box.component';
import { TextBoxComponent } from '../../controls/text-box.component';
import type { GridNumberEditDto } from '../../core/generated/contracts';
import { TemporalBoxComponent } from '../../datepicker/temporal-box.component';
import { OverlayService } from '../../overlay/overlay.service';
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
 * The editor of a cell edited in place: one control bound directly to the edit's value — a text box for text, e-mail
 * and url, a phone box for a phone, a number box for a number, an amount box for money and percent, the temporal box
 * for a date, a date and time or a time, and the select over the column's options — required when the column is, drawn
 * in the cell's look: filling the cell with no box of its own. A number box takes the step, the bounds and the stepper
 * of the column's `numberEdit`, the box the grid's form declares at its path; its presses and its arrow keys change the
 * value the editor holds and post nothing. It is named by the column's label, takes the focus when it opens, typing the
 * key that started the edit into its box, and gives it back to the table when the edit ends. `Enter` commits and moves
 * down, `Tab` and `Shift+Tab` commit and edit the row's next or previous editable cell, `Escape` cancels: the editor
 * takes these keys before its control and the table do. A refused save draws its messages under the cell, the control
 * invalid and described by them; the control takes no input while the save runs.
 */
@Component({
  selector: 'ui-grid-cell-editor',
  imports: [AmountBoxComponent, NumberBoxComponent, PhoneBoxComponent, SelectComponent, TemporalBoxComponent, TextBoxComponent],
  providers: [
    { provide: FIELD_DESCRIPTION, useFactory: () => inject(GridCellEditorComponent).describedBy },
    { provide: FIELD_LOOK, useValue: 'cell' },
  ],
  template: `
    <label class="sr-only" [for]="id">{{ label() }}</label>
    @switch (kind()) {
      @case ('select') {
        <ui-select
          [id]="id"
          [model]="selectSource()"
          [picked]="picked()"
          [clearable]="!column().isRequired"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="editValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('number') {
        <ui-number-box
          [id]="id"
          [step]="numberEdit()?.step"
          [steppable]="numberEdit()?.hasStepper ?? false"
          [min]="minNumber()"
          [max]="maxNumber()"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="numberValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('money') {
        <ui-amount-box
          [id]="id"
          kind="money"
          [locale]="locale()"
          [currency]="currency()"
          [digits]="digits()"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="numberValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('percent') {
        <ui-amount-box
          [id]="id"
          kind="percent"
          [locale]="locale()"
          [digits]="digits()"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="numberValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('date') {
        <ui-temporal-box
          [id]="id"
          kind="date"
          [temporalType]="column().temporalType ?? null"
          [clearable]="!column().isRequired"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="textValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('datetime') {
        <ui-temporal-box
          [id]="id"
          kind="datetime"
          [temporalType]="column().temporalType ?? null"
          [clearable]="!column().isRequired"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="textValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('time') {
        <ui-temporal-box
          [id]="id"
          kind="time"
          [clearable]="!column().isRequired"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="textValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('phone') {
        <ui-phone-box
          [id]="id"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="textValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('email') {
        <ui-text-box
          [id]="id"
          kind="email"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="textValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('url') {
        <ui-text-box
          [id]="id"
          kind="url"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="textValue()"
          (valueChange)="keep($event)"
        />
      }
      @case ('text') {
        <ui-text-box
          [id]="id"
          [readonly]="isSaving()"
          [required]="column().isRequired"
          [invalid]="invalid()"
          [value]="textValue()"
          (valueChange)="keep($event)"
        />
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
  private readonly overlay = inject(OverlayService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  protected readonly locale = inject(LocaleService).current;
  private readonly numberBox = viewChild(NumberBoxComponent);
  private readonly amountBox = viewChild(AmountBoxComponent);
  private destroyed = false;

  /** The cell being edited. */
  readonly cell = input.required<GridCellContext>();

  protected readonly id = `ui-grid-cell-editor-${++GridCellEditorComponent.next}`;
  protected readonly column = computed(() => this.cell().column);
  protected readonly kind = computed(() => editKindOf(this.column()));
  protected readonly label = computed(() => this.column().label ?? this.column().path ?? '');
  /** The number box of a number cell as the grid's form declares its element; none for another column. */
  protected readonly numberEdit = computed<GridNumberEditDto | null>(() => this.column().numberEdit ?? null);
  protected readonly minNumber = computed(() => numberBound(this.numberEdit()?.min));
  protected readonly maxNumber = computed(() => numberBound(this.numberEdit()?.max));

  /** The edit of this cell, none once it ended. */
  private readonly edit = computed<GridCellEdit | null>(() => {
    const edit = this.grid.edits.current();
    return edit && edit.key === keyOf(this.cell().row) && edit.path === this.column().path ? edit : null;
  });
  protected readonly editValue = computed(() => this.edit()?.value ?? null);
  protected readonly textValue = computed(() => {
    const value = this.edit()?.value;
    return typeof value === 'string' ? value : null;
  });
  protected readonly numberValue = computed(() => {
    const value = this.edit()?.value;
    return typeof value === 'number' ? value : null;
  });
  protected readonly isSaving = computed(() => this.edit()?.isSaving ?? false);

  /** What the refused save said at the column's path, a line each. */
  protected readonly messages = computed(() =>
    (this.edit()?.messages ?? []).flatMap(message => {
      const said = message.title ?? message.detail;
      return said ? [said] : [];
    }),
  );

  private readonly hasParseError = computed(
    () => (this.numberBox()?.parseErrors().length ?? 0) > 0 || (this.amountBox()?.parseErrors().length ?? 0) > 0,
  );
  private readonly isOutOfBounds = computed(() => {
    const value = this.numberValue();
    if (value === null) {
      return false;
    }
    const min = this.minNumber();
    const max = this.maxNumber();
    return (min !== undefined && value < min) || (max !== undefined && value > max);
  });
  protected readonly invalid = computed(() => this.messages().length > 0 || this.hasParseError() || this.isOutOfBounds());

  /** The ids of the messages under the cell, which describe the control; null while there is none. */
  readonly describedBy = computed(() =>
    this.messages().length
      ? this.messages()
          .map((_, index) => `${this.id}-error-${index}`)
          .join(' ')
      : null,
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
    const key = this.editValue();
    const option = this.options().find(o => o.id === key);
    return option ? [option] : [];
  });

  constructor() {
    // The table the focus returns to, found while the editor stands in it
    let table: HTMLElement | null = null;
    const onKeyDown = (event: KeyboardEvent) => this.onKeyDown(event);
    const onFocusOut = (event: FocusEvent) => this.onFocusOut(event);
    // Capturing: the editor's keys are taken before its control opens a picker on `Enter` and before the table moves
    this.host.addEventListener('keydown', onKeyDown, true);
    this.host.addEventListener('focusout', onFocusOut);
    afterNextRender(
      () => {
        table = this.host.closest<HTMLElement>('[role="grid"]');
        this.focusControl();
      },
      { injector: inject(Injector) },
    );
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.host.removeEventListener('keydown', onKeyDown, true);
      this.host.removeEventListener('focusout', onFocusOut);
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

  private onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (next instanceof Node && this.host.contains(next)) {
      return;
    }
    queueMicrotask(() => {
      if (
        this.destroyed ||
        this.host.contains(this.host.ownerDocument.activeElement) ||
        this.hasChildOverlay() ||
        this.grid.edits.isConfirming ||
        this.isSaving() ||
        this.edit() === null ||
        this.hasParseError()
      ) {
        return;
      }
      void this.grid.edits.commit('stay');
    });
  }

  private hasChildOverlay(): boolean {
    return this.overlay.stack().some(ref => ref.anchor !== null && this.host.contains(ref.anchor));
  }

  private onKeyDown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      if (!this.hasParseError()) {
        void this.grid.edits.commit('down');
      }
    } else if (event.key === 'Tab') {
      if (!this.hasParseError()) {
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
      control.setSelectionRange(control.value.length, control.value.length);
    }
  }
}

/** A bound of the column's box as the field takes it: none unless it is a number. */
function numberBound(value: unknown): number | undefined {
  return typeof value === 'number' ? value : undefined;
}

import { computed, inject, Injector, Service, signal } from '@angular/core';
import { type Observable, Subject } from 'rxjs';
import { LocaleService } from '../app/locale.service';
import { datePartOf, pad2, type TemporalKind, timePartOf } from '../controls/temporal-text';
import { TextsService, type UiTextKey } from '../core/texts.service';
import { wallOf } from '../core/time-zone';
import type { OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { serviceMenu } from '../panels/service-menu';
import { type CalendarView, type IsoDate, localIsoDate } from './calendar-month';
import { formatTemporalText, parseTemporalText } from './date-text';

/** What a temporal control opens the picker with. */
export interface DatePickerRequest {
  readonly kind: TemporalKind;
  /**
   * The editor text of the value (`controls/temporal-text.ts`), empty for none:
   * where the picker starts, and what a commit that changed nothing equals.
   */
  readonly text: string;
  /**
   * The minutes between two options of the minutes grid, a whole number from
   * 1 to 60; 5 when absent. Another step throws when the minutes are listed.
   */
  readonly minuteStep?: number | null;
  /** Whether the picker may write no value: its `Clear` button, and `Enter` on an empty input. */
  readonly clearable: boolean;
  /** The printable key the closed control took, which the input starts with in place of the value. */
  readonly seed?: string;
}

/** What the picker's card shows below its input: a view of the calendar, or the hours or the minutes of the time. */
export type PickerView = CalendarView | 'hours' | 'minutes';

const DEFAULT_MINUTE_STEP = 5;

/** What the picker's card is called, by what it picks. */
const NAMES = { date: 'ChooseDate', datetime: 'ChooseDatetime', time: 'ChooseTime' } satisfies Record<TemporalKind, UiTextKey>;

/**
 * One temporal control's picker, provided by the control that opens it and injected by the overlay body it opens. The
 * picker works on a draft, an editor text (`controls/temporal-text.ts`) that starts as the control's value: picking a
 * day of a date commits it; picking a day of a date and time changes the draft and moves the card to the hours, picking
 * an hour changes it and moves the card to the minutes, and picking a minute commits; `Enter` in the input commits the
 * text typed, or marks it invalid when it is no real value. A commit emits the editor text only when it differs from
 * the value the picker opened with, and closes the picker; `close`, `Escape` and a click outside discard the draft.
 */
@Service({ autoProvided: false })
export class DatePickerService {
  private readonly overlay = inject(OverlayService);
  private readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService);
  /** The picker's card, one at a time. */
  private readonly menu = this.overlay.menu();

  private readonly _request = signal<DatePickerRequest>({ kind: 'date', text: '', clearable: true });
  readonly request = this._request.asReadonly();

  /** Today by the user's clock, read when the picker opens: the day marked current, and the century of a two-digit year. */
  private readonly _today = signal<IsoDate>(localIsoDate(new Date(), this.locale.zone()));
  readonly today = this._today.asReadonly();

  /** The value being picked, as an editor text; empty for none. */
  private readonly _draft = signal('');
  readonly draft = this._draft.asReadonly();

  /** The input's text: the draft in the locale's format, or what the user typed over it. */
  private readonly _text = signal('');
  readonly text = this._text.asReadonly();

  /** Whether `Enter` found the input's text no real value; typing clears it. */
  private readonly _isInvalid = signal(false);
  readonly isInvalid = this._isInvalid.asReadonly();

  /** The day the calendar's keys move and its tab stop stands on: the draft's day, a day typed, else today, every open. */
  private readonly _active = signal<IsoDate>(this.today());
  readonly active = this._active.asReadonly();

  /** Emits the editor text of each commit that changed the value, empty for a cleared one. */
  private readonly _committed = new Subject<string>();
  readonly committed: Observable<string> = this._committed.asObservable();

  /** Whether the picker is open, and the id of its card while it is: what the control's combobox says it controls. */
  readonly isOpen = this.menu.isOpen;
  readonly popupId = this.menu.id;

  /** What the card shows: the days on every open, the hours for a time of day. */
  private readonly _view = signal<PickerView>('days');
  readonly view = this._view.asReadonly();

  /** The field the picker is for, as its card's header says: the anchor's label, else the picker's name. */
  private readonly _label = signal('');
  readonly label = this._label.asReadonly();

  readonly kind = computed(() => this.request().kind);

  /** What the picker is called: its card, and the input the value is typed into. */
  readonly name = computed(() => this.texts.of(NAMES[this.kind()]));

  /** Whether the input holds text the user typed, rather than the draft it shows. */
  readonly isTyped = computed(() => this.text() !== this.shown(this.draft()));

  /** The day of the draft, the one the calendar shows selected; `null` for none and for a time of day. */
  readonly day = computed(() => this.dateOf(this.draft()));

  /** The hour and the minute of the draft, `null` for none and for a date. */
  readonly hour = computed(() => this.clockOf(this.draft())?.hour ?? null);
  readonly minute = computed(() => this.clockOf(this.draft())?.minute ?? null);

  /** The minutes grid: every step from `00`, and the draft's minute when it falls between two steps. */
  readonly minutes = computed(() => minuteOptions(this.request().minuteStep ?? DEFAULT_MINUTE_STEP, this.minute()));

  /**
   * Opens the picker as a menu from the anchor, over the value the request carries — or the key it was opened with —
   * the input taking focus; the body it mounts injects this very service. It opens on the value's day, and on today
   * for no value, whatever day an earlier open was left on; an open picker stays as it is.
   */
  open(request: DatePickerRequest, anchor: HTMLElement, injector: Injector): OverlayRef {
    if (this.menu.ref) {
      return this.menu.ref;
    }
    this._request.set(request);
    this._today.set(localIsoDate(new Date(), this.locale.zone()));
    this._active.set(this.today());
    this._view.set(request.kind === 'time' ? 'hours' : 'days');
    this._label.set(labelOf(anchor) || this.name());
    this.setDraft(request.text);
    if (request.seed) {
      this.typeText(request.seed);
    }

    const provided = { token: DatePickerService, instance: this, parent: injector };
    const menu = serviceMenu('datepicker', { kind: 'DatePickerOverlay' }, provided);
    return this.menu.open(menu.model, anchor, menu.injector, this.name(), { keepsOwnWidth: true });
  }

  /** Closes the picker and writes nothing. */
  close(): void {
    this.menu.close();
  }

  /** The user typed: the text is kept as typed, and a date it reads as becomes the active day. */
  typeText(text: string): void {
    this._text.set(text);
    this._isInvalid.set(false);
    const day = this.dateOf(this.parse(text));
    if (day) {
      this._active.set(day);
    }
  }

  /**
   * `Enter` in the input: commits the draft while the input shows it, the text typed when it reads as a real value —
   * the draft itself when the text says the same minute of it, so its seconds stay — no value for an empty input
   * where that is allowed, and otherwise marks the input invalid and keeps the picker open.
   */
  enterText(): void {
    const text = this.text();
    if (!this.isTyped()) {
      this.commit(this.draft());
      return;
    }
    if (!text.trim() && this.request().clearable) {
      this.commit('');
      return;
    }
    const typed = this.parse(text);
    if (typed === null) {
      this._isInvalid.set(true);
      return;
    }
    this.commit(this.draft().startsWith(typed) ? this.draft() : typed);
  }

  /** Makes a day the active one: the calendar shows its month and its tab stop stands on it. */
  moveActive(day: IsoDate): void {
    this._active.set(day);
  }

  /** Shows a view of the calendar or of the time in the card. */
  show(view: PickerView): void {
    this._view.set(view);
  }

  /**
   * A day picked: a date commits it; a date and time takes it in place of its date, keeping its time, and stays open
   * on the hours.
   */
  pickDay(day: IsoDate): void {
    if (this.kind() === 'date') {
      this.commit(day);
      return;
    }
    this.setDraft(`${day}T${this.draft().split('T')[1] ?? '00:00'}`);
    this._view.set('hours');
  }

  /** An hour picked: the draft's time becomes that hour at its minute, and the picker stays open on the minutes. */
  pickHour(hour: number): void {
    this.setDraft(this.withTime(hour, this.minute() ?? 0));
    this._view.set('minutes');
  }

  /** A minute picked: the draft's time becomes its hour at that minute, and the picker commits. */
  pickMinute(minute: number): void {
    this.commit(this.withTime(this.hour() ?? 0, minute));
  }

  /** `Today` or `Now`: commits the user's present day, time of day, or both. */
  pickNow(): void {
    const now = new Date();
    const wall = wallOf(now, this.locale.zone());
    const day = localIsoDate(now, this.locale.zone());
    const time = `${pad2(wall.hour)}:${pad2(wall.minute)}`;
    const present = { date: day, datetime: `${day}T${time}`, time } satisfies Record<TemporalKind, string>;
    this.commit(present[this.kind()]);
  }

  /** `Clear`: commits no value. */
  clear(): void {
    this.commit('');
  }

  private commit(text: string): void {
    if (text !== this.request().text) {
      this._committed.next(text);
    }
    this.menu.close();
  }

  /** A new draft: the input shows it and the calendar stands on its day. */
  private setDraft(draft: string): void {
    this._draft.set(draft);
    this._text.set(this.shown(draft));
    this._isInvalid.set(false);
    this._active.set(this.dateOf(draft) ?? this.active());
  }

  /** The draft's day, or the active one, at a time; a time of day alone for a time of day. */
  private withTime(hour: number, minute: number): string {
    const time = `${pad2(hour)}:${pad2(minute)}`;
    return this.kind() === 'time' ? time : `${this.day() ?? this.active()}T${time}`;
  }

  private shown(text: string): string {
    return formatTemporalText(text, this.kind(), this.locale.current());
  }

  private parse(text: string): string | null {
    return parseTemporalText(text, this.kind(), this.locale.current(), this.today());
  }

  private dateOf(text: string | null): IsoDate | null {
    return text && this.kind() !== 'time' ? datePartOf(text) : null;
  }

  /**
   * The hour and the minute of the draft's time, `HH:mm` of a time of day
   * or after the `T` of a date and time; `null` for a date and for no time.
   */
  private clockOf(text: string): { readonly hour: number; readonly minute: number } | null {
    const time = this.kind() === 'date' ? '' : timePartOf(text);
    if (!time) {
      return null;
    }
    const [hour, minute] = time.split(':').map(Number);
    return { hour, minute };
  }
}

/**
 * The text of the label that names the anchor, `<label for>` of a combobox, without what it hides from assistive
 * technology (the required mark); empty for an anchor no label names.
 */
function labelOf(anchor: HTMLElement): string {
  const label = 'labels' in anchor ? (anchor.labels as NodeListOf<HTMLLabelElement> | null)?.[0] : undefined;
  if (!label) {
    return '';
  }
  const copy = label.cloneNode(true) as HTMLElement;
  copy.querySelectorAll('[aria-hidden="true"]').forEach(hidden => hidden.remove());
  return copy.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}

/**
 * The minutes the grid lists: every `step` minutes from 00, and the minute the value holds when it falls between two
 * steps, in order. A step that is no whole number from 1 to 60 throws: the server refuses it where the element is
 * declared, so only a model built by hand reaches it; no step at all is `DEFAULT_MINUTE_STEP`, applied by the caller.
 */
function minuteOptions(step: number, held: number | null): readonly number[] {
  if (!Number.isInteger(step) || step < 1 || step > 60) {
    throw new Error(`minuteStep ${step} is not a whole number from 1 to 60`);
  }
  const minutes = Array.from({ length: Math.ceil(60 / step) }, (_, index) => index * step);
  return held === null || minutes.includes(held) ? minutes : [...minutes, held].sort((a, b) => a - b);
}

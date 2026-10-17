import { InjectionToken, type Signal } from '@angular/core';

/**
 * The ids of what describes a control — the help and the messages that stand under it — as a form item provides them;
 * a control writes them as its `aria-describedby`, and outside a form item it finds none.
 */
export const FIELD_DESCRIPTION = new InjectionToken<Signal<string | null>>('FIELD_DESCRIPTION');

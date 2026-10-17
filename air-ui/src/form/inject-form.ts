import { computed, inject, type Signal } from '@angular/core';
import { PanelsContext } from '../panels/panels-context';
import { FORM } from './form-panel-values';
import { FormService } from './form.service';

/**
 * The form a nav element acts on: the one the layout's `form` panel publishes as `FORM`, or the
 * one provided around the element where a form is mounted by hand; `null` when there is none.
 * A layout that publishes no form asks the layout it sits inside, so an element of a menu, whose
 * card is a layout of its own opened from the nav, finds the form of the nav.
 * Called in an injection context, like `inject`, so an application's own form-bar element finds
 * its form the way the library's do.
 */
export function injectForm(): Signal<FormService | null> {
  const context = inject(PanelsContext, { optional: true });
  const injected = inject(FormService, { optional: true });
  return computed(() => {
    for (let level = context; level; level = level.parent) {
      const form = level.of(FORM)();
      if (form) {
        return form;
      }
    }
    return injected;
  });
}

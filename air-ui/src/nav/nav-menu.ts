import { InjectionToken } from '@angular/core';

/** The menu a nav element stands in, as the menu's body gives it to its elements. */
export interface NavMenuHandle {
  /** Closes the menu when its opener said an action closes it; an element whose work outlasts a click calls it as it starts. */
  close(): void;
}

export const NAV_MENU = new InjectionToken<NavMenuHandle>('NAV_MENU');

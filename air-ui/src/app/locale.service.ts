import { computed, DOCUMENT, effect, inject, Service, signal } from '@angular/core';
import { browserZone, knownZone } from '../core/time-zone';
import { AuthService } from './auth/auth.service';

/** The culture before anything said one: the platform's neutral culture. */
const NEUTRAL_CULTURE = 'en-US';

/**
 * The culture and the clock the session reads: the signed-in user's language, before sign-in the login screen's, and
 * the profile's zone, before sign-in the browser's. The library formats numbers, dates and sizes in the culture and
 * shows instants on the clock, and the document's language follows the culture, so the browser reads, spells and
 * displays native fields in it after a reload and after a sign-in without one. Nothing for an application to declare.
 */
@Service()
export class LocaleService {
  private readonly auth = inject(AuthService);
  private readonly screen = signal<string | null>(null);

  readonly current = computed(() => this.auth.currentUser()?.language || this.screen() || NEUTRAL_CULTURE);

  /**
   * The user's clock: the profile's zone, the one the server resolved for the user in the active organisation, before
   * sign-in the browser's own; a zone the browser does not know reads as UTC. The one place the library knows it: every
   * instant it shows or reads, a stamped name's moment included, is on this clock.
   */
  readonly zone = computed(() => knownZone(this.auth.currentUser()?.timeZone || browserZone()));

  constructor() {
    const root = inject(DOCUMENT).documentElement;
    effect(() => (root.lang = this.current()));
  }

  /** What the login screen says its culture is, while no one is signed in. */
  useScreenCulture(culture: string): void {
    this.screen.set(culture);
  }
}

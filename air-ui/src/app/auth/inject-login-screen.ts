import { inject, signal, type Signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { LoginScreenDto } from '../../core/generated/contracts';
import { problemOf } from '../../core/problem';
import { TextsService } from '../../core/texts.service';
import { LocaleService } from '../locale.service';
import { AuthService } from './auth.service';

/** The model of the sign-in and change-password pages as a page holds it. */
export interface LoginScreen {
  /** The screen in the request's culture, once it arrived. */
  readonly screen: Signal<LoginScreenDto | null>;
  /** Why the screen did not arrive; empty while it loads and once it arrived. */
  readonly failure: Signal<string>;
}

/**
 * The texts of an authentication page, asked for once, in an injection context like `inject`: the page shows nothing
 * of its own before they arrive, and their culture becomes the document's language, so the browser reads, spells
 * and speaks the page in it.
 */
export function injectLoginScreen(): LoginScreen {
  const locale = inject(LocaleService);
  const texts = inject(TextsService);
  const screen = signal<LoginScreenDto | null>(null);
  const failure = signal('');

  inject(AuthService)
    .loginScreen()
    .pipe(takeUntilDestroyed())
    .subscribe({
      next: value => {
        if (value.culture) {
          locale.useScreenCulture(value.culture);
        }
        screen.set(value);
      },
      error: (error: unknown) => {
        const problem = problemOf(error);
        failure.set(problem?.title || problem?.detail || texts.of('RequestFailed'));
      },
    });

  return { screen: screen.asReadonly(), failure: failure.asReadonly() };
}

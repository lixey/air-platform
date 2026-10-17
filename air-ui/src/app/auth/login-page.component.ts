import type { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { BtnComponent } from '../../btn/btn.component';
import { TextBoxComponent } from '../../controls/text-box.component';
import type { LoginScreenDto } from '../../core/generated/contracts';
import { WORK_AREA } from '../../theme/css-classes';
import { AuthService } from './auth.service';
import { injectLoginScreen } from './inject-login-screen';
import { returnUrlOf } from './return-url';

/**
 * The sign-in page every application mounts through `AUTH_ROUTES`. Every word on it is the server's, in the request's
 * culture; a success goes to the local return address, or first to the change-password page when the profile
 * requires it, and leaves no history entry behind. The fields are read from the form itself on submit, so a value a
 * password manager filled counts whether or not it raised an input event.
 */
@Component({
  selector: 'ui-login-page',
  imports: [BtnComponent, TextBoxComponent],
  template: `
    @if (loginScreen.screen(); as screen) {
      <form
        class="m-auto flex w-full max-w-sm flex-col gap-4 ${WORK_AREA} rounded-2xl border border-line bg-surface p-8 text-ink shadow-xl"
        (submit)="signIn($event)"
      >
        <h1 class="text-xl font-semibold">{{ screen.title }}</h1>
        <div class="flex flex-col gap-1">
          <label for="ui-login-page-login">{{ screen.login }}</label>
          <ui-text-box [id]="'ui-login-page-login'" name="login" autocomplete="username" [required]="true" />
        </div>
        <div class="flex flex-col gap-1">
          <label for="ui-login-page-password">{{ screen.password }}</label>
          <ui-text-box [id]="'ui-login-page-password'" name="password" kind="password" autocomplete="current-password" [required]="true" />
        </div>
        @if (error()) {
          <p role="alert" class="text-warning">{{ error() }}</p>
        }
        <button
          ui-btn
          type="submit"
          color="blue"
          variant="solid"
          [disabled]="isBusy()"
          [loading]="isBusy()"
          class="w-full justify-center rounded-lg p-3 font-semibold"
        >
          {{ screen.submit }}
        </button>
      </form>
    } @else {
      <p role="status">{{ loginScreen.failure() || '…' }}</p>
    }
  `,
  host: { class: 'flex h-full min-h-0 items-center justify-center overflow-auto bg-page p-4 text-ink' },
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly loginScreen = injectLoginScreen();

  protected readonly error = signal('');
  protected readonly isBusy = signal(false);

  protected signIn(event: Event): void {
    event.preventDefault();
    const screen = this.loginScreen.screen();
    const form = event.target as HTMLFormElement;
    if (this.isBusy() || !screen || !form.reportValidity()) {
      return;
    }
    const data = new FormData(form);
    this.isBusy.set(true);
    this.error.set('');
    this.auth
      .login(String(data.get('login') ?? ''), String(data.get('password') ?? ''))
      .pipe(finalize(() => this.isBusy.set(false)))
      .subscribe({
        next: session => {
          form.reset();
          const target = returnUrlOf(this.route.snapshot.queryParamMap.get('returnUrl'));
          if (session.user.mustChangePassword) {
            void this.router.navigate(['/change-password'], { queryParams: { returnUrl: target }, replaceUrl: true });
          } else {
            void this.router.navigateByUrl(target, { replaceUrl: true });
          }
        },
        error: (error: HttpErrorResponse) => this.error.set(failureOf(error, screen)),
      });
  }
}

/** What the page says when the server refused to sign in: the screen's own words for each outcome. */
function failureOf(error: HttpErrorResponse, screen: LoginScreenDto): string {
  switch (error.status) {
    case 401:
      return screen.invalidCredentials;
    case 429:
      return screen.tooManyAttempts;
    default:
      return screen.unavailable;
  }
}

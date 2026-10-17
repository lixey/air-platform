import type { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { BtnComponent } from '../../btn/btn.component';
import { TextBoxComponent } from '../../controls/text-box.component';
import type { LoginScreenDto } from '../../core/generated/contracts';
import { problemOf } from '../../core/problem';
import { WORK_AREA } from '../../theme/css-classes';
import { AuthService } from './auth.service';
import { injectLoginScreen } from './inject-login-screen';
import { returnUrlOf } from './return-url';

/**
 * The change-password page every application mounts through `AUTH_ROUTES`, behind `authGuard`. Its words are the
 * login screen's, since the metadata is refused while a password change is required; the new password is typed twice
 * and a mismatch never reaches the server. A success replaces the profile with the new session's and goes to the
 * local return address without a history entry; signing out is always open. A hidden username field tells a password
 * manager whose password changes.
 */
@Component({
  selector: 'ui-change-password-page',
  imports: [BtnComponent, TextBoxComponent],
  template: `
    @if (loginScreen.screen(); as screen) {
      <form
        class="m-auto flex w-full max-w-sm flex-col gap-4 ${WORK_AREA} rounded-2xl border border-line bg-surface p-8 text-ink shadow-xl"
        (submit)="changePassword($event)"
      >
        <h1 class="text-xl font-semibold">{{ screen.changePasswordTitle }}</h1>
        <input name="username" autocomplete="username" [value]="login()" readonly hidden />
        <div class="flex flex-col gap-1">
          <label for="ui-change-password-page-current-password">{{ screen.currentPassword }}</label>
          <ui-text-box
            [id]="'ui-change-password-page-current-password'"
            name="currentPassword"
            kind="password"
            autocomplete="current-password"
            [required]="true"
          />
        </div>
        <div class="flex flex-col gap-1">
          <label for="ui-change-password-page-new-password">{{ screen.newPassword }}</label>
          <ui-text-box
            [id]="'ui-change-password-page-new-password'"
            name="newPassword"
            kind="password"
            autocomplete="new-password"
            [required]="true"
          />
        </div>
        <div class="flex flex-col gap-1">
          <label for="ui-change-password-page-repeat-password">{{ screen.repeatPassword }}</label>
          <ui-text-box
            [id]="'ui-change-password-page-repeat-password'"
            name="repeatPassword"
            kind="password"
            autocomplete="new-password"
            [required]="true"
          />
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
          {{ screen.changePasswordSubmit }}
        </button>
        <button ui-btn type="button" variant="soft" [disabled]="isBusy()" (click)="signOut()" class="w-full justify-center rounded-lg p-3">
          {{ screen.signOut }}
        </button>
      </form>
    } @else {
      <p role="status">{{ loginScreen.failure() || '…' }}</p>
    }
  `,
  host: { class: 'flex h-full min-h-0 items-center justify-center overflow-auto bg-page p-4 text-ink' },
})
export class ChangePasswordPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly loginScreen = injectLoginScreen();

  protected readonly error = signal('');
  protected readonly isBusy = signal(false);

  protected readonly login = computed(() => this.auth.currentUser()?.login ?? '');

  protected changePassword(event: Event): void {
    event.preventDefault();
    const screen = this.loginScreen.screen();
    const form = event.target as HTMLFormElement;
    if (this.isBusy() || !screen || !form.reportValidity()) {
      return;
    }
    const data = new FormData(form);
    const newPassword = String(data.get('newPassword') ?? '');
    if (newPassword !== String(data.get('repeatPassword') ?? '')) {
      this.error.set(screen.passwordsDiffer);
      return;
    }
    this.isBusy.set(true);
    this.error.set('');
    this.auth
      .changePassword(String(data.get('currentPassword') ?? ''), newPassword)
      .pipe(finalize(() => this.isBusy.set(false)))
      .subscribe({
        next: () => {
          form.reset();
          void this.router.navigateByUrl(returnUrlOf(this.route.snapshot.queryParamMap.get('returnUrl')), { replaceUrl: true });
        },
        error: (error: HttpErrorResponse) => this.error.set(failureOf(error, screen)),
      });
  }

  protected signOut(): void {
    this.auth.logout();
  }
}

/**
 * What the page says when the server refused the change: the password policy's own message for a rejected password,
 * the screen's words for a wrong current password and for everything else.
 */
function failureOf(error: HttpErrorResponse, screen: LoginScreenDto): string {
  switch (error.status) {
    case 400: {
      const problem = problemOf(error);
      return problem?.detail || problem?.title || screen.unavailable;
    }
    case 401:
      return screen.invalidCredentials;
    default:
      return screen.unavailable;
  }
}

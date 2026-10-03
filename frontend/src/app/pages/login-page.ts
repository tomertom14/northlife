import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthStore } from '../auth/auth-store';
import { GoogleButton } from '../auth/google-button';
import { SignInResult, isAuthenticated } from '../auth/auth.models';
import { destinationAfterSignIn } from '../auth/sign-in-navigation';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule, RouterLink, GoogleButton],
  templateUrl: './login-page.html',
  styleUrl: './auth-page.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  email = '';
  password = '';
  code = '';
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  /** Set once the password (or Google) step passed for an account with 2FA. */
  readonly mfaToken = signal<string | null>(null);
  readonly useBackupCode = signal(false);
  readonly expired = this.route.snapshot.queryParamMap.get('expired') === '1';

  submit(): void {
    if (!this.email.trim() || !this.password) {
      this.errorMessage.set('כתבו אימייל וסיסמה.');
      return;
    }

    this.begin();
    this.auth.login({ email: this.email, password: this.password }).subscribe({
      next: (result) => this.handle(result),
      error: (error: HttpErrorResponse) => this.fail(error, 'האימייל או הסיסמה לא נכונים.'),
    });
  }

  submitCode(): void {
    const ticket = this.mfaToken();
    if (!ticket || !this.code.trim()) {
      this.errorMessage.set(this.useBackupCode() ? 'כתבו קוד גיבוי.' : 'כתבו את הקוד בן 6 הספרות מהאפליקציה.');
      return;
    }

    this.begin();
    this.auth.completeMfa(ticket, this.code.trim()).subscribe({
      next: (response) => this.handle(response),
      error: (error: HttpErrorResponse) => {
        if (error.error?.code === 'mfa_expired') this.mfaToken.set(null);
        this.fail(error, error.error?.code === 'mfa_expired'
          ? 'עבר זמן רב מדי. התחברו שוב.'
          : 'הקוד לא נכון או שכבר נעשה בו שימוש. נסו את הקוד הבא.');
        this.code = '';
      },
    });
  }

  signInWithGoogle(idToken: string): void {
    this.begin();
    this.auth.google(idToken).subscribe({
      next: (result) => this.handle(result),
      error: (error: HttpErrorResponse) => this.fail(error, 'ההתחברות עם Google נכשלה. נסו שוב.'),
    });
  }

  startOver(): void {
    this.mfaToken.set(null);
    this.code = '';
    this.errorMessage.set('');
  }

  private handle(result: SignInResult): void {
    this.submitting.set(false);
    if (isAuthenticated(result)) {
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      void this.router.navigateByUrl(destinationAfterSignIn(result.user, returnUrl));
    } else if (result.status === 'mfa_required') {
      this.mfaToken.set(result.mfaToken);
      this.code = '';
    } else {
      void this.router.navigate(['/manage/complete-profile'], {
        state: { signupToken: result.signupToken, email: result.email, fullName: result.fullName },
      });
    }
  }

  private begin(): void {
    this.submitting.set(true);
    this.errorMessage.set('');
  }

  private fail(error: HttpErrorResponse, unauthorized: string): void {
    this.submitting.set(false);
    this.errorMessage.set(
      error.error?.code === 'mfa_locked'
        ? 'נרשמו יותר מדי קודים שגויים. מטעמי אבטחה אפשר לנסות שוב בעוד 15 דקות.'
        : error.status === 429
        ? 'היו יותר מדי ניסיונות. נסו שוב בעוד דקה.'
        : error.status === 401 || error.status === 400
          ? unauthorized
          : 'לא הצלחנו להתחבר כרגע. בדקו את החיבור ונסו שוב.',
    );
  }
}

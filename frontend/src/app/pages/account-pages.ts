import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthApi } from '../auth/auth-api';
import { AuthStore } from '../auth/auth-store';
import { destinationAfterSignIn } from '../auth/sign-in-navigation';
import { problemFieldErrors } from '../shared/problem-details';

const PASSWORD_RULE = 'הסיסמה צריכה להכיל 10–128 תווים, אות גדולה, אות קטנה ומספר.';

function isStrongPassword(password: string): boolean {
  return password.length >= 10 && password.length <= 128 && /[A-Z]/.test(password) && /[a-z]/.test(password) && /\d/.test(password);
}

/** Landing page of the emailed verification link. */
@Component({
  selector: 'app-verify-email-page',
  imports: [RouterLink],
  template: `
    <section class="auth">
      <div class="auth-intro"><h1>אימות אימייל</h1></div>
      <div class="auth-form result" aria-live="polite">
        @switch (state()) {
          @case ('working') { <p class="step-lead">מאמתים את הכתובת…</p> }
          @case ('done') {
            <p class="notice notice-success">כתובת האימייל אומתה. אפשר לשלוח אירועים לאישור.</p>
            <a class="btn btn-primary" [routerLink]="auth.isAuthenticated() ? '/manage/dashboard' : '/manage/login'">
              {{ auth.isAuthenticated() ? 'ללוח האירועים' : 'לכניסה' }}
            </a>
          }
          @default {
            <p class="notice notice-error">הקישור פג תוקף או שכבר נעשה בו שימוש.</p>
            <p class="step-lead">אחרי ההתחברות אפשר לבקש קישור חדש מלוח האירועים.</p>
            <a class="btn" routerLink="/manage/login">לכניסה</a>
          }
        }
      </div>
    </section>
  `,
  styleUrl: './auth-page.scss',
})
export class VerifyEmailPage implements OnInit {
  private readonly api = inject(AuthApi);
  private readonly route = inject(ActivatedRoute);
  readonly auth = inject(AuthStore);
  readonly state = signal<'working' | 'done' | 'failed'>('working');

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.state.set('failed');
      return;
    }
    this.api.verifyEmail(token).subscribe({
      next: () => {
        this.state.set('done');
        if (this.auth.hasValidSession()) this.auth.refreshUser().subscribe({ error: () => undefined });
      },
      error: () => this.state.set('failed'),
    });
  }
}

@Component({
  selector: 'app-forgot-password-page',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="auth">
      <div class="auth-intro">
        <h1>שכחתם סיסמה?</h1>
        <p>כתבו את האימייל של החשבון ונשלח קישור לבחירת סיסמה חדשה.</p>
      </div>
      <form class="auth-form" (ngSubmit)="submit()">
        @if (sent()) {
          <p class="notice notice-success" role="status">אם קיים חשבון עם הכתובת הזו, שלחנו אליה קישור. הקישור תקף ל-30 דקות.</p>
          <a class="btn" routerLink="/manage/login">חזרה לכניסה</a>
        } @else {
          <div class="field">
            <label for="forgot-email">אימייל</label>
            <input class="input" id="forgot-email" type="email" name="email" inputmode="email" autocomplete="email" [(ngModel)]="email" />
          </div>
          @if (errorMessage()) { <p class="notice notice-error" role="alert">{{ errorMessage() }}</p> }
          <button type="submit" class="btn btn-primary" [disabled]="submitting()">{{ submitting() ? 'שולחים…' : 'שליחת קישור' }}</button>
        }
      </form>
      <p class="alternate"><a routerLink="/manage/login">חזרה לכניסה</a></p>
    </section>
  `,
  styleUrl: './auth-page.scss',
})
export class ForgotPasswordPage {
  private readonly api = inject(AuthApi);
  email = '';
  readonly submitting = signal(false);
  readonly sent = signal(false);
  readonly errorMessage = signal('');

  submit(): void {
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email.trim())) {
      this.errorMessage.set('כתבו כתובת אימייל תקינה.');
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set('');
    this.api.forgotPassword(this.email.trim()).subscribe({
      next: () => this.sent.set(true),
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        this.errorMessage.set(error.status === 429 ? 'נשלחו כבר כמה בקשות. נסו שוב בעוד רבע שעה.' : 'השליחה נכשלה. נסו שוב בעוד רגע.');
      },
    });
  }
}

@Component({
  selector: 'app-reset-password-page',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="auth">
      <div class="auth-intro"><h1>סיסמה חדשה</h1></div>
      <form class="auth-form" (ngSubmit)="submit()">
        @if (done()) {
          <p class="notice notice-success" role="status">הסיסמה עודכנה. אפשר להתחבר איתה עכשיו.</p>
          <a class="btn btn-primary" routerLink="/manage/login">לכניסה</a>
        } @else {
          <div class="field">
            <label for="reset-password">סיסמה חדשה</label>
            <input class="input" id="reset-password" type="password" name="password" autocomplete="new-password" maxlength="128" [(ngModel)]="password" aria-describedby="reset-help" />
            <span class="field-hint" id="reset-help">לפחות 10 תווים, עם אות גדולה, אות קטנה ומספר.</span>
          </div>
          <div class="field">
            <label for="reset-confirm">אימות הסיסמה</label>
            <input class="input" id="reset-confirm" type="password" name="confirm" autocomplete="new-password" maxlength="128" [(ngModel)]="confirm" />
          </div>
          @if (errorMessage()) { <p class="notice notice-error" role="alert">{{ errorMessage() }}</p> }
          <button type="submit" class="btn btn-primary" [disabled]="submitting()">{{ submitting() ? 'שומרים…' : 'שמירת הסיסמה' }}</button>
        }
      </form>
    </section>
  `,
  styleUrl: './auth-page.scss',
})
export class ResetPasswordPage {
  private readonly api = inject(AuthApi);
  private readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';
  password = '';
  confirm = '';
  readonly submitting = signal(false);
  readonly done = signal(false);
  readonly errorMessage = signal(this.token ? '' : 'הקישור אינו שלם. בקשו קישור חדש.');

  submit(): void {
    if (!isStrongPassword(this.password)) {
      this.errorMessage.set(PASSWORD_RULE);
      return;
    }
    if (this.password !== this.confirm) {
      this.errorMessage.set('שתי הסיסמאות אינן זהות.');
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set('');
    this.api.resetPassword(this.token, this.password).subscribe({
      next: () => this.done.set(true),
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        this.errorMessage.set(
          error.error?.code === 'invalid_token'
            ? 'הקישור פג תוקף או שכבר נעשה בו שימוש. בקשו קישור חדש.'
            : problemFieldErrors(error)['password'] ?? 'השמירה נכשלה. נסו שוב.',
        );
      },
    });
  }
}

/** New Google sign-ups add the business details Google does not provide. */
@Component({
  selector: 'app-complete-profile-page',
  imports: [FormsModule],
  template: `
    <section class="auth">
      <div class="auth-intro">
        <h1>עוד רגע מסיימים</h1>
        <p>{{ email }} אומת על ידי Google. חסרים רק פרטי העסק.</p>
      </div>
      <form class="auth-form" (ngSubmit)="submit()">
        <div class="field">
          <label for="profile-name">שם מלא</label>
          <input class="input" id="profile-name" name="fullName" autocomplete="name" maxlength="150" [(ngModel)]="fullName" [attr.aria-invalid]="errors()['fullName'] ? true : null" />
          @if (errors()['fullName']) { <span class="field-error">{{ errors()['fullName'] }}</span> }
        </div>
        <div class="field">
          <label for="profile-business">שם העסק</label>
          <input class="input" id="profile-business" name="businessName" autocomplete="organization" maxlength="200" [(ngModel)]="businessName" [attr.aria-invalid]="errors()['businessName'] ? true : null" />
          @if (errors()['businessName']) { <span class="field-error">{{ errors()['businessName'] }}</span> }
        </div>
        <div class="field">
          <label for="profile-phone">טלפון</label>
          <input class="input" id="profile-phone" name="phone" type="tel" autocomplete="tel" inputmode="tel" maxlength="30" [(ngModel)]="phone" [attr.aria-invalid]="errors()['phone'] ? true : null" />
          @if (errors()['phone']) { <span class="field-error">{{ errors()['phone'] }}</span> }
        </div>
        @if (errorMessage()) { <p class="notice notice-error" role="alert">{{ errorMessage() }}</p> }
        <button type="submit" class="btn btn-primary" [disabled]="submitting()">{{ submitting() ? 'יוצרים חשבון…' : 'יצירת החשבון' }}</button>
      </form>
    </section>
  `,
  styleUrl: './auth-page.scss',
})
export class CompleteProfilePage implements OnInit {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private signupToken = '';
  email = '';
  fullName = '';
  businessName = '';
  phone = '';
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly errors = signal<Record<string, string>>({});

  ngOnInit(): void {
    const state = (history.state ?? {}) as { signupToken?: string; email?: string; fullName?: string };
    if (!state.signupToken) {
      void this.router.navigate(['/manage/register']);
      return;
    }
    this.signupToken = state.signupToken;
    this.email = state.email ?? '';
    this.fullName = state.fullName ?? '';
  }

  submit(): void {
    const errors: Record<string, string> = {};
    if (this.fullName.trim().length < 2) errors['fullName'] = 'כתבו שם מלא, 2–150 תווים.';
    if (this.businessName.trim().length < 2) errors['businessName'] = 'כתבו את שם העסק, 2–200 תווים.';
    if (this.phone.trim().length < 7) errors['phone'] = 'כתבו מספר טלפון, 7–30 תווים.';
    this.errors.set(errors);
    if (Object.keys(errors).length) return;

    this.submitting.set(true);
    this.errorMessage.set('');
    this.auth.completeGoogle({ signupToken: this.signupToken, fullName: this.fullName, businessName: this.businessName, phone: this.phone }).subscribe({
      next: (response) => void this.router.navigateByUrl(destinationAfterSignIn(response.user, null)),
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        if (error.status === 400 && error.error?.code === 'invalid_registration') this.errors.set(problemFieldErrors(error));
        else this.errorMessage.set(
          error.error?.code === 'signup_expired' ? 'עבר זמן רב מדי. התחילו שוב עם Google.'
            : error.status === 409 ? 'כבר קיים חשבון עם האימייל הזה. היכנסו אליו.'
              : 'יצירת החשבון נכשלה. נסו שוב.',
        );
      },
    });
  }
}

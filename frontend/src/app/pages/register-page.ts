import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../auth/auth-store';
import { problemCode, problemFieldErrors } from '../shared/problem-details';

const PASSWORD_RULE = 'הסיסמה צריכה להכיל 10–128 תווים, אות גדולה, אות קטנה ומספר.';

@Component({
  selector: 'app-register-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './register-page.html',
  styleUrl: './auth-page.scss',
})
export class RegisterPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  fullName = '';
  businessName = '';
  phone = '';
  email = '';
  password = '';
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly fieldErrors = signal<Record<string, string>>({});

  submit(): void {
    const errors = this.validate();
    this.fieldErrors.set(errors);
    this.errorMessage.set('');
    if (Object.keys(errors).length > 0) return;

    this.submitting.set(true);
    this.auth.register({
      fullName: this.fullName,
      businessName: this.businessName,
      phone: this.phone,
      email: this.email,
      password: this.password,
    }).subscribe({
      next: () => void this.router.navigateByUrl('/manage/dashboard'),
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        if (problemCode(error) === 'email_exists') {
          this.fieldErrors.set({ email: 'כבר קיים חשבון עם האימייל הזה. אפשר להתחבר במקום.' });
        } else if (error.status === 400) {
          this.fieldErrors.set(problemFieldErrors(error));
        } else if (error.status === 429) {
          this.errorMessage.set('היו יותר מדי ניסיונות. נסו שוב בעוד דקה.');
        } else {
          this.errorMessage.set('לא הצלחנו לפתוח את החשבון כרגע. נסו שוב בעוד רגע.');
        }
      },
    });
  }

  errorId(field: string): string | null {
    return this.fieldErrors()[field] ? `register-${field}-error` : null;
  }

  private validate(): Record<string, string> {
    const errors: Record<string, string> = {};
    const length = (value: string) => value.trim().length;
    if (length(this.fullName) < 2 || length(this.fullName) > 150) errors['fullName'] = 'כתבו שם מלא, 2–150 תווים.';
    if (length(this.businessName) < 2 || length(this.businessName) > 200) errors['businessName'] = 'כתבו את שם העסק, 2–200 תווים.';
    if (length(this.phone) < 7 || length(this.phone) > 30) errors['phone'] = 'כתבו מספר טלפון, 7–30 תווים.';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email.trim())) errors['email'] = 'כתבו כתובת אימייל תקינה.';
    const password = this.password;
    const strong = password.length >= 10 && password.length <= 128 && /[A-Z]/.test(password) && /[a-z]/.test(password) && /\d/.test(password);
    if (!strong) errors['password'] = PASSWORD_RULE;
    return errors;
  }
}

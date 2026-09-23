import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthStore } from '../auth/auth-store';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './login-page.html',
  styleUrl: './auth-page.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  email = '';
  password = '';
  readonly submitting = signal(false);
  readonly errorMessage = signal('');

  submit(): void {
    if (!this.email.trim() || !this.password) {
      this.errorMessage.set('יש למלא אימייל וסיסמה.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    this.auth.login({ email: this.email, password: this.password }).subscribe({
      next: () => {
        const requested = this.route.snapshot.queryParamMap.get('returnUrl');
        const destination = requested?.startsWith('/manage/')
          ? requested
          : '/manage/dashboard';
        void this.router.navigateByUrl(destination);
      },
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        this.errorMessage.set(
          error.status === 429
            ? 'יותר מדי ניסיונות. נסו שוב בעוד דקה.'
            : 'האימייל או הסיסמה אינם נכונים.',
        );
      },
    });
  }
}

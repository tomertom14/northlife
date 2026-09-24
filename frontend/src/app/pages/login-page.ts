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
  readonly expired = this.route.snapshot.queryParamMap.get('expired') === '1';

  submit(): void {
    if (!this.email.trim() || !this.password) {
      this.errorMessage.set('כתבו אימייל וסיסמה.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    this.auth.login({ email: this.email, password: this.password }).subscribe({
      next: () => {
        const requested = this.route.snapshot.queryParamMap.get('returnUrl');
        const destination = requested?.startsWith('/manage/')
          ? requested
          : this.auth.user()?.role === 'Admin'
            ? '/manage/admin'
            : '/manage/dashboard';
        void this.router.navigateByUrl(destination);
      },
      error: (error: HttpErrorResponse) => {
        this.submitting.set(false);
        this.errorMessage.set(
          error.status === 429
            ? 'היו יותר מדי ניסיונות. נסו שוב בעוד דקה.'
            : error.status === 401
              ? 'האימייל או הסיסמה לא נכונים.'
              : 'לא הצלחנו להתחבר כרגע. בדקו את החיבור ונסו שוב.',
        );
      },
    });
  }
}

import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../auth/auth-store';

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

  submit(): void {
    if (!this.fullName.trim() || !this.businessName.trim() || !this.phone.trim() ||
        !this.email.trim() || !this.password) {
      this.errorMessage.set('יש למלא את כל השדות.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
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
        const code = error.error?.code;
        if (code === 'email_exists') {
          this.errorMessage.set('כבר קיים חשבון עם כתובת האימייל הזו.');
        } else if (error.status === 429) {
          this.errorMessage.set('יותר מדי ניסיונות. נסו שוב בעוד דקה.');
        } else {
          this.errorMessage.set('לא הצלחנו להשלים את ההרשמה. בדקו את הפרטים ונסו שוב.');
        }
      },
    });
  }
}

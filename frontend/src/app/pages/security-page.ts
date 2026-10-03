import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthApi } from '../auth/auth-api';
import { AuthStore } from '../auth/auth-store';
import { SecurityStatus, TotpSetup } from '../auth/auth.models';
import { ToastService } from '../shared/toast';

@Component({
  selector: 'app-security-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './security-page.html',
  styleUrl: './security-page.scss',
})
export class SecurityPage implements OnInit {
  private readonly api = inject(AuthApi);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthStore);
  readonly required = inject(ActivatedRoute).snapshot.queryParamMap.get('required') === '1';

  readonly status = signal<SecurityStatus | null>(null);
  readonly setup = signal<TotpSetup | null>(null);
  readonly recoveryCodes = signal<string[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly disabling = signal(false);
  readonly isAdmin = computed(() => this.auth.user()?.role === 'Admin');
  readonly homeLink = computed(() => (this.isAdmin() ? '/manage/admin' : '/manage/dashboard'));
  code = '';

  ngOnInit(): void {
    this.loadStatus();
  }

  beginSetup(): void {
    this.busy.set(true);
    this.error.set('');
    this.api.beginTotpSetup().subscribe({
      next: (setup) => {
        this.busy.set(false);
        this.setup.set(setup);
        this.code = '';
      },
      error: () => this.fail('לא הצלחנו להתחיל את ההגדרה. נסו שוב.'),
    });
  }

  confirmSetup(): void {
    if (!/^\d{6}$/.test(this.code.trim())) {
      this.error.set('כתבו את הקוד בן 6 הספרות שמופיע באפליקציה.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.api.enableTotp(this.code.trim()).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.auth.accept(result.auth);
        this.setup.set(null);
        this.recoveryCodes.set(result.recoveryCodes);
        this.loadStatus();
      },
      error: (error: HttpErrorResponse) =>
        this.fail(error.status === 400 ? 'הקוד לא נכון. ודאו שהשעה במכשיר מעודכנת ונסו את הקוד הבא.' : 'ההפעלה נכשלה. נסו שוב.'),
    });
  }

  disable(): void {
    if (!this.code.trim()) {
      this.error.set('כתבו קוד מהאפליקציה או קוד גיבוי.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.api.disableTotp(this.code.trim()).subscribe({
      next: (response) => {
        this.busy.set(false);
        this.auth.accept(response);
        this.disabling.set(false);
        this.code = '';
        this.toast.success('האימות הדו-שלבי כובה.');
        this.loadStatus();
      },
      error: (error: HttpErrorResponse) =>
        this.fail(
          error.error?.code === 'mfa_locked'
            ? 'נרשמו יותר מדי קודים שגויים. נסו שוב בעוד 15 דקות.'
            : error.status === 400
              ? 'הקוד לא נכון או שכבר נעשה בו שימוש.'
              : 'הכיבוי נכשל. נסו שוב.',
        ),
    });
  }

  async copyCodes(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.recoveryCodes().join('\n'));
      this.toast.success('קודי הגיבוי הועתקו.');
    } catch {
      this.toast.error('ההעתקה לא הצליחה. רשמו את הקודים ידנית.');
    }
  }

  finish(): void {
    this.recoveryCodes.set([]);
    void this.router.navigateByUrl(this.homeLink());
  }

  private loadStatus(): void {
    this.api.securityStatus().subscribe({
      next: (status) => this.status.set(status),
      error: () => this.error.set('מצב האבטחה לא נטען. רעננו את העמוד.'),
    });
  }

  private fail(message: string): void {
    this.busy.set(false);
    this.error.set(message);
  }
}

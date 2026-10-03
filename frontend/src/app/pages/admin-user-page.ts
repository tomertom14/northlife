import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import {
  AUDIT_LABELS,
  AdminUserDetails,
  AdminUsersApi,
  ROLE_LABELS,
  auditActor,
  describeAudit,
} from '../admin/admin-users-api';
import { AuthStore } from '../auth/auth-store';
import { UserRole } from '../auth/auth.models';
import { AdminNav } from '../manage/admin-nav';
import { StatusBadge } from '../manage/status-badge';
import { formatLongDate, formatShortDate, formatTime } from '../shared/jerusalem-time';
import { problemCode } from '../shared/problem-details';
import { ToastService } from '../shared/toast';

const PROBLEM_MESSAGES: Record<string, string> = {
  cannot_suspend_self: 'אי אפשר להשעות את החשבון שלכם.',
  cannot_change_own_role: 'אי אפשר לשנות את התפקיד של עצמכם.',
  last_admin: 'חייב להישאר לפחות מנהל פעיל אחד.',
  concurrent_change: 'שינוי אחר בוצע באותו רגע. הפרטים רועננו, נסו שוב.',
  reason_required: 'כתבו סיבה להשעיה, עד 500 תווים.',
};

@Component({
  selector: 'app-admin-user-page',
  imports: [FormsModule, RouterLink, AdminNav, StatusBadge],
  templateUrl: './admin-user-page.html',
  styleUrl: './admin-people.scss',
})
export class AdminUserPage implements OnInit {
  private readonly api = inject(AdminUsersApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthStore);
  private readonly userId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  readonly details = signal<AdminUserDetails | null>(null);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly busy = signal(false);
  readonly suspending = signal(false);
  readonly reasonError = signal('');
  readonly isSelf = computed(() => this.details()?.user.id === this.auth.user()?.id);
  readonly roleLabels = ROLE_LABELS;
  readonly auditLabels = AUDIT_LABELS;
  readonly describe = describeAudit;
  readonly actor = auditActor;
  readonly date = formatShortDate;
  reason = '';
  role: UserRole = 'BusinessOwner';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.get(this.userId).subscribe({
      next: (details) => {
        this.details.set(details);
        this.role = details.user.role;
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  startSuspend(): void {
    this.reason = '';
    this.reasonError.set('');
    this.suspending.set(true);
  }

  suspend(): void {
    const reason = this.reason.trim();
    if (!reason) {
      this.reasonError.set('כתבו למה החשבון מושעה. הסיבה נשמרת ביומן הפעולות.');
      return;
    }
    this.run(this.api.suspend(this.userId, reason), 'החשבון הושעה. החיבורים הפעילים שלו נותקו.');
  }

  unsuspend(): void {
    this.run(this.api.unsuspend(this.userId), 'ההשעיה בוטלה. המשתמש יכול להתחבר שוב.');
  }

  saveRole(): void {
    this.run(this.api.changeRole(this.userId, this.role), `התפקיד עודכן ל${ROLE_LABELS[this.role]}.`);
  }

  when(value: string): string {
    return `${formatLongDate(value)}, ${formatTime(value)}`;
  }

  private run(request: Observable<void>, message: string): void {
    this.busy.set(true);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.suspending.set(false);
        this.toast.success(message);
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.busy.set(false);
        if (error.status === 401) return;
        const code = problemCode(error);
        this.toast.error((code && PROBLEM_MESSAGES[code]) || 'הפעולה נכשלה. נסו שוב בעוד רגע.');
        if (code === 'concurrent_change') this.load();
      },
    });
  }
}

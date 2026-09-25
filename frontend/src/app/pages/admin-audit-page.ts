import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AUDIT_LABELS, AdminUsersApi, AuditEntry, describeAudit } from '../admin/admin-users-api';
import { AdminNav } from '../manage/admin-nav';
import { formatLongDate, formatTime } from '../shared/jerusalem-time';

@Component({
  selector: 'app-admin-audit-page',
  imports: [RouterLink, AdminNav],
  template: `
    <div class="people">
      <header class="page-head">
        <h1>יומן פעולות</h1>
      </header>
      <app-admin-nav />
      <p class="muted">כל פעולת ניהול נרשמת כאן. אי אפשר לערוך או למחוק רשומות.</p>

      @if (loading()) {
        <p class="muted" role="status">טוענים את היומן…</p>
      } @else if (failed()) {
        <p class="notice notice-error" role="alert">היומן לא נטען. נסו שוב.</p>
      } @else if (entries().length === 0) {
        <p class="muted">עוד לא בוצעו פעולות ניהול.</p>
      } @else {
        <ul class="timeline card">
          @for (entry of entries(); track entry.id) {
            <li>
              <span class="when">{{ when(entry.createdAt) }}, {{ entry.actorName }}</span>
              <span class="what">
                {{ labels[entry.action] ?? entry.action }}
                @if (entry.targetType === 'User') {
                  <a class="target" [routerLink]="['/manage/admin/users', entry.targetId]">לחשבון</a>
                }
              </span>
              @if (describe(entry); as line) {
                <span class="detail">{{ line }}</span>
              }
            </li>
          }
        </ul>
        @if (nextCursor()) {
          <button type="button" class="btn more" [disabled]="loadingMore()" (click)="loadMore()">
            {{ loadingMore() ? 'טוענים…' : 'פעולות קודמות' }}
          </button>
        }
      }
    </div>
  `,
  styleUrl: './admin-people.scss',
})
export class AdminAuditPage implements OnInit {
  private readonly api = inject(AdminUsersApi);
  readonly entries = signal<AuditEntry[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly loading = signal(true);
  readonly loadingMore = signal(false);
  readonly failed = signal(false);
  readonly labels = AUDIT_LABELS;
  readonly describe = describeAudit;

  ngOnInit(): void {
    this.api.audit(null).subscribe({
      next: (page) => {
        this.entries.set(page.items);
        this.nextCursor.set(page.nextCursor);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor) return;
    this.loadingMore.set(true);
    this.api.audit(cursor).subscribe({
      next: (page) => {
        this.entries.update((current) => [...current, ...page.items]);
        this.nextCursor.set(page.nextCursor);
        this.loadingMore.set(false);
      },
      error: () => this.loadingMore.set(false),
    });
  }

  when(value: string): string {
    return `${formatLongDate(value)}, ${formatTime(value)}`;
  }
}

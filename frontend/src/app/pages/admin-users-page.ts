import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminUserSummary, AdminUsersApi, ROLE_LABELS, UserListQuery } from '../admin/admin-users-api';
import { AdminNav } from '../manage/admin-nav';
import { formatShortDate } from '../shared/jerusalem-time';

@Component({
  selector: 'app-admin-users-page',
  imports: [FormsModule, RouterLink, AdminNav],
  templateUrl: './admin-users-page.html',
  styleUrl: './admin-people.scss',
})
export class AdminUsersPage implements OnInit {
  private readonly api = inject(AdminUsersApi);
  readonly users = signal<AdminUserSummary[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly loading = signal(true);
  readonly loadingMore = signal(false);
  readonly failed = signal(false);
  readonly roleLabels = ROLE_LABELS;
  readonly date = formatShortDate;
  query: UserListQuery = { search: '', role: '', status: '' };

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.list({ ...this.query, cursor: null }).subscribe({
      next: (page) => {
        this.users.set(page.items);
        this.nextCursor.set(page.nextCursor);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  /** Keyset pagination: the server continues exactly after the last row shown. */
  loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor) return;
    this.loadingMore.set(true);
    this.api.list({ ...this.query, cursor }).subscribe({
      next: (page) => {
        this.users.update((current) => [...current, ...page.items]);
        this.nextCursor.set(page.nextCursor);
        this.loadingMore.set(false);
      },
      error: () => this.loadingMore.set(false),
    });
  }
}

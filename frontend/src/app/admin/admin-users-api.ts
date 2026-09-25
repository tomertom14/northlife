import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { UserRole } from '../auth/auth.models';
import { OwnerEventStatus } from '../owner/owner-events-api';

export interface AdminUserSummary {
  id: string;
  fullName: string;
  email: string;
  businessName: string;
  role: UserRole;
  emailConfirmed: boolean;
  totpEnabled: boolean;
  suspended: boolean;
  createdAt: string;
  publishedEvents: number;
  pendingEvents: number;
}

export interface AdminUserPage {
  items: AdminUserSummary[];
  nextCursor: string | null;
}

export interface AuditEntry {
  id: string;
  actorId: string;
  actorName: string;
  action: string;
  targetType: string;
  targetId: string;
  details: Record<string, unknown>;
  createdAt: string;
}

export interface AdminUserDetails {
  user: AdminUserSummary;
  phone: string;
  suspendedAt: string | null;
  suspensionReason: string | null;
  googleLinked: boolean;
  events: { id: string; title: string; status: OwnerEventStatus; startAt: string }[];
  audit: AuditEntry[];
}

export interface UserListQuery {
  search?: string;
  role?: UserRole | '';
  status?: '' | 'active' | 'suspended' | 'unverified';
  cursor?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminUsersApi {
  private readonly http = inject(HttpClient);

  list(query: UserListQuery) {
    let params = new HttpParams().set('pageSize', 20);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.role) params = params.set('role', query.role);
    if (query.status) params = params.set('status', query.status);
    if (query.cursor) params = params.set('cursor', query.cursor);
    return this.http.get<AdminUserPage>('/api/admin/users', { params });
  }

  get(id: string) {
    return this.http.get<AdminUserDetails>(`/api/admin/users/${encodeURIComponent(id)}`);
  }

  suspend(id: string, reason: string) {
    return this.http.post<void>(`/api/admin/users/${encodeURIComponent(id)}/suspend`, { reason });
  }

  unsuspend(id: string) {
    return this.http.post<void>(`/api/admin/users/${encodeURIComponent(id)}/unsuspend`, {});
  }

  changeRole(id: string, role: UserRole) {
    return this.http.post<void>(`/api/admin/users/${encodeURIComponent(id)}/role`, { role });
  }

  audit(cursor: string | null) {
    let params = new HttpParams().set('pageSize', 30);
    if (cursor) params = params.set('cursor', cursor);
    return this.http.get<{ items: AuditEntry[]; nextCursor: string | null }>('/api/admin/audit', { params });
  }
}

/** Plain-language labels for audit actions. */
export const AUDIT_LABELS: Record<string, string> = {
  'user.suspended': 'השעיית משתמש',
  'user.unsuspended': 'ביטול השעיה',
  'user.role_changed': 'שינוי תפקיד',
  'event.created': 'פרסום אירוע',
  'event.updated': 'עריכת אירוע',
  'event.approved': 'אישור אירוע',
  'event.rejected': 'דחיית אירוע',
  'event.highlighted': 'הוספה לבחירות העורכים',
  'event.unhighlighted': 'הסרה מבחירות העורכים',
  'event.deleted': 'מחיקת אירוע',
};

export const ROLE_LABELS: Record<UserRole, string> = { BusinessOwner: 'בעל עסק', Admin: 'מנהל' };

/** One readable line from an audit entry's JSON details. */
export function describeAudit(entry: AuditEntry): string {
  const details = entry.details ?? {};
  const text = (key: string) => (typeof details[key] === 'string' ? (details[key] as string) : '');
  switch (entry.action) {
    case 'user.suspended':
      return `סיבה: ${text('reason')}`;
    case 'user.unsuspended':
      return text('previousReason') ? `ההשעיה הייתה בגלל: ${text('previousReason')}` : '';
    case 'user.role_changed': {
      const from = ROLE_LABELS[text('from') as UserRole] ?? text('from');
      const to = ROLE_LABELS[text('to') as UserRole] ?? text('to');
      return `מ${from} ל${to}`;
    }
    case 'event.rejected':
      return [text('title'), text('rejectionReason') && `סיבה: ${text('rejectionReason')}`].filter(Boolean).join('. ');
    default:
      return text('title');
  }
}

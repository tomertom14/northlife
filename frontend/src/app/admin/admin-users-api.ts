import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { UserRole } from '../auth/auth.models';
import { OwnerEventStatus } from '../owner/owner-events-api';
import { AutoModerationMode, MODE_LABELS, describeRunSummary } from './auto-moderation-api';

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
  /** Null when the automatic event approval service acted. */
  actorId: string | null;
  actorName: string | null;
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
  'automoderation.settings_changed': 'שינוי הגדרות האישור האוטומטי',
  'automoderation.run': 'הרצה ידנית של האישור האוטומטי',
};

export const ROLE_LABELS: Record<UserRole, string> = { BusinessOwner: 'בעל עסק', Admin: 'מנהל' };

/** Who acted: the administrator's name, or the automatic event approval service. */
export function auditActor(entry: AuditEntry): string {
  return entry.actorName ?? 'אישור אוטומטי';
}

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
    case 'automoderation.run': {
      const count = (key: string) => (typeof details[key] === 'number' ? (details[key] as number) : 0);
      const mode: AutoModerationMode = text('mode') === 'Approve' ? 'Approve' : 'NotesOnly';
      return describeRunSummary({ mode, checked: count('checked'), approved: count('approved'), wouldApprove: count('wouldApprove'), held: count('held') });
    }
    case 'automoderation.settings_changed':
      return describeSettingsChange(details['changes']);
    default:
      return text('title');
  }
}

/** "מצב: מהערות בלבד לאישור אוטומטי. עודכנו עוד 2 הגדרות" */
function describeSettingsChange(changes: unknown): string {
  if (!changes || typeof changes !== 'object') return '';
  const fields = changes as Record<string, { from?: unknown; to?: unknown }>;
  const parts: string[] = [];
  const mode = fields['mode'];
  if (mode) {
    const label = (value: unknown) => (MODE_LABELS as Record<string, string>)[String(value)] ?? String(value);
    parts.push(`מצב: מ${label(mode.from)} ל${label(mode.to)}`);
  }
  const others = Object.keys(fields).filter((field) => field !== 'mode').length;
  if (others > 0) parts.push(others === 1 ? `עודכנה ${mode ? 'עוד ' : ''}הגדרה אחת` : `עודכנו ${mode ? 'עוד ' : ''}${others} הגדרות`);
  return parts.join('. ');
}

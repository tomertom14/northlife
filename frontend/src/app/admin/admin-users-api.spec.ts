import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminUsersApi, AuditEntry, auditActor, describeAudit } from './admin-users-api';

describe('AdminUsersApi', () => {
  let api: AdminUsersApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(AdminUsersApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('sends only the filters that are set, plus the keyset cursor', () => {
    api.list({ search: '  galil ', role: '', status: 'suspended', cursor: 'abc' }).subscribe();
    const request = http.expectOne((r) => r.url === '/api/admin/users');
    expect(request.request.params.get('search')).toBe('galil');
    expect(request.request.params.has('role')).toBe(false);
    expect(request.request.params.get('status')).toBe('suspended');
    expect(request.request.params.get('cursor')).toBe('abc');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush({ items: [], nextCursor: null });
  });

  it('posts the suspension reason', () => {
    api.suspend('user-1', 'spam').subscribe();
    const request = http.expectOne('/api/admin/users/user-1/suspend');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'spam' });
    request.flush(null);
  });

  it('posts the new role', () => {
    api.changeRole('user-1', 'Admin').subscribe();
    const request = http.expectOne('/api/admin/users/user-1/role');
    expect(request.request.body).toEqual({ role: 'Admin' });
    request.flush(null);
  });
});

describe('describeAudit', () => {
  const entry = (action: string, details: Record<string, unknown>): AuditEntry => ({
    id: '1',
    actorId: 'a',
    actorName: 'Admin',
    action,
    targetType: 'User',
    targetId: 't',
    details,
    createdAt: '2026-09-25T10:00:00Z',
  });

  it('shows the suspension reason', () => {
    expect(describeAudit(entry('user.suspended', { reason: 'ספאם' }))).toBe('סיבה: ספאם');
  });

  it('translates both roles of a role change', () => {
    expect(describeAudit(entry('user.role_changed', { from: 'BusinessOwner', to: 'Admin' }))).toBe('מבעל עסק למנהל');
  });

  it('falls back to the event title', () => {
    expect(describeAudit(entry('event.approved', { title: 'ערב ג׳אז' }))).toBe('ערב ג׳אז');
  });

  it('ignores details that are not text', () => {
    expect(describeAudit(entry('event.deleted', { title: 42 }))).toBe('');
  });

  it('summarises a manual run of the automatic approval service', () => {
    expect(describeAudit(entry('automoderation.run', { mode: 'Approve', checked: 3, approved: 2, wouldApprove: 0, held: 1 }))).toBe(
      'נבדקו 3 אירועים: 2 אושרו, אחד נשאר לבדיקה.',
    );
  });

  it('names a mode change and counts the other changed settings', () => {
    const changes = { mode: { from: 'NotesOnly', to: 'Approve' }, maxPrice: { from: 1000, to: 500 }, runAt: { from: '07:00', to: '06:00' } };
    expect(describeAudit(entry('automoderation.settings_changed', { changes }))).toBe('מצב: מהערות בלבד לאישור אוטומטי. עודכנו עוד 2 הגדרות');
    expect(describeAudit(entry('automoderation.settings_changed', { changes: { maxPrice: { from: 1000, to: 500 } } }))).toBe('עודכנה הגדרה אחת');
  });
});

describe('auditActor', () => {
  it('names the administrator, or the automatic approval service when there is no actor', () => {
    const base: AuditEntry = {
      id: '1', actorId: 'a', actorName: 'נועה', action: 'event.approved', targetType: 'Event',
      targetId: 't', details: {}, createdAt: '2026-09-29T07:00:00Z',
    };
    expect(auditActor(base)).toBe('נועה');
    expect(auditActor({ ...base, actorId: null, actorName: null })).toBe('אישור אוטומטי');
  });
});

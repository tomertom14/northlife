import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminUsersApi, AuditEntry, describeAudit } from './admin-users-api';

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
});

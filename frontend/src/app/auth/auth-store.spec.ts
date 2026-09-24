import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { OwnerEventsApi } from '../owner/owner-events-api';
import { authInterceptor } from './auth.interceptor';
import { AuthApi } from './auth-api';
import { AuthStore } from './auth-store';

describe('AuthStore and interceptor', () => {
  let store: AuthStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    store = TestBed.inject(AuthStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('keeps a successful session in memory and attaches its bearer token', () => {
    store.login({ email: 'owner@example.com', password: 'StrongPass123' }).subscribe();

    const login = http.expectOne('/api/auth/login');
    expect(login.request.headers.has('Authorization')).toBe(false);
    login.flush({
      token: 'signed-token',
      expiresAt: '2026-09-23T09:00:00Z',
      user: {
        id: '019924c0-0000-7000-8000-000000000100',
        fullName: 'Test Owner',
        email: 'owner@example.com',
        businessName: 'Test Business',
        role: 'BusinessOwner',
      },
    });

    expect(store.isAuthenticated()).toBe(true);
    const api = TestBed.inject(AuthApi);
    api.session().subscribe();
    const session = http.expectOne('/api/auth/session');
    expect(session.request.headers.get('Authorization')).toBe('Bearer signed-token');
    session.flush({});
    expect(store.token()).toBe('signed-token');
  });

  it('sends the bearer token to protected auth calls and clears it on logout', () => {
    store.login({ email: 'owner@example.com', password: 'StrongPass123' }).subscribe();
    http.expectOne('/api/auth/login').flush({
      token: 'signed-token',
      expiresAt: '2026-09-23T09:00:00Z',
      user: {
        id: '019924c0-0000-7000-8000-000000000100',
        fullName: 'Test Owner',
        email: 'owner@example.com',
        businessName: 'Test Business',
        role: 'BusinessOwner',
      },
    });

    store.logout().subscribe();
    const logout = http.expectOne('/api/auth/logout');
    expect(logout.request.headers.get('Authorization')).toBe('Bearer signed-token');
    logout.flush(null);

    expect(store.isAuthenticated()).toBe(false);
    expect(store.token()).toBeNull();
  });

  it('never writes the session to browser storage', () => {
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });

  it('treats a session past its expiry time as invalid', () => {
    store.login({ email: 'owner@example.com', password: 'StrongPass123' }).subscribe();
    http.expectOne('/api/auth/login').flush({
      token: 'signed-token',
      expiresAt: '2026-09-23T09:00:00Z',
      user: {
        id: '019924c0-0000-7000-8000-000000000100',
        fullName: 'Test Owner',
        email: 'owner@example.com',
        businessName: 'Test Business',
        role: 'BusinessOwner',
      },
    });

    expect(store.hasValidSession(Date.parse('2026-09-23T08:59:00Z'))).toBe(true);
    expect(store.hasValidSession(Date.parse('2026-09-23T09:00:00Z'))).toBe(false);
  });

  it('drops the session and asks for a new login when a protected call returns 401', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    store.login({ email: 'owner@example.com', password: 'StrongPass123' }).subscribe();
    http.expectOne('/api/auth/login').flush({
      token: 'expired-token',
      expiresAt: '2026-09-23T09:00:00Z',
      user: {
        id: '019924c0-0000-7000-8000-000000000100',
        fullName: 'Test Owner',
        email: 'owner@example.com',
        businessName: 'Test Business',
        role: 'BusinessOwner',
      },
    });

    TestBed.inject(OwnerEventsApi).list().subscribe({ error: () => undefined });
    http.expectOne('/api/manage/events').flush({ code: 'invalid_token' }, { status: 401, statusText: 'Unauthorized' });

    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/manage/login'], expect.objectContaining({
      queryParams: expect.objectContaining({ expired: 1 }),
    }));
  });
});

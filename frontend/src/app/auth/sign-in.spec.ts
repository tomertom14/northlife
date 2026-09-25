import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthStore } from './auth-store';
import { AuthUser } from './auth.models';
import { destinationAfterSignIn } from './sign-in-navigation';

const owner: AuthUser = { id: '1', fullName: 'Owner', email: 'o@example.com', businessName: 'Biz', role: 'BusinessOwner' };
const admin: AuthUser = { ...owner, role: 'Admin' };

describe('destinationAfterSignIn', () => {
  it('sends an administrator without a verified second factor to set it up', () => {
    expect(destinationAfterSignIn({ ...admin, mfaVerified: false }, '/manage/admin')).toBe('/manage/security?required=1');
  });

  it('honours a management return URL and ignores anything else', () => {
    expect(destinationAfterSignIn(owner, '/manage/security')).toBe('/manage/security');
    expect(destinationAfterSignIn(owner, 'https://evil.example')).toBe('/manage/dashboard');
    expect(destinationAfterSignIn({ ...admin, mfaVerified: true }, null)).toBe('/manage/admin');
  });
});

describe('AuthStore two-step sign-in', () => {
  let store: AuthStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    store = TestBed.inject(AuthStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('keeps no session after the password step when a second factor is required', () => {
    let status = '';
    store.login({ email: 'o@example.com', password: 'StrongPass123' }).subscribe((result) => (status = result.status ?? ''));
    http.expectOne('/api/auth/login').flush({ status: 'mfa_required', mfaToken: 'ticket' });

    expect(status).toBe('mfa_required');
    expect(store.isAuthenticated()).toBe(false);
  });

  it('signs in once the code is accepted, marking the session as second-factor verified', () => {
    store.completeMfa('ticket', '123456').subscribe();
    const request = http.expectOne('/api/auth/mfa');
    expect(request.request.body).toEqual({ mfaToken: 'ticket', code: '123456' });
    request.flush({
      status: 'authenticated',
      token: 'signed',
      expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      user: { ...admin, mfaVerified: true },
    });

    expect(store.hasValidSession()).toBe(true);
    expect(store.user()?.mfaVerified).toBe(true);
  });

  it('updates account flags from the session endpoint without replacing the token', () => {
    store.accept({ token: 'signed', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), user: { ...owner, emailConfirmed: false } });
    store.refreshUser().subscribe();
    const request = http.expectOne('/api/auth/session');
    expect(request.request.headers.get('Authorization')).toBe('Bearer signed');
    request.flush({ ...owner, emailConfirmed: true });

    expect(store.user()?.emailConfirmed).toBe(true);
    expect(store.token()).toBe('signed');
  });
});

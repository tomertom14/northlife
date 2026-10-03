import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, finalize, map, tap, throwError } from 'rxjs';
import { AuthApi } from './auth-api';
import { AuthResponse, LoginRequest, RegisterRequest, SignInResult, isAuthenticated } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly api = inject(AuthApi);
  private readonly session = signal<AuthResponse | null>(null);

  readonly token = computed(() => this.session()?.token ?? null);
  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  /** A session exists and its token has not reached its expiry time. */
  hasValidSession(now = Date.now()): boolean {
    const session = this.session();
    return session !== null && Date.parse(session.expiresAt) > now;
  }

  /** Signs in immediately, or returns the second-factor challenge for accounts with TOTP. */
  login(request: LoginRequest) {
    return this.api.login(request).pipe(tap((result) => this.acceptIfSignedIn(result)));
  }

  completeMfa(mfaToken: string, code: string) {
    return this.api.completeMfa(mfaToken, code).pipe(tap((response) => this.accept(response)));
  }

  google(idToken: string) {
    return this.api.google(idToken).pipe(tap((result) => this.acceptIfSignedIn(result)));
  }

  completeGoogle(request: { signupToken: string; fullName: string; businessName: string; phone: string }) {
    return this.api.completeGoogle(request).pipe(tap((response) => this.accept(response)));
  }

  register(request: RegisterRequest) {
    return this.api.register(request).pipe(tap((response) => this.accept(response)));
  }

  /** Re-reads account flags (verified email, 2FA) without replacing the token. */
  refreshUser() {
    return this.api.session().pipe(
      tap((user) => {
        const current = this.session();
        if (current) this.session.set({ ...current, user });
      }),
      map(() => undefined),
    );
  }

  accept(response: AuthResponse): void {
    this.session.set(response);
  }

  logout() {
    return this.api.logout().pipe(
      catchError((error) => {
        this.clear();
        return throwError(() => error);
      }),
      finalize(() => this.clear()),
    );
  }

  clear(): void {
    this.session.set(null);
  }

  private acceptIfSignedIn(result: SignInResult): void {
    if (isAuthenticated(result)) this.accept(result);
  }
}

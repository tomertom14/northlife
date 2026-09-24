import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, finalize, tap, throwError } from 'rxjs';
import { AuthApi } from './auth-api';
import { AuthResponse, LoginRequest, RegisterRequest } from './auth.models';

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

  login(request: LoginRequest) {
    return this.api.login(request).pipe(tap((response) => this.session.set(response)));
  }

  register(request: RegisterRequest) {
    return this.api.register(request).pipe(tap((response) => this.session.set(response)));
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
}

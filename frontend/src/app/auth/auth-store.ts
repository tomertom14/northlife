import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, finalize, tap, throwError } from 'rxjs';
import { AuthApi } from './auth-api';
import { AuthResponse, AuthUser, LoginRequest, RegisterRequest } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly api = inject(AuthApi);
  private readonly tokenState = signal<string | null>(null);
  private readonly userState = signal<AuthUser | null>(null);

  readonly token = this.tokenState.asReadonly();
  readonly user = this.userState.asReadonly();
  readonly isAuthenticated = computed(() => this.tokenState() !== null && this.userState() !== null);

  login(request: LoginRequest) {
    return this.api.login(request).pipe(tap((response) => this.accept(response)));
  }

  register(request: RegisterRequest) {
    return this.api.register(request).pipe(tap((response) => this.accept(response)));
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
    this.tokenState.set(null);
    this.userState.set(null);
  }

  private accept(response: AuthResponse): void {
    this.tokenState.set(response.token);
    this.userState.set(response.user);
  }
}

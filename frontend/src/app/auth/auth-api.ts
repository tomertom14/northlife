import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import {
  AuthResponse,
  AuthUser,
  LoginRequest,
  RegisterRequest,
  SecurityStatus,
  SignInResult,
  TotpEnabled,
  TotpSetup,
} from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly baseUrl = '/api/auth';

  constructor(private readonly http: HttpClient) {}

  register(request: RegisterRequest) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/register`, request);
  }

  login(request: LoginRequest) {
    return this.http.post<SignInResult>(`${this.baseUrl}/login`, request);
  }

  completeMfa(mfaToken: string, code: string) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/mfa`, { mfaToken, code });
  }

  google(idToken: string) {
    return this.http.post<SignInResult>(`${this.baseUrl}/google`, { idToken });
  }

  completeGoogle(request: { signupToken: string; fullName: string; businessName: string; phone: string }) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/google/complete`, request);
  }

  verifyEmail(token: string) {
    return this.http.post<void>(`${this.baseUrl}/verify-email`, { token });
  }

  resendVerification() {
    return this.http.post<void>(`${this.baseUrl}/resend-verification`, {});
  }

  forgotPassword(email: string) {
    return this.http.post<void>(`${this.baseUrl}/forgot-password`, { email });
  }

  resetPassword(token: string, password: string) {
    return this.http.post<void>(`${this.baseUrl}/reset-password`, { token, password });
  }

  securityStatus() {
    return this.http.get<SecurityStatus>(`${this.baseUrl}/security`);
  }

  beginTotpSetup() {
    return this.http.post<TotpSetup>(`${this.baseUrl}/security/totp/setup`, {});
  }

  enableTotp(code: string) {
    return this.http.post<TotpEnabled>(`${this.baseUrl}/security/totp/enable`, { code });
  }

  disableTotp(code: string) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/security/totp/disable`, { code });
  }

  logout() {
    return this.http.post<void>(`${this.baseUrl}/logout`, {});
  }

  session() {
    return this.http.get<AuthUser>(`${this.baseUrl}/session`);
  }
}

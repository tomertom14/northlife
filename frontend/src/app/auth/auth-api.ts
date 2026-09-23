import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { AuthResponse, AuthUser, LoginRequest, RegisterRequest } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly baseUrl = '/api/auth';

  constructor(private readonly http: HttpClient) {}

  register(request: RegisterRequest) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/register`, request);
  }

  login(request: LoginRequest) {
    return this.http.post<AuthResponse>(`${this.baseUrl}/login`, request);
  }

  logout() {
    return this.http.post<void>(`${this.baseUrl}/logout`, {});
  }

  session() {
    return this.http.get<AuthUser>(`${this.baseUrl}/session`);
  }
}

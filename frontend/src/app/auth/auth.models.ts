export type UserRole = 'BusinessOwner' | 'Admin';

export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
  businessName: string;
  role: UserRole;
}

export interface AuthResponse {
  token: string;
  expiresAt: string;
  user: AuthUser;
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
  phone: string;
  businessName: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

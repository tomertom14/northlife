export type UserRole = 'BusinessOwner' | 'Admin';

export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
  businessName: string;
  role: UserRole;
  emailConfirmed?: boolean;
  totpEnabled?: boolean;
  /** This session passed the TOTP (or backup code) step. */
  mfaVerified?: boolean;
}

export interface AuthResponse {
  status?: 'authenticated';
  token: string;
  expiresAt: string;
  user: AuthUser;
}

export interface MfaChallenge {
  status: 'mfa_required';
  mfaToken: string;
}

export interface GoogleProfileRequired {
  status: 'profile_required';
  signupToken: string;
  email: string;
  fullName: string;
}

export type SignInResult = AuthResponse | MfaChallenge | GoogleProfileRequired;

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

export interface SecurityStatus {
  emailConfirmed: boolean;
  totpEnabled: boolean;
  recoveryCodesLeft: number;
  hasPassword: boolean;
  googleLinked: boolean;
}

export interface TotpSetup {
  secret: string;
  provisioningUri: string;
  qrCodeDataUri: string;
}

export interface TotpEnabled {
  recoveryCodes: string[];
  auth: AuthResponse;
}

export function isAuthenticated(result: SignInResult): result is AuthResponse {
  return 'token' in result;
}

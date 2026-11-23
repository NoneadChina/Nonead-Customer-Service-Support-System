import type { UserResponse } from './core';

export interface LoginRequest {
  email: string;
  password: string;
  captchaId: string;
  captchaCode: string;
}

export interface CaptchaResponse {
  captchaId: string;
  svgContent: string;
}

export interface PasswordPolicyResponse {
  minLength: number;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireDigit: boolean;
  requireNonAlphanumeric: boolean;
  requirements: string[];
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export interface LoginResponse {
  success: boolean;
  token: string;
  refreshToken?: string;
  user: UserResponse | null;
  errorMessage?: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
  confirmPassword: string;
}

export interface ForgotPasswordRequest {
  firstName: string;
  lastName: string;
  email: string;
}

export interface AuthOperationResult {
  success: boolean;
  message?: string;
}

export interface PublicConfigResponse {
  allowRegistration: boolean;
}

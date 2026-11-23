import client from '../client';
import type {
  LoginRequest,
  LoginResponse,
  CaptchaResponse,
  PasswordPolicyResponse,
  RegisterRequest,
  ResetPasswordRequest,
  ForgotPasswordRequest,
  AuthOperationResult,
  PublicConfigResponse,
} from '../types';

// ====== 认证 API ======
export const authApi = {
  login: (data: LoginRequest) =>
    client.post<LoginResponse>('/api/auth/login', data).then((r) => r.data),

  logout: () => client.post<boolean>('/api/auth/logout').then((r) => r.data),

  refreshToken: () => client.post<{ token: string }>('/api/auth/refresh-token').then((r) => r.data),

  getCaptcha: () =>
    client.get<CaptchaResponse>('/api/auth/captcha').then((r) => r.data),

  // 获取当前密码策略（公开接口，用于注册/重置密码等场景）
  getPasswordPolicy: () =>
    client.get<PasswordPolicyResponse>('/api/auth/password-policy').then((r) => r.data),

  // 获取公开配置（是否开放注册等，公开接口）
  getPublicConfig: () =>
    client.get<PublicConfigResponse>('/api/auth/public-config').then((r) => r.data),

  register: (data: RegisterRequest) =>
    client.post<LoginResponse>('/api/auth/register', data).then((r) => r.data),

  resetPassword: (data: ResetPasswordRequest) =>
    client.post<AuthOperationResult>('/api/auth/reset-password', data).then((r) => r.data),

  forgotPassword: (data: ForgotPasswordRequest) =>
    client.post<AuthOperationResult>('/api/auth/forgot-password', data).then((r) => r.data),
};

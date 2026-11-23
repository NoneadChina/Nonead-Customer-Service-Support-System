import type { Entries } from '../../i18n/types';

/** 认证页文案（英文值）。必须用 `satisfies Entries`，不得写成类型标注 */
export const auth = {
  // ── Shared by all four auth pages ──
  'auth.common.backToLogin': 'Back to sign in',
  'auth.common.goToLogin': 'Go to sign in',
  'auth.common.showPassword': 'Show password',
  'auth.common.hidePassword': 'Hide password',
  'auth.common.emailLabel': 'Email',
  'auth.common.emailPlaceholder': 'Enter your email',
  'auth.common.passwordMismatch': 'Passwords do not match',
  'auth.common.validationErrorSeparator': '; ',
  'auth.common.passwordMinLengthPlaceholder': 'At least {min} characters',
  'auth.common.networkError': 'Network error, try again later',
  'auth.common.loading': 'Loading...',

  // ── Sign in ──
  'auth.login.title': 'Nonead Customer Service & Support System',
  // h1 已经是产品全称，副标题必须换一句，否则英文界面同一句话出现两遍
  'auth.login.tagline': 'Helpdesk and customer support, in one place',
  'auth.login.passwordLabel': 'Password',
  'auth.login.passwordPlaceholder': 'Enter your password',
  'auth.login.captchaLabel': 'CAPTCHA',
  // The input carries `uppercase` + 0.3em letter-spacing next to a 130px CAPTCHA image,
  // so "Enter the code" got clipped in the browser ("ENTER THE COI…" was visible).
  // Shortening the placeholder is in scope; the CSS fix is not (reported instead).
  'auth.login.captchaPlaceholder': 'Code',
  'auth.login.refreshCaptcha': 'Refresh CAPTCHA',
  'auth.login.captchaRefreshHint': 'Click to refresh the CAPTCHA',
  'auth.login.captchaHelp': 'Cannot read it? Click the image to refresh',
  'auth.login.forgotPasswordLink': 'Forgot password?',
  'auth.login.submit': 'Sign in',
  'auth.login.submitting': 'Signing in...',
  'auth.login.registerLink': "Don't have an account? Sign up",
  'auth.login.captchaFailed': 'Could not load the CAPTCHA. Refresh the page and try again.',
  'auth.login.captchaBackendDown': 'Cannot reach the backend (connection refused or 502). Start the API first (scripts/dev-up.ps1), then click the CAPTCHA image to retry.',
  'auth.login.failed': 'Sign-in failed. Check your email and password.',

  // ── Sign up ──
  'auth.register.title': 'Create an account',
  'auth.register.subtitle': 'Fill in the details below to sign up',
  'auth.register.firstNameLabel': 'First name',
  'auth.register.firstNamePlaceholder': 'First name',
  'auth.register.lastNameLabel': 'Last name',
  'auth.register.lastNamePlaceholder': 'Last name',
  'auth.register.passwordLabel': 'Password',
  'auth.register.confirmPasswordLabel': 'Confirm password',
  'auth.register.confirmPasswordPlaceholder': 'Enter the password again',
  'auth.register.passwordMatch': 'Passwords match',
  'auth.register.submit': 'Sign up',
  'auth.register.submitting': 'Signing up...',
  'auth.register.haveAccount': 'Already have an account?',
  'auth.register.nameRequired': 'Enter your first and last name',
  'auth.register.emailRequired': 'Enter your email',
  'auth.register.failed': 'Registration failed',
  'auth.register.failedRetry': 'Registration failed, try again later',
  'auth.register.disabledTitle': 'Registration is closed',
  'auth.register.disabledNote': 'Self-service registration is currently unavailable.',
  'auth.register.disabledContactAdmin': 'To request an account, contact your system administrator.',
  'auth.register.successTitle': 'Registration successful',
  'auth.register.successSubmitted': 'Your account request has been submitted',
  'auth.register.successPending':
    'Wait for an administrator to review and activate your account before signing in.',

  // ── Forgot password ──
  'auth.forgot.title': 'Forgot password',
  'auth.forgot.subtitle':
    'Enter your name and email. Once verified, we will send a password reset link.',
  'auth.forgot.securityBadge': 'Secure reset process',
  'auth.forgot.firstNameLabel': 'First name',
  'auth.forgot.firstNamePlaceholder': 'Enter your first name',
  'auth.forgot.lastNameLabel': 'Last name',
  'auth.forgot.lastNamePlaceholder': 'Enter your last name',
  'auth.forgot.emailLabel': 'Email',
  'auth.forgot.emailPlaceholder': 'Enter your email address',
  'auth.forgot.submit': 'Send reset link',
  'auth.forgot.submitting': 'Sending...',
  'auth.forgot.requestFailed': 'Request failed',
  'auth.forgot.successBadge': 'Identity verified',
  'auth.forgot.successTitle': 'Reset link sent',
  'auth.forgot.successSentTo': 'A password reset link was sent to',
  'auth.forgot.successTipExpiry': 'Check your inbox and reset your password within 24 hours',
  'auth.forgot.successTipSpam': 'If you do not receive the email, check your spam folder',
  'auth.forgot.successTipSingleUse': 'The reset link can be used only once, so keep it safe',

  // ── Reset password ──
  'auth.reset.title': 'Reset password',
  'auth.reset.subtitle': 'Enter your new password',
  'auth.reset.newPasswordLabel': 'New password',
  'auth.reset.confirmPasswordLabel': 'Confirm password',
  'auth.reset.confirmPasswordPlaceholder': 'Enter the new password again',
  'auth.reset.submit': 'Reset password',
  'auth.reset.submitting': 'Resetting...',
  'auth.reset.invalidToken': 'Invalid token. Request a new password reset.',
  'auth.reset.failed': 'Reset failed',
  'auth.reset.successTitle': 'Password reset successful',
  'auth.reset.successDescription':
    'Your password has been reset. You can now sign in with the new password.',
} satisfies Entries;

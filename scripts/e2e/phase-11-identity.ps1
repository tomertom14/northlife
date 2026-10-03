<#
  End-to-end check of Phase 11 (identity) against a running stack with Mailpit.
  Usage: pwsh scripts/e2e/phase-11-identity.ps1 -BaseUrl http://localhost:10000 -MailpitUrl http://localhost:8025 `
           -AdminEmail admin@northlife.local -AdminPassword '...'
  Exits non-zero when any check fails.
#>
param(
  [string]$BaseUrl = 'http://localhost:10000',
  [string]$MailpitUrl = 'http://localhost:8025',
  [Parameter(Mandatory)][string]$AdminEmail,
  [Parameter(Mandatory)][string]$AdminPassword,
  [string]$ImagePath = ''
)
$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not available in parameter defaults on Windows PowerShell 5.1.
if (-not $ImagePath) { $ImagePath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\docs\screenshots\phase-5-dashboard.png' }
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'e2e-common.ps1')

$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$email = "owner.$stamp@example.com"
$password = 'StrongPass123'

Write-Host "`n== Registration and email verification"
$register = Call POST '/api/auth/register' @{ fullName = 'בעלת עסק לבדיקה'; email = $email; password = $password; phone = '0501234567'; businessName = 'עסק לבדיקה' }
Check 'register returns 201 as an unverified business owner' ($register.Status -eq 201 -and $register.Json.user.emailConfirmed -eq $false -and $register.Json.user.role -eq 'BusinessOwner')
$ownerToken = $register.Json.token
$image = New-Image $ownerToken $ImagePath
$start = [DateTimeOffset]::UtcNow.AddDays(2).ToString('o'); $end = [DateTimeOffset]::UtcNow.AddDays(2).AddHours(2).ToString('o')
$eventBody = @{ title = 'אירוע בדיקה לאימות'; description = 'נוצר בבדיקת קצה לקצה'; category = 'Culture'; venueName = 'מרכז'; locality = 'צפת'; address = 'רחוב 1'; latitude = 32.96; longitude = 35.49; startAt = $start; endAt = $end; price = 0; imageId = $image.id; organizerName = 'עסק לבדיקה'; tags = @() }
$blocked = Call POST '/api/manage/events' $eventBody $ownerToken
Check 'unverified owner cannot submit an event (403 email_not_verified)' ($blocked.Status -eq 403 -and $blocked.Json.code -eq 'email_not_verified')
$verifyToken = Get-MailToken $email '/manage/verify-email'
Check 'verification email reaches Mailpit with a link token' ([bool]$verifyToken)
Check 'verification link is accepted (204)' ((Call POST '/api/auth/verify-email' @{ token = $verifyToken }).Status -eq 204)
Check 'the same link cannot be used twice (400)' ((Call POST '/api/auth/verify-email' @{ token = $verifyToken }).Status -eq 400)
Check 'session now reports a confirmed email' ((Call GET '/api/auth/session' $null $ownerToken).Json.emailConfirmed -eq $true)
Check 'verified owner can submit the event (201 Pending)' ((Call POST '/api/manage/events' $eventBody $ownerToken).Json.status -eq 'Pending')

Write-Host "`n== Forgot and reset password"
Check 'forgot-password answers 204 for an unknown address' ((Call POST '/api/auth/forgot-password' @{ email = "nobody.$stamp@example.com" }).Status -eq 204)
Check 'forgot-password answers 204 for a real account' ((Call POST '/api/auth/forgot-password' @{ email = $email }).Status -eq 204)
$resetToken = Get-MailToken $email '/manage/reset-password'
Check 'reset email reaches Mailpit' ([bool]$resetToken)
$newPassword = 'EvenStronger456'
Check 'reset rejects a weak password without spending the token' ((Call POST '/api/auth/reset-password' @{ token = $resetToken; password = 'weak' }).Status -eq 400)
Check 'reset accepts a strong password (204)' ((Call POST '/api/auth/reset-password' @{ token = $resetToken; password = $newPassword }).Status -eq 204)
Check 'old password no longer signs in (401)' ((Call POST '/api/auth/login' @{ email = $email; password = $password }).Status -eq 401)
$login = Call POST '/api/auth/login' @{ email = $email; password = $newPassword }
Check 'new password signs in' ($login.Status -eq 200 -and $login.Json.status -eq 'authenticated')
$ownerToken = $login.Json.token

Write-Host "`n== Owner TOTP enrollment and two-step sign-in"
$setup = (Call POST '/api/auth/security/totp/setup' $null $ownerToken).Json
Check 'setup returns a Base32 secret, otpauth URI and QR image' ($setup.secret -and $setup.provisioningUri -like 'otpauth://totp/*' -and $setup.qrCodeDataUri -like 'data:image/png;base64,*')
Check 'a wrong code does not enable 2FA (400)' ((Call POST '/api/auth/security/totp/enable' @{ code = '000000' } $ownerToken).Status -eq 400)
$enabled = Call POST '/api/auth/security/totp/enable' @{ code = (Get-Totp $setup.secret) } $ownerToken
Check 'a correct code enables 2FA and returns 10 backup codes' ($enabled.Status -eq 200 -and $enabled.Json.recoveryCodes.Count -eq 10 -and $enabled.Json.auth.user.mfaVerified -eq $true)
$challenge = Call POST '/api/auth/login' @{ email = $email; password = $newPassword }
Check 'password step now returns mfa_required and no token' ($challenge.Json.status -eq 'mfa_required' -and -not $challenge.Json.token)
Check 'the code used at enrollment cannot be replayed (401)' ((Call POST '/api/auth/mfa' @{ mfaToken = $challenge.Json.mfaToken; code = (Get-Totp $setup.secret) }).Status -eq 401)
$backup = $enabled.Json.recoveryCodes[0]
$viaBackup = Call POST '/api/auth/mfa' @{ mfaToken = $challenge.Json.mfaToken; code = $backup }
Check 'a backup code completes sign-in with mfaVerified' ($viaBackup.Status -eq 200 -and $viaBackup.Json.user.mfaVerified -eq $true)
Check 'the same backup code works only once (401)' ((Call POST '/api/auth/mfa' @{ mfaToken = $challenge.Json.mfaToken; code = $backup }).Status -eq 401)
Check 'security status counts 9 backup codes left' ((Call GET '/api/auth/security' $null $viaBackup.Json.token).Json.recoveryCodesLeft -eq 9)

Write-Host "`n== Administrator must enroll TOTP"
$admin = Call POST '/api/auth/login' @{ email = $AdminEmail; password = $AdminPassword }
if ($admin.Json.status -eq 'mfa_required') {
  Write-Host 'SKIP  administrator already enrolled; run on a fresh admin to repeat this block'
} else {
  Check 'admin password sign-in is not yet MFA-verified' ($admin.Json.user.role -eq 'Admin' -and $admin.Json.user.mfaVerified -eq $false)
  $gate = Call GET '/api/admin/events' $null $admin.Json.token
  Check 'admin API refuses a session without TOTP (403 mfa_required)' ($gate.Status -eq 403 -and $gate.Json.code -eq 'mfa_required')
  $adminSetup = (Call POST '/api/auth/security/totp/setup' $null $admin.Json.token).Json
  $adminEnabled = Call POST '/api/auth/security/totp/enable' @{ code = (Get-Totp $adminSetup.secret) } $admin.Json.token
  Check 'after enrollment the admin API answers 200' ((Call GET '/api/admin/events' $null $adminEnabled.Json.auth.token).Status -eq 200)
  Check 'administrators cannot switch 2FA off (403)' ((Call POST '/api/auth/security/totp/disable' @{ code = $adminEnabled.Json.recoveryCodes[0] } $adminEnabled.Json.auth.token).Status -eq 403)
  Write-Host "INFO  admin TOTP secret for manual sign-in: $($adminSetup.secret)"
}

Write-Host "`n$(if ($failures) { "$failures check(s) failed" } else { 'All checks passed' })"
exit $failures

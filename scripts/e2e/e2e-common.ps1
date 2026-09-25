<#
  Shared helpers for the end-to-end scripts. Dot-source after setting $BaseUrl and $MailpitUrl.
  Works on Windows PowerShell 5.1 and PowerShell 7.
#>
$script:failures = 0

function Check([string]$Name, [bool]$Condition) {
  if ($Condition) { Write-Host "PASS  $Name" } else { Write-Host "FAIL  $Name" -ForegroundColor Red; $script:failures++ }
}

# HTTP errors are caught, not thrown, so every call returns its status and parsed JSON body.
function Call([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
  $headers = @{}
  if ($Token) { $headers.Authorization = "Bearer $Token" }
  $params = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $headers; UseBasicParsing = $true }
  if ($null -ne $Body) {
    $params.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5))
    $params.ContentType = 'application/json; charset=utf-8'
  }
  try {
    $response = Invoke-WebRequest @params
    $status = [int]$response.StatusCode
    $content = $response.Content
    # Windows PowerShell 5.1 decodes JSON without a charset as Latin-1; re-read the raw bytes as UTF-8.
    if ($response.RawContentStream) { $content = [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray()) }
  } catch {
    $failed = $_.Exception.Response
    if (-not $failed) { throw }
    $status = [int]$failed.StatusCode
    $content = $_.ErrorDetails.Message
    # Windows PowerShell 5.1 does not always fill ErrorDetails; read the body stream instead.
    if (-not $content -and $failed -is [System.Net.HttpWebResponse]) {
      $stream = $failed.GetResponseStream()
      if ($stream.CanSeek) { $stream.Position = 0 }
      $content = (New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)).ReadToEnd()
    }
  }
  $json = $null
  if ($content) { try { $json = $content | ConvertFrom-Json } catch { } }
  [pscustomobject]@{ Status = $status; Json = $json }
}

# RFC 6238 TOTP computed here, independently of the API implementation.
function Get-Totp([string]$Secret, [long]$StepOffset = 0) {
  $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  $bits = ($Secret.ToUpperInvariant() -replace '[^A-Z2-7]', '').ToCharArray() | ForEach-Object { [Convert]::ToString($alphabet.IndexOf($_), 2).PadLeft(5, '0') }
  $bitString = -join $bits
  $key = for ($i = 0; $i + 8 -le $bitString.Length; $i += 8) { [Convert]::ToByte($bitString.Substring($i, 8), 2) }
  $step = [long][Math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30) + $StepOffset
  $counter = [BitConverter]::GetBytes($step); [Array]::Reverse($counter)
  $hmac = [System.Security.Cryptography.HMACSHA1]::new([byte[]]$key)
  $hash = $hmac.ComputeHash($counter)
  $offset = $hash[19] -band 15
  # Widen to [int] first: PowerShell keeps -shl on a [byte] as a byte and overflows to 0.
  $binary = (([int]$hash[$offset] -band 0x7f) -shl 24) -bor ([int]$hash[$offset + 1] -shl 16) -bor ([int]$hash[$offset + 2] -shl 8) -bor [int]$hash[$offset + 3]
  ($binary % 1000000).ToString('000000')
}

<#
  Completes the TOTP step of a sign-in. The server refuses a time step it has already accepted for
  this user (replay guard), so when the current step was used moments ago, try the next one (inside
  the ±1 window) and, if that is spent too, wait for a fresh step.
#>
function Complete-Mfa([string]$MfaToken, [string]$Secret) {
  for ($attempt = 0; $attempt -lt 4; $attempt++) {
    foreach ($offset in 0, 1) {
      $result = Call POST '/api/auth/mfa' @{ mfaToken = $MfaToken; code = (Get-Totp $Secret $offset) }
      if ($result.Status -eq 200) { return $result }
    }
    Start-Sleep -Seconds (31 - ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() % 30))
  }
  return $result
}

# Password plus TOTP sign-in; returns the final session response.
function Connect-WithTotp([string]$Email, [string]$Password, [string]$Secret) {
  $first = Call POST '/api/auth/login' @{ email = $Email; password = $Password }
  if ($first.Json.status -ne 'mfa_required') { return $first }
  Complete-Mfa $first.Json.mfaToken $Secret
}

# Newest message to $To whose link points at $LinkPath (ASCII, so no charset guessing is needed).
function Get-MailToken([string]$To, [string]$LinkPath) {
  for ($i = 0; $i -lt 15; $i++) {
    $search = Invoke-RestMethod "$MailpitUrl/api/v1/search?query=to:$To"
    foreach ($message in $search.messages) {
      $text = (Invoke-RestMethod "$MailpitUrl/api/v1/message/$($message.ID)").Text
      if ($text -match "$([regex]::Escape($LinkPath))\?token=([A-Za-z0-9_-]+)") { return $Matches[1] }
    }
    Start-Sleep -Milliseconds 500
  }
  return $null
}

function New-Image([string]$Token, [string]$Path) {
  $resolved = (Resolve-Path $Path).Path
  (& curl.exe --silent --fail -X POST "$BaseUrl/api/manage/images" -H "Authorization: Bearer $Token" -F "file=@$resolved;type=image/png") | ConvertFrom-Json
}

# Registers a business owner and confirms the address through the Mailpit link; returns email and token.
function New-VerifiedOwner([string]$Label, [string]$Password) {
  $email = "$Label.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())@example.com"
  $register = Call POST '/api/auth/register' @{ fullName = "בדיקה $Label"; email = $email; password = $Password; phone = '0501234567'; businessName = "עסק $Label" }
  $token = Get-MailToken $email '/manage/verify-email'
  $null = Call POST '/api/auth/verify-email' @{ token = $token }
  [pscustomobject]@{ Email = $email; Token = $register.Json.token; Id = $register.Json.user.id }
}

<#
  Sends several requests at the same moment on separate connections and returns their status codes
  in order. Used to race two administrators against each other.
#>
function Invoke-Simultaneously([object[]]$Requests) {
  Add-Type -AssemblyName System.Net.Http
  $handler = New-Object System.Net.Http.HttpClientHandler
  $client = New-Object System.Net.Http.HttpClient($handler)
  $messages = foreach ($request in $Requests) {
    $message = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$BaseUrl$($request.Path)")
    $message.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $request.Token)
    $message.Content = New-Object System.Net.Http.StringContent(($request.Body | ConvertTo-Json -Depth 5), [Text.Encoding]::UTF8, 'application/json')
    $message
  }
  $tasks = [System.Threading.Tasks.Task[]]@($messages | ForEach-Object { $client.SendAsync($_) })
  [System.Threading.Tasks.Task]::WaitAll($tasks)
  $statuses = $tasks | ForEach-Object { [int]$_.Result.StatusCode }
  $client.Dispose()
  $statuses
}

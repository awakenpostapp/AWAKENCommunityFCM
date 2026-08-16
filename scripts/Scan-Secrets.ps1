param(
  [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$scanRoots = @('backend', 'Services', 'Models', 'Views', 'Ui', 'MauiProgram.cs', 'App.xaml.cs', 'CommunityFootballClubManager.csproj', 'README.md')
$patterns = @(
  '(?i)client_secret[^\r\n]*\.json',
  '(?i)-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
  '(?i)gh[pousr]_[A-Za-z0-9_\-]{20,}',
  '(?i)AIza[0-9A-Za-z_\-]{30,}',
  '(?i)"(JWT_SECRET|ADMIN_BOOTSTRAP_SECRET|GOOGLE_OAUTH_CLIENT_SECRET)"\s*:\s*"(?!replace-with)[^"]+"'
)

$files = @()
foreach ($scanRoot in $scanRoots) {
  $path = Join-Path $Root $scanRoot
  if (Test-Path -LiteralPath $path -PathType Container) {
    $files += Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue
  } elseif (Test-Path -LiteralPath $path -PathType Leaf) {
    $files += Get-Item -LiteralPath $path
  }
}
$files = @($files | Where-Object {
  $_.Length -le 2MB -and $_.FullName -notmatch '\\(?:node_modules|\.tools|\.wrangler|dist|coverage)\\'
})

$findings = @()
foreach ($file in $files) {
  $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
  if ($null -eq $text) { continue }
  foreach ($pattern in $patterns) {
    if ($text -match $pattern) {
      $findings += $file.FullName
      break
    }
  }
}

if ($findings.Count -gt 0) {
  $findings | Sort-Object -Unique | ForEach-Object { Write-Error "Potential secret: $_" }
  exit 1
}

Write-Output "Secret scan passed: $($files.Count) files checked."

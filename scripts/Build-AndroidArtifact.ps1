[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Bundle,

    [string]$OutputDirectory = ''
)

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $projectRoot 'CommunityFootballClubManager.csproj'
$projectText = Get-Content -LiteralPath $projectFile -Raw

function Get-ProjectProperty([string]$PropertyName) {
    $pattern = "<$PropertyName>(?<value>[^<]+)</$PropertyName>"
    $match = [regex]::Match($projectText, $pattern)
    if (-not $match.Success) {
        throw "Không tìm thấy $PropertyName trong $projectFile."
    }

    return $match.Groups['value'].Value.Trim()
}

$displayVersion = Get-ProjectProperty 'ApplicationDisplayVersion'
$applicationVersion = Get-ProjectProperty 'ApplicationVersion'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$format = if ($Bundle) { 'aab' } else { 'apk' }
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$stagingDirectory = Join-Path $projectRoot "obj\artifact-build\$timestamp-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

try {
    $publishArguments = @(
        'publish',
        $projectFile,
        '--configuration', $Configuration,
        '--framework', 'net10.0-android',
        '--no-restore',
        '-p:UseSharedCompilation=false',
        "-p:AndroidPackageFormat=$format",
        "-p:PublishDir=$stagingDirectory\"
    )

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish thất bại với mã $LASTEXITCODE."
    }

    $package = Get-ChildItem -LiteralPath $stagingDirectory -File |
        Where-Object { $_.Extension -ieq ".$format" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $package) {
        throw "Không tìm thấy gói .$format trong thư mục publish."
    }

    $artifactBaseName = "AWAKENCommunityFCM-v$displayVersion-build$applicationVersion-$Configuration"
    $destination = Join-Path $OutputDirectory "$artifactBaseName.$format"
    $counter = 1
    while (Test-Path -LiteralPath $destination) {
        $destination = Join-Path $OutputDirectory "$artifactBaseName-$('{0:D2}' -f $counter).$format"
        $counter++
    }

    # Không dùng -Force: nếu tên đã tồn tại, vòng lặp sẽ chọn tên mới.
    Copy-Item -LiteralPath $package.FullName -Destination $destination -ErrorAction Stop

    Write-Output "Đã tạo: $destination"
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

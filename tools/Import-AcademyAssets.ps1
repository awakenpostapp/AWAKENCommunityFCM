[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$assetRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$imageRoot = Join-Path $assetRoot 'Resources\Images'
$licenseRoot = Join-Path $assetRoot 'Resources\Licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
$iconMap = [ordered]@{
    tab_home='home'; tab_classes='calendar'; tab_people='users';
    tab_finance='wallet'; tab_achievements='trophy'; tab_attendance='clipboard-check';
    tab_notifications='bell'; tab_profile='user'; tab_more='settings'; tab_tuition='receipt';
    password_eye='eye'; password_eye_off='eye-off'; icon_trophy='trophy';
    icon_trash='trash'; icon_soccer_ball='ball-football'; icon_send='send';
    icon_plus='plus'; icon_login='login'; icon_help='help-circle';
    icon_edit='pencil'; icon_bell='bell'; icon_chevron_right='chevron-right';
    icon_arrow_right='arrow-right'; icon_clock='clock'; icon_gift='gift';
    icon_check='circle-check'; icon_search='search'; icon_minus='circle-minus'
}
# Import upstream MIT artwork; only resolve currentColor for MAUI's SVG rasterizer.
# This is a mechanical asset import, not hand-drawn vector artwork.
$tablerBase = 'https://raw.githubusercontent.com/tabler/tabler-icons/v3.46.0'
foreach ($icon in $iconMap.GetEnumerator()) {
    $svg = (Invoke-WebRequest -Uri "$tablerBase/icons/outline/$($icon.Value).svg").Content
    if ($svg -notmatch '<svg') { throw "Invalid SVG response for $($icon.Value)" }
    $svg.Replace('currentColor', '#103C34').TrimEnd() | Set-Content -LiteralPath (Join-Path $imageRoot "$($icon.Key).svg") -Encoding utf8
    $svg.Replace('currentColor', '#FFFFFF').TrimEnd() | Set-Content -LiteralPath (Join-Path $imageRoot "$($icon.Key)_white.svg") -Encoding utf8
}
Invoke-WebRequest -Uri "$tablerBase/LICENSE" -OutFile (Join-Path $licenseRoot 'Tabler-Icons-MIT.txt')
$fontBase = 'https://raw.githubusercontent.com/google/fonts/main/ofl/nunitosans'
Invoke-WebRequest -Uri "$fontBase/NunitoSans%5BYTLC%2Copsz%2Cwdth%2Cwght%5D.ttf" -OutFile (Join-Path $assetRoot 'Resources\Fonts\NunitoSans-Variable.ttf')
Invoke-WebRequest -Uri "$fontBase/OFL.txt" -OutFile (Join-Path $licenseRoot 'Nunito-Sans-OFL.txt')
Write-Output 'Imported Tabler outline icons with dark/white variants and Nunito Sans with licenses.'

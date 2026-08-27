param(
    [Parameter(Mandatory = $true)]
    [string] $Source,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$cropSpecs = @(
    @{ Key = "cup_ngoai_hang"; X = 24; Y = 16; Width = 218; Height = 286 },
    @{ Key = "cup_hang_1"; X = 252; Y = 16; Width = 218; Height = 286 },
    @{ Key = "cup_hang_2"; X = 480; Y = 16; Width = 218; Height = 286 },
    @{ Key = "cup_hang_3"; X = 708; Y = 16; Width = 218; Height = 286 },
    @{ Key = "huy_chuong_vang"; X = 928; Y = 16; Width = 208; Height = 286 },
    @{ Key = "huy_chuong_bac"; X = 1138; Y = 16; Width = 208; Height = 286 },
    @{ Key = "huy_chuong_dong"; X = 1348; Y = 16; Width = 184; Height = 286 },
    @{ Key = "gang_tay_vang"; X = 22; Y = 330; Width = 220; Height = 200 },
    @{ Key = "qua_bong_vang"; X = 252; Y = 330; Width = 220; Height = 200 },
    @{ Key = "cau_thu_xuat_sac"; X = 482; Y = 330; Width = 226; Height = 200 },
    @{ Key = "vong_nguyet_que"; X = 718; Y = 330; Width = 250; Height = 200 },
    @{ Key = "the_vang"; X = 1018; Y = 330; Width = 190; Height = 200 },
    @{ Key = "the_do"; X = 1248; Y = 330; Width = 190; Height = 200 },
    @{ Key = "tham_gia"; X = 18; Y = 578; Width = 190; Height = 165 },
    @{ Key = "tich_cuc"; X = 208; Y = 578; Width = 190; Height = 165 },
    @{ Key = "ghi_ban"; X = 398; Y = 578; Width = 190; Height = 165 },
    @{ Key = "giu_sach_luoi"; X = 588; Y = 578; Width = 190; Height = 165 },
    @{ Key = "fair_play"; X = 778; Y = 578; Width = 190; Height = 165 },
    @{ Key = "tinh_than_tot"; X = 968; Y = 578; Width = 190; Height = 165 },
    @{ Key = "tien_bo"; X = 1158; Y = 578; Width = 190; Height = 165 },
    @{ Key = "no_luc_xuat_sac"; X = 1348; Y = 578; Width = 188; Height = 165 }
)

function Test-BlackPixel([System.Drawing.Color] $color) {
    # The source sheet has a pure black canvas. A small tolerance removes
    # JPEG ringing around the canvas while preserving enclosed dark details.
    return $color.R -le 28 -and $color.G -le 28 -and $color.B -le 28
}

function Set-TransparentBackground([System.Drawing.Bitmap] $bitmap) {
    $width = $bitmap.Width
    $height = $bitmap.Height
    $background = [bool[,]]::new($width, $height)
    $queue = [System.Collections.Generic.Queue[System.Drawing.Point]]::new()

    function Add-BackgroundPoint([int] $x, [int] $y) {
        if ($x -lt 0 -or $x -ge $width -or $y -lt 0 -or $y -ge $height) { return }
        if ($background[$x, $y]) { return }
        if (-not (Test-BlackPixel $bitmap.GetPixel($x, $y))) { return }
        $background[$x, $y] = $true
        $queue.Enqueue([System.Drawing.Point]::new($x, $y))
    }

    for ($x = 0; $x -lt $width; $x++) {
        Add-BackgroundPoint $x 0
        Add-BackgroundPoint $x ($height - 1)
    }
    for ($y = 0; $y -lt $height; $y++) {
        Add-BackgroundPoint 0 $y
        Add-BackgroundPoint ($width - 1) $y
    }

    while ($queue.Count -gt 0) {
        $point = $queue.Dequeue()
        Add-BackgroundPoint ($point.X + 1) $point.Y
        Add-BackgroundPoint ($point.X - 1) $point.Y
        Add-BackgroundPoint $point.X ($point.Y + 1)
        Add-BackgroundPoint $point.X ($point.Y - 1)
    }

    $minX = $width
    $minY = $height
    $maxX = -1
    $maxY = -1
    for ($x = 0; $x -lt $width; $x++) {
        for ($y = 0; $y -lt $height; $y++) {
            if ($background[$x, $y]) {
                $bitmap.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
                continue
            }

            $minX = [Math]::Min($minX, $x)
            $minY = [Math]::Min($minY, $y)
            $maxX = [Math]::Max($maxX, $x)
            $maxY = [Math]::Max($maxY, $y)
        }
    }

    if ($maxX -lt $minX -or $maxY -lt $minY) {
        throw "Không tìm thấy nội dung biểu trưng trong crop ${width}x${height}."
    }

    $padding = 6
    $outWidth = ($maxX - $minX + 1) + ($padding * 2)
    $outHeight = ($maxY - $minY + 1) + ($padding * 2)
    $output = [System.Drawing.Bitmap]::new(
        $outWidth,
        $outHeight,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($x = 0; $x -lt $outWidth; $x++) {
        for ($y = 0; $y -lt $outHeight; $y++) {
            $sourceX = $minX + $x - $padding
            $sourceY = $minY + $y - $padding
            if ($sourceX -lt 0 -or $sourceX -ge $width -or $sourceY -lt 0 -or $sourceY -ge $height) {
                $output.SetPixel($x, $y, [System.Drawing.Color]::Transparent)
            } else {
                $output.SetPixel($x, $y, $bitmap.GetPixel($sourceX, $sourceY))
            }
        }
    }
    return $output
}

if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    throw "Không tìm thấy ảnh nguồn: $Source"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$sourceBitmap = [System.Drawing.Bitmap]::new($Source)
try {
    foreach ($spec in $cropSpecs) {
        if ($spec.X + $spec.Width -gt $sourceBitmap.Width -or $spec.Y + $spec.Height -gt $sourceBitmap.Height) {
            throw "Crop $($spec.Key) vượt ngoài ảnh nguồn."
        }
        $crop = [System.Drawing.Bitmap]::new(
            $spec.Width,
            $spec.Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($crop)
            try {
                $graphics.DrawImage(
                    $sourceBitmap,
                    [System.Drawing.Rectangle]::new(0, 0, $spec.Width, $spec.Height),
                    [System.Drawing.Rectangle]::new($spec.X, $spec.Y, $spec.Width, $spec.Height),
                    [System.Drawing.GraphicsUnit]::Pixel)
            } finally {
                $graphics.Dispose()
            }

            $transparent = Set-TransparentBackground $crop
            try {
                $path = Join-Path $OutputDirectory ("achievement_badge_{0}.png" -f $spec.Key)
                $transparent.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
                Write-Output ("$($spec.Key): $($transparent.Width)x$($transparent.Height) -> $path")
            } finally {
                $transparent.Dispose()
            }
        } finally {
            $crop.Dispose()
        }
    }
} finally {
    $sourceBitmap.Dispose()
}

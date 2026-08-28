param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# Each supplied file is a 256x256 export. Five exports contain a caption at
# the bottom and are deliberately cropped above it. The remaining files use
# the complete canvas and are tightened automatically after background
# removal. Keeping this mapping explicit makes replacement repeatable without
# relying on OCR or a generative image edit.
$sources = @(
    @{ File = "01_cup_ngoai_hang.png"; Key = "cup_ngoai_hang"; Height = 256 },
    @{ File = "02_cup_hang_1.png"; Key = "cup_hang_1"; Height = 256 },
    @{ File = "03_cup_hang_2.png"; Key = "cup_hang_2"; Height = 256 },
    @{ File = "04_cup_hang_3.png"; Key = "cup_hang_3"; Height = 256 },
    @{ File = "05_huy_chuong_vang.png"; Key = "huy_chuong_vang"; Height = 256 },
    @{ File = "06_huy_chuong_bac.png"; Key = "huy_chuong_bac"; Height = 256 },
    @{ File = "07_huy_chuong_dong.png"; Key = "huy_chuong_dong"; Height = 256 },
    @{ File = "08_gang_tay_vang.png"; Key = "gang_tay_vang"; Height = 220 },
    @{ File = "09_qua_bong_vang.png"; Key = "qua_bong_vang"; Height = 226 },
    @{ File = "10_cau_thu_xuat_sac.png"; Key = "cau_thu_xuat_sac"; Height = 224 },
    @{ File = "11_vong_nguyet_que.png"; Key = "vong_nguyet_que"; Height = 224 },
    @{ File = "12_the_vang.png"; Key = "the_vang"; Height = 222 },
    @{ File = "13_the_do.png"; Key = "the_do"; Height = 223 },
    @{ File = "14_tham_gia.png"; Key = "tham_gia"; Height = 230 },
    @{ File = "15_tich_cuc.png"; Key = "tich_cuc"; Height = 230 },
    @{ File = "16_ghi_ban.png"; Key = "ghi_ban"; Height = 229 },
    @{ File = "17_giu_sach_luoi.png"; Key = "giu_sach_luoi"; Height = 228 },
    @{ File = "18_fair_play.png"; Key = "fair_play"; Height = 229 },
    @{ File = "19_tinh_than_tot.png"; Key = "tinh_than_tot"; Height = 228 },
    @{ File = "20_tien_bo.png"; Key = "tien_bo"; Height = 228 },
    @{ File = "21_no_luc_xuat_sac.png"; Key = "no_luc_xuat_sac"; Height = 224 }
)

function Test-BackgroundPixel([System.Drawing.Color] $color) {
    # The supplied PNGs use a pure-black canvas. A small tolerance removes
    # anti-aliased edge pixels while preserving enclosed black shield details.
    return $color.R -le 32 -and $color.G -le 32 -and $color.B -le 32
}

function Remove-ConnectedBackground([System.Drawing.Bitmap] $bitmap) {
    $width = $bitmap.Width
    $height = $bitmap.Height
    $background = [bool[,]]::new($width, $height)
    $queue = [System.Collections.Generic.Queue[System.Drawing.Point]]::new()

    function Add-Point([int] $x, [int] $y) {
        if ($x -lt 0 -or $x -ge $width -or $y -lt 0 -or $y -ge $height) {
            return
        }
        if ($background[$x, $y]) {
            return
        }
        if (-not (Test-BackgroundPixel $bitmap.GetPixel($x, $y))) {
            return
        }
        $background[$x, $y] = $true
        $queue.Enqueue([System.Drawing.Point]::new($x, $y))
    }

    for ($x = 0; $x -lt $width; $x++) {
        Add-Point $x 0
        Add-Point $x ($height - 1)
    }
    for ($y = 0; $y -lt $height; $y++) {
        Add-Point 0 $y
        Add-Point ($width - 1) $y
    }

    while ($queue.Count -gt 0) {
        $point = $queue.Dequeue()
        Add-Point ($point.X + 1) $point.Y
        Add-Point ($point.X - 1) $point.Y
        Add-Point $point.X ($point.Y + 1)
        Add-Point $point.X ($point.Y - 1)
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
        throw "No badge pixels found in crop ${width}x${height}."
    }

    $padding = 6
    $output = [System.Drawing.Bitmap]::new(
        ($maxX - $minX + 1) + ($padding * 2),
        ($maxY - $minY + 1) + ($padding * 2),
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($x = 0; $x -lt $output.Width; $x++) {
        for ($y = 0; $y -lt $output.Height; $y++) {
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

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Source directory not found: $SourceDirectory"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

foreach ($source in $sources) {
    $sourcePath = Join-Path $SourceDirectory $source.File
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Source image not found: $sourcePath"
    }

    $input = [System.Drawing.Bitmap]::new($sourcePath)
    try {
        if ($input.Width -ne 256 -or $input.Height -ne 256) {
            throw "Image $($source.File) must be 256x256 (actual $($input.Width)x$($input.Height))."
        }
        $crop = [System.Drawing.Bitmap]::new(
            256,
            $source.Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($crop)
            try {
                $graphics.DrawImage(
                    $input,
                    [System.Drawing.Rectangle]::new(0, 0, 256, $source.Height),
                    [System.Drawing.Rectangle]::new(0, 0, 256, $source.Height),
                    [System.Drawing.GraphicsUnit]::Pixel)
            } finally {
                $graphics.Dispose()
            }

            $transparent = Remove-ConnectedBackground $crop
            try {
                $path = Join-Path $OutputDirectory ("achievement_badge_{0}.png" -f $source.Key)
                $transparent.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
                Write-Output ("$($source.Key): $($transparent.Width)x$($transparent.Height) -> $path")
            } finally {
                $transparent.Dispose()
            }
        } finally {
            $crop.Dispose()
        }
    } finally {
        $input.Dispose()
    }
}

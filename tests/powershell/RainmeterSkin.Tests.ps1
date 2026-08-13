$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\TestHarness.ps1"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$iniPath = Join-Path $root 'Skins\MiniMaxUsage\MiniMaxUsage.ini'
$luaPath = Join-Path $root 'Skins\MiniMaxUsage\@Resources\Freshness.lua'
$ini = Get-Content $iniPath -Raw -Encoding UTF8

It 'uses generated variables and WebParser with correct config' {
    Assert-True ($ini -notmatch 'D:/claude_work') 'skin must not contain the development path'
    Assert-True ($ini -match '@Include=#@#Variables.inc') 'Variables.inc include missing'
    Assert-True ($ini -match 'MeasureReset1') 'MeasureReset1 missing'
    Assert-True ($ini -match 'MeasureReset2') 'MeasureReset2 missing'
    Assert-True ($ini -match 'MeasureUpdated') 'MeasureUpdated missing'
    Assert-True ($ini -match 'MeasureStatusText') 'MeasureStatusText missing'
    Assert-True ($ini -match 'MeasureDetailText') 'MeasureDetailText missing'
    Assert-True ($ini -match 'SkinWidth=280') 'SkinWidth must be 280'
}

It 'contains two complete quota rings' {
    foreach ($name in @('MeasureBar1','MeasureBar2','MeterRing1Track','MeterRing1','MeterRing2Track','MeterRing2')) {
        Assert-True ($ini -match "\[$name\]") "missing [$name]"
    }
    Assert-True (($ini | Select-String -Pattern 'Meter=Roundline' -AllMatches).Matches.Count -ge 4) 'four Roundline meters required'
}

It 'launches the detail executable with project-dir' {
    Assert-True ($ini -match 'LeftMouseUpAction=.*#DetailExe#.*--project-dir.*#ProjectDir#') 'detail launch action missing'
}

It 'has a freshness script that reads cache text files' {
    Assert-True (Test-Path $luaPath) 'Freshness.lua missing'
    $lua = Get-Content $luaPath -Raw -Encoding UTF8
    Assert-True ($lua -match 'status\.txt') 'status.txt reference missing'
    Assert-True ($lua -match 'reset1\.txt') 'reset1.txt reference missing'
}

function Get-IniValue {
    param([string]$Section, [string]$Key)
    $sectionPattern = "(?ms)^\[$([regex]::Escape($Section))\]\s*(.*?)(?=^\[|\z)"
    $sectionMatch = [regex]::Match($ini, $sectionPattern)
    if (-not $sectionMatch.Success) { return $null }
    $keyPattern = "(?m)^$([regex]::Escape($Key))=(.*)$"
    $keyMatch = [regex]::Match($sectionMatch.Groups[1].Value, $keyPattern)
    if (-not $keyMatch.Success) { return $null }
    return $keyMatch.Groups[1].Value.Trim()
}

It 'uses compact 280px canvas without clipping the rings' {
    Assert-Equal '280' (Get-IniValue 'Rainmeter' 'SkinWidth') 'SkinWidth must be 280'
    Assert-True ((Get-IniValue 'MeterBackground' 'Shape') -match 'Rectangle 0,0,280,165') 'background must match 280px canvas'

    $ring1X = [int](Get-IniValue 'MeterRing1' 'X')
    $ring2X = [int](Get-IniValue 'MeterRing2' 'X')
    $ring1W = [int](Get-IniValue 'MeterRing1' 'W')
    $ring2W = [int](Get-IniValue 'MeterRing2' 'W')
    Assert-Equal 94 $ring1W 'left ring diameter must be 94'
    Assert-Equal 94 $ring2W 'right ring diameter must be 94'
    Assert-True ($ring1X -ge 18) 'left ring needs at least 18px margin'
    Assert-True (($ring2X + $ring2W) -le 262) 'right ring needs at least 18px margin'
}

It 'draws a full 360-degree ring with valid inner and outer radii' {
    foreach ($section in @('MeterRing1Track','MeterRing1','MeterRing2Track','MeterRing2')) {
        $lineStart = [int](Get-IniValue $section 'LineStart')
        $lineLength = [int](Get-IniValue $section 'LineLength')
        $w = [int](Get-IniValue $section 'W')
        $h = [int](Get-IniValue $section 'H')
        $radius = [int]([Math]::Min($w, $h) / 2)
        $outer = $lineStart + $lineLength
        Assert-True ($outer -le $radius) "$section ring must fit within control (outer=$outer, radius=$radius)"
        Assert-True ($lineStart -ge 15) "$section inner radius must be at least 15"
        Assert-True ((Get-IniValue $section 'RotationAngle') -ge '6.283') "$section must rotate a full circle (>= 2π)"
    }
}

It 'uses a small percentage font' {
    Assert-Equal '11' (Get-IniValue 'MeterRing1Value' 'FontSize') 'left percentage font must be 11'
    Assert-Equal '11' (Get-IniValue 'MeterRing2Value' 'FontSize') 'right percentage font must be 11'
}

It 'centers percentages and places reset text below the rings' {
    foreach ($n in 1,2) {
        $ringSection = "MeterRing$n"
        $valueSection = "MeterRing${n}Value"
        $resetSection = "MeterRing${n}Reset"
        $centerX = [int](Get-IniValue $ringSection 'X') + [int](Get-IniValue $ringSection 'W') / 2
        $centerY = [int](Get-IniValue $ringSection 'Y') + [int](Get-IniValue $ringSection 'H') / 2
        Assert-Equal $centerX ([int](Get-IniValue $valueSection 'X')) "$valueSection X must equal ring center"
        Assert-Equal $centerY ([int](Get-IniValue $valueSection 'Y')) "$valueSection Y must equal ring center"
        $ringBottom = [int](Get-IniValue $ringSection 'Y') + [int](Get-IniValue $ringSection 'H')
        Assert-True (([int](Get-IniValue $resetSection 'Y')) -gt $ringBottom) "$resetSection must be below the ring"
    }
}

Complete-Tests
param([string]$ProjectPath = '')
$ErrorActionPreference = 'Stop'
$rememberedProjectFile = Join-Path $env:LOCALAPPDATA 'Karolina/last-project.txt'
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $rememberedProject = if (Test-Path -LiteralPath $rememberedProjectFile) { (Get-Content -LiteralPath $rememberedProjectFile -Raw).Trim() } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($rememberedProject) -and (Test-Path -LiteralPath (Join-Path $rememberedProject 'Assets')) -and (Test-Path -LiteralPath (Join-Path $rememberedProject 'ProjectSettings'))) {
        $ProjectPath = $rememberedProject
    } else {
        $ProjectPath = Read-Host '首次启动或上次工程不可用，请输入 Unity 工程根目录（包含 Assets 和 ProjectSettings）'
    }
}
if ([string]::IsNullOrWhiteSpace($ProjectPath)) { throw '未提供 Unity 工程路径，已取消启动。' }
if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
    if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'Assets')) -or -not (Test-Path -LiteralPath (Join-Path $ProjectPath 'ProjectSettings'))) { throw '指定目录不是完整的 Unity 工程根目录（缺少 Assets 或 ProjectSettings）。' }
}
$karolinaProject = Join-Path $PSScriptRoot 'Karolina.Desktop/Karolina.Desktop.csproj'
$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$karolinaOutput = Join-Path $artifactRoot 'current'
$desktopRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Karolina.Desktop'))
$outputPrefix = $repositoryRoot + [IO.Path]::DirectorySeparatorChar
$normalizedOutput = [IO.Path]::GetFullPath($karolinaOutput)
if (-not $normalizedOutput.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw '构建输出必须位于 Karolina 仓库内。' }
$relativeOutput = [IO.Path]::GetRelativePath($repositoryRoot, $normalizedOutput)
$checkedOutputPath = $repositoryRoot
$outputSegments = @($relativeOutput.Split([char[]]@([IO.Path]::DirectorySeparatorChar)))
for ($index = 0; $index -lt $outputSegments.Count; $index++) {
    $checkedOutputPath = Join-Path $checkedOutputPath $outputSegments[$index]
    $outputEntry = Get-Item -LiteralPath $checkedOutputPath -Force -ErrorAction SilentlyContinue
    if ($null -ne $outputEntry) {
        if (($outputEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "拒绝将构建输出写入链接路径：$checkedOutputPath" }
        if ($index -lt ($outputSegments.Count - 1) -and -not $outputEntry.PSIsContainer) { throw "构建输出的父路径不是目录：$checkedOutputPath" }
    }
}
if (Test-Path -LiteralPath $normalizedOutput -PathType Container) {
    $linkedOutputEntry = Get-ChildItem -LiteralPath $normalizedOutput -Force -Recurse | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } | Select-Object -First 1
    if ($null -ne $linkedOutputEntry) { throw "构建输出目录内存在链接，拒绝继续：$($linkedOutputEntry.FullName)" }
}
$activeApps = @(Get-CimInstance Win32_Process -Filter "name='Karolina.Desktop.exe'" | Where-Object {
    $_.ExecutablePath -and (([IO.Path]::GetFullPath($_.ExecutablePath)).StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or ([IO.Path]::GetFullPath($_.ExecutablePath)).StartsWith($desktopRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
})
if ($activeApps.Count -gt 0) { throw 'Karolina 正在运行。请从系统托盘选择“完全退出”后重新运行启动入口；窗口 X 只收起到托盘，更新仍使用同一个 current 目录。' }
& dotnet build $karolinaProject -c Release --output $karolinaOutput --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Karolina build failed' }
# 旧生成目录可恢复地移出程序目录；源码与 current 始终保留。
$retiredOutputRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Karolina/recovery/retired-build-outputs'))
New-Item -ItemType Directory -Path $retiredOutputRoot -Force | Out-Null
foreach ($oldOutput in @(Get-ChildItem -LiteralPath $artifactRoot -Directory | Where-Object Name -ne 'current')) {
    $resolvedOutput = [IO.Path]::GetFullPath($oldOutput.FullName)
    if (-not $resolvedOutput.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or ($oldOutput.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '拒绝清理越界或链接的输出目录' }
    $retiredOutput = Join-Path $retiredOutputRoot $oldOutput.Name
    if (Test-Path -LiteralPath $retiredOutput) { throw '同名旧输出已有归档，请在文件管理器处理后再更新。' }
    Move-Item -LiteralPath $resolvedOutput -Destination $retiredOutput
}
$legacyBuildRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Karolina/builds'))
if (Test-Path -LiteralPath $legacyBuildRoot) {
    $activeLegacy = @(Get-CimInstance Win32_Process -Filter "name='Karolina.Desktop.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($legacyBuildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
    if ($activeLegacy.Count -eq 0) {
        foreach ($legacyOutput in @(Get-ChildItem -LiteralPath $legacyBuildRoot -Directory)) {
            $resolvedOutput = [IO.Path]::GetFullPath($legacyOutput.FullName)
            if ($legacyOutput.Name -match '^[a-f0-9]{32}$' -and $resolvedOutput.StartsWith($legacyBuildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and -not ($legacyOutput.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                $retiredOutput = Join-Path $retiredOutputRoot $legacyOutput.Name
                if (Test-Path -LiteralPath $retiredOutput) { throw '同名旧输出已有归档，请在文件管理器处理后再更新。' }
                Move-Item -LiteralPath $resolvedOutput -Destination $retiredOutput
            }
        }
    }
}
# 这是用户要交互的桌面窗口；Hidden 会使 WinForms 的首次 Show 被 STARTUPINFO 隐藏。
$launchArguments = @()
if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) { $launchArguments = @('"' + [IO.Path]::GetFullPath($ProjectPath) + '"') }
Start-Process -FilePath (Join-Path $karolinaOutput 'Karolina.Desktop.exe') -ArgumentList $launchArguments -WindowStyle Normal

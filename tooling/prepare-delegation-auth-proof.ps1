[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$boundary = [IO.Path]::GetFullPath((Join-Path $root '.dependencies/delegation-chain'))

function Invoke-Bounded([string] $Command, [string[]] $Arguments, [int] $Seconds = 300) {
    $start = [Diagnostics.ProcessStartInfo]::new($Command)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $inherited = @{}
    foreach ($name in @('PATH', 'SystemRoot', 'TEMP', 'TMP', 'HOME', 'USERPROFILE', 'DOTNET_ROOT', 'APPDATA', 'LOCALAPPDATA', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramData')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $inherited[$name] = $value }
    }
    $start.Environment.Clear()
    foreach ($name in $inherited.Keys) { $start.Environment[$name] = $inherited[$name] }
    $start.Environment['GIT_TERMINAL_PROMPT'] = '0'
    $start.Environment['GCM_INTERACTIVE'] = 'Never'
    $start.Environment['GIT_CONFIG_COUNT'] = '0'
    $start.Environment['GIT_CONFIG_NOSYSTEM'] = '1'
    $start.Environment['GIT_CONFIG_GLOBAL'] = $(if ($IsWindows) { 'NUL' } else { '/dev/null' })
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($Seconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "$Command exceeded its bounded preparation deadline."
        }
        if ($process.ExitCode -ne 0) {
            throw "$Command preparation failed with exit $($process.ExitCode): $($output.GetAwaiter().GetResult()) $($errorOutput.GetAwaiter().GetResult())"
        }
        return $output.GetAwaiter().GetResult().Trim()
    }
    finally { $process.Dispose() }
}

function Pinned-Checkout([string] $Repository, [string] $Directory, [string] $Sha) {
    $path = [IO.Path]::GetFullPath((Join-Path $boundary $Directory))
    if (!$path.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Preparation checkout escaped its disposable workspace boundary.'
    }
    if (!(Test-Path -LiteralPath $path)) {
        $null = Invoke-Bounded git @('-c', 'credential.helper=', '-c', 'http.extraheader=', '-c', 'core.longpaths=true', 'clone', '--no-checkout', '--filter=blob:none', "https://github.com/MALIEV-Co-Ltd/$Repository.git", $path)
        $null = Invoke-Bounded git @('-C', $path, '-c', 'credential.helper=', '-c', 'http.extraheader=', 'fetch', '--depth=1', 'origin', $Sha)
        $null = Invoke-Bounded git @('-C', $path, '-c', 'core.longpaths=true', 'checkout', '--detach', $Sha)
    }
    if (!(Test-Path -LiteralPath (Join-Path $path '.git'))) { throw "Existing dependency $Directory is not a Git checkout; preserved unchanged." }
    $status = Invoke-Bounded git @('-C', $path, 'status', '--porcelain', '--untracked-files=all')
    $head = Invoke-Bounded git @('-C', $path, 'rev-parse', 'HEAD')
    $remote = Invoke-Bounded git @('-C', $path, 'remote', 'get-url', 'origin')
    if ($status -or $head -ne $Sha -or $remote -ne "https://github.com/MALIEV-Co-Ltd/$Repository.git") {
        throw "Dirty, wrong-pin or wrong-origin dependency $Directory preserved unchanged."
    }
    return $path
}

$auth = Pinned-Checkout 'Legacy.Maliev.AuthService' 'auth' '82c8d63dd08677a7f8ccd107c05dd6c9badbfd79'
$runtime = Join-Path $boundary 'auth-runtime'
$null = Pinned-Checkout 'Legacy.Maliev.ServiceDefaults' 'auth-runtime/Legacy.Maliev.ServiceDefaults' '5c5f9479313710fa576f83d3b396442997a2fcf4'
$null = Pinned-Checkout 'Legacy.Maliev.CompatibilityContracts' 'auth-runtime/Legacy.Maliev.CompatibilityContracts' '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
$buildOutput = Invoke-Bounded dotnet @('build', (Join-Path $auth 'Legacy.Maliev.AuthService.Api/Legacy.Maliev.AuthService.Api.csproj'), '-c', 'Release', '--nologo', '--no-incremental', '-warnaserror', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$runtime")
Write-Host $buildOutput
$dll = Join-Path $auth 'Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Pinned Auth API build did not produce its required proof binary.' }
Write-Host 'Exact-pinned isolated Auth API proof binary prepared; no database or service started.'

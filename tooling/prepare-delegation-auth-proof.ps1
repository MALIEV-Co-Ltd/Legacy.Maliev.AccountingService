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

$auth = Pinned-Checkout 'Legacy.Maliev.AuthService' 'auth-51afbbd' '51afbbd6e2829382a3431338abedccf339de33b1'
$runtime = Join-Path $boundary 'auth-runtime'
$null = Pinned-Checkout 'Legacy.Maliev.ServiceDefaults' 'auth-runtime/Legacy.Maliev.ServiceDefaults' '5c5f9479313710fa576f83d3b396442997a2fcf4'
$null = Pinned-Checkout 'Legacy.Maliev.CompatibilityContracts' 'auth-runtime/Legacy.Maliev.CompatibilityContracts' '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
$buildOutput = Invoke-Bounded dotnet @('build', (Join-Path $auth 'Legacy.Maliev.AuthService.Api/Legacy.Maliev.AuthService.Api.csproj'), '-c', 'Release', '--nologo', '--no-incremental', '-warnaserror', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$runtime")
Write-Host $buildOutput
$dll = Join-Path $auth 'Legacy.Maliev.AuthService.Api/bin/Release/net10.0/Legacy.Maliev.AuthService.Api.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Pinned Auth API build did not produce its required proof binary.' }
## Test-only generated utility: owner EF migrations, never copied DDL or migration history.
$seed = Join-Path $boundary 'seed-51afbbd-v5'
[IO.Directory]::CreateDirectory($seed) | Out-Null
$reference = [Security.SecurityElement]::Escape((Join-Path $auth 'Legacy.Maliev.AuthService.Infrastructure/Legacy.Maliev.AuthService.Infrastructure.csproj'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" /><ProjectReference Include="$reference" /></ItemGroup></Project>
"@
$source = @'
using Legacy.Maliev.AuthService.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// Disposable fixture only. No connection or identity values are printed, even on failure.
try
{
    string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("Missing fixture input.");
    var run = Guid.ParseExact(Required("CHAIN_RUN"), "N");
    var authority = new NpgsqlConnectionStringBuilder(Required("CHAIN_CONTAINER"));
    if (authority.Host != "127.0.0.1" && authority.Host != "localhost")
        throw new InvalidOperationException("Fixture authority must be loopback.");
    if (authority.Port <= 1024 || authority.Database != "postgres")
        throw new InvalidOperationException("Fixture authority is not a mapped disposable container.");
    var connections = new Dictionary<string, string>();
    foreach (var name in new[] { "invoice", "customer", "employee", "state" })
    {
        var connection = new NpgsqlConnectionStringBuilder(Required("CHAIN_" + name.ToUpperInvariant()));
        if (connection.Database != $"accounting_chain_{run:N}_{name}" || connection.Host != authority.Host
            || connection.Port != authority.Port || connection.Username != authority.Username
            || connection.Password != authority.Password || connection.Pooling)
            throw new InvalidOperationException("Fixture connection escaped its exact disposable authority.");
        connections.Add(name, connection.ConnectionString);
    }
    if (connections.Values.Distinct(StringComparer.Ordinal).Count() != 4)
        throw new InvalidOperationException("Fixture databases must be distinct.");
    if (Environment.GetEnvironmentVariable("CHAIN_VALIDATE_ONLY") == "1")
    {
        Console.WriteLine("Disposable fixture inputs validated before migrations.");
        return 0;
    }
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await using var customer = new CustomerIdentityDbContext(new DbContextOptionsBuilder<CustomerIdentityDbContext>().UseNpgsql(connections["customer"]).Options);
    await using var employee = new EmployeeIdentityDbContext(new DbContextOptionsBuilder<EmployeeIdentityDbContext>().UseNpgsql(connections["employee"]).Options);
    await using var sessions = new RefreshSessionDbContext(new DbContextOptionsBuilder<RefreshSessionDbContext>().UseNpgsql(connections["state"]).Options);
    await customer.Database.MigrateAsync(deadline.Token);
    await employee.Database.MigrateAsync(deadline.Token);
    await sessions.Database.MigrateAsync(deadline.Token);
    foreach (var id in new[] { "42", "99" })
    {
        var username = "chain-employee-" + id + "@example.invalid";
        var row = new LegacyIdentityRow { Id = "employee:" + id, UserName = username, NormalizedUserName = username.ToUpperInvariant(),
            EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N") };
        row.PasswordHash = new PasswordHasher<LegacyIdentityRow>().HashPassword(row, Required("CHAIN_PASSWORD"));
        employee.Users.Add(row);
    }
    await employee.SaveChangesAsync(deadline.Token);
    Console.WriteLine("Disposable owner migrations and synthetic identities prepared.");
    return 0;
}
catch
{
    Console.Error.WriteLine("Disposable identity preparation failed.");
    return 1;
}
'@
foreach ($entry in @{ 'Seed.csproj' = $project; 'Program.cs' = $source }.GetEnumerator()) {
    $target = [IO.Path]::GetFullPath((Join-Path $seed $entry.Key))
    if (!$target.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated utility escaped private boundary.' }
    if (Test-Path -LiteralPath $target) {
        if ([IO.File]::ReadAllText($target) -cne $entry.Value) { throw 'Existing different generated utility preserved unchanged.' }
    } else { [IO.File]::WriteAllText($target, $entry.Value) }
}
Write-Host (Invoke-Bounded dotnet @('build', (Join-Path $seed 'Seed.csproj'), '-c', 'Release', '--nologo', '-warnaserror', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false', '-p:UseLocalMalievDependencies=true', "-p:MalievWorkspaceRoot=$runtime"))
Write-Host 'Exact-pinned isolated Auth API proof binary prepared; no database or service started.'

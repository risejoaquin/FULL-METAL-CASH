param(
    [string]$Root = '.',
    [switch]$IncludeIgnored,
    [switch]$VerboseMatches
)

$ErrorActionPreference = 'Stop'

$resolvedRoot = (Resolve-Path $Root).Path

$patterns = @(
    @{ Name = 'PostgreSQL URL with password'; Regex = 'postgres(?:ql)?://[^\s:@/]+:[^\s:@/]+@[^\s]+' },
    @{ Name = 'Npgsql password connection string'; Regex = '(?i)(Host|Server)=.+;(Password|Pwd)=[^;\s]+' },
    @{ Name = 'Railway token'; Regex = '(?i)railway[_-]?(token|key|secret)\s*[=:]\s*[^\s]+' },
    @{ Name = 'Supabase JWT/API key'; Regex = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+' },
    @{ Name = 'AWS access key'; Regex = 'AKIA[0-9A-Z]{16}' },
    @{ Name = 'Private key block'; Regex = '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----' },
    @{ Name = 'JWT signing key literal'; Regex = '(?i)(Jwt__SigningKey|Jwt:SigningKey|SigningKey)\s*[=:]\s*[A-Za-z0-9+/=_-]{32,}' },
    @{ Name = 'Provision key literal'; Regex = '(?i)(PROVISION_KEY|Provisioning__BootstrapKey|BootstrapKey)\s*[=:]\s*[A-Za-z0-9+/=_-]{32,}' }
)

$excludedPathFragments = @(
    [IO.Path]::DirectorySeparatorChar + '.git' + [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::DirectorySeparatorChar + 'bin' + [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::DirectorySeparatorChar + 'obj' + [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::DirectorySeparatorChar + 'node_modules' + [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::DirectorySeparatorChar + 'dist' + [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::DirectorySeparatorChar + '.runtime' + [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::DirectorySeparatorChar + 'TestResults' + [IO.Path]::DirectorySeparatorChar
)

$textExtensions = @(
    '.cs', '.csproj', '.sln', '.json', '.yaml', '.yml', '.md', '.txt', '.ps1', '.sh', '.sql', '.ts', '.tsx', '.js', '.mjs', '.cjs', '.css', '.html', '.env', '.example', '.config'
)

$allowedExampleFragments = @(
    'solidpos_dev_password',
    'solidpos:solidpos_dev_password',
    'user:password',
    'USER:PASSWORD',
    '<user>:<password>',
    '<password>',
    'Password=PASSWORD',
    'Password=<password>',
    'password@host',
    'Admin123!',
    'AdminSeguro123!',
    'postgresql://user:password@host:5432/db',
    'postgresql://[A-Za-z0-9_.%-]+:[A-Za-z0-9_.%-]+@'
)

$files = Get-ChildItem -Path $resolvedRoot -Recurse -File -Force | Where-Object {
    $path = $_.FullName
    if (-not $IncludeIgnored) {
        foreach ($fragment in $excludedPathFragments) {
            if ($path.Contains($fragment)) { return $false }
        }
    }

    if ($_.Length -gt 2MB) { return $false }

    $extension = $_.Extension
    if ([string]::IsNullOrWhiteSpace($extension)) {
        return $_.Name -like '.env*' -or $_.Name -like '*Dockerfile*'
    }

    return $textExtensions -contains $extension.ToLowerInvariant()
}

$matches = New-Object System.Collections.Generic.List[object]
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($content)) { continue }

    foreach ($pattern in $patterns) {
        $regexMatches = [regex]::Matches($content, $pattern.Regex)
        foreach ($match in $regexMatches) {
            $value = $match.Value
            $isAllowedExample = $false
            foreach ($fragment in $allowedExampleFragments) {
                if ($value.Contains($fragment) -or $content.Contains($fragment)) {
                    $isAllowedExample = $true
                    break
                }
            }

            if (-not $isAllowedExample) {
                $lineNumber = ($content.Substring(0, $match.Index) -split "`n").Count
                $relativePath = [IO.Path]::GetRelativePath($resolvedRoot, $file.FullName)
                $redacted = if ($value.Length -gt 18) { $value.Substring(0, 8) + '...' + $value.Substring($value.Length - 4) } else { '[redacted]' }
                $matches.Add([pscustomobject]@{
                    File = $relativePath
                    Line = $lineNumber
                    Pattern = $pattern.Name
                    Sample = $redacted
                })
            }
        }
    }
}

if ($matches.Count -gt 0) {
    if ($VerboseMatches) {
        $matches | Format-Table -AutoSize
    } else {
        $matches | Select-Object File, Line, Pattern | Format-Table -AutoSize
    }
    throw "Potential secret material found. Review before committing. Count=$($matches.Count)"
}

Write-Host "No obvious secret patterns found. Root=$resolvedRoot"

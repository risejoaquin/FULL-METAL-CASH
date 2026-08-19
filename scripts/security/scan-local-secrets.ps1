$ErrorActionPreference = "Stop"

$patterns = @(
  'postgresql://[A-Za-z0-9_.%-]+:[A-Za-z0-9_.%-]+@',
  'Host=.*Password=[^<;\s][^;\s]+',
  'AKIA[0-9A-Z]{16}',
  '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)

$excluded = @(
  '\bin\',
  '\obj\',
  '\.git\',
  '\node_modules\'
)

$files = Get-ChildItem -Recurse -File | Where-Object {
  $path = $_.FullName
  -not ($excluded | Where-Object { $path -like "*$_*" })
}

$matches = @()
foreach ($file in $files) {
  $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
  foreach ($pattern in $patterns) {
    if ($content -match $pattern) {
      if ($content -notmatch 'solidpos_dev_password|solidpos:solidpos_dev_password|user:password|USER:PASSWORD|Password=PASSWORD|Password=<password>|password@host|<user>:<password>|Password=\.\.\.|Host=\.\*Password|Host=.*\\.\\*Password|postgresql://\[\^') {
        $matches += [pscustomobject]@{ File = $file.FullName; Pattern = $pattern }
      }
    }
  }
}

if ($matches.Count -gt 0) {
  $matches | Format-Table -AutoSize
  throw "Potential secret material found. Review before committing."
}

Write-Host "No obvious secret patterns found."

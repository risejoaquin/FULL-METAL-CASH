param(
    [ValidateSet('JwtSigningKey', 'ProvisionKey', 'Generic')]
    [string]$Kind = 'Generic',

    [ValidateRange(32, 128)]
    [int]$Bytes = 48
)

$ErrorActionPreference = 'Stop'

$buffer = New-Object byte[] $Bytes
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($buffer)
}
finally {
    if ($null -ne $rng) {
        $rng.Dispose()
    }
}

function Convert-BytesToLowerHex {
    param([byte[]]$InputBytes)

    return ([System.BitConverter]::ToString($InputBytes) -replace '-', '').ToLowerInvariant()
}

switch ($Kind) {
    'JwtSigningKey' {
        $secret = [Convert]::ToBase64String($buffer)
        Write-Host 'Generated Jwt__SigningKey candidate. Store only in Railway/Supabase secret manager. Do not commit it.'
        Write-Output $secret
    }
    'ProvisionKey' {
        $secret = Convert-BytesToLowerHex -InputBytes $buffer
        Write-Host 'Generated Provisioning__BootstrapKey candidate. Store only in Railway variables. Do not commit it.'
        Write-Output $secret
    }
    default {
        $secret = [Convert]::ToBase64String($buffer)
        Write-Host 'Generated generic SolidPOS secret. Do not commit it.'
        Write-Output $secret
    }
}

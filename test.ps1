# Requer PowerShell 7. Usa exclusivamente certificado e documentos sintéticos.
# Não consulta a SEFAZ nem procura certificados do usuário.
# O teste completo também verifica mTLS contra um servidor localhost temporário.
# A senha abaixo pertence somente ao PFX sintético descartável dos testes.
# Não use este script com um certificado real.
#Requires -Version 7.0
param()
$ErrorActionPreference='Stop'
$reaTestRoot=Join-Path ([IO.Path]::GetTempPath()) ('XML-A1-Offline-'+[guid]::NewGuid().ToString('N'))
$reaOutput=Join-Path $reaTestRoot 'build'
$reaPfx=Join-Path $reaTestRoot 'sintetico.pfx'
New-Item -ItemType Directory -Path $reaTestRoot | Out-Null
$reaRsa=$null
$reaCert=$null
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Output $reaOutput
    $reaRsa=[System.Security.Cryptography.RSA]::Create(2048)
    $reaRequest=[System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=XML A1 TESTE:11222333000181',$reaRsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    # SAN ICP-Brasil fictício: otherName 2.16.76.1.3.3 com o CNPJ de teste.
    $reaSan=[byte[]](@(0x30,27,0xa0,25,6,5,0x60,0x4c,1,3,3,0xa0,16,0x0c,14)+
        [Text.Encoding]::ASCII.GetBytes('11222333000181'))
    $reaRequest.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509Extension]::new('2.5.29.17',$reaSan,$false))
    $reaCert=$reaRequest.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-1),[DateTimeOffset]::Now.AddDays(2))
    [IO.File]::WriteAllBytes($reaPfx,$reaCert.Export(
        [System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx,'somente-teste'))
    $reaProcess=Start-Process -FilePath (Join-Path $reaOutput 'XmlA1.exe') `
        -ArgumentList @('--self-test','--test-cert',('"'+$reaPfx+'"')) `
        -WindowStyle Hidden -Wait -PassThru
    $reaReport=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'BuscaNFe-A1\testes.txt'
    Get-Content -LiteralPath $reaReport
    if($reaProcess.ExitCode -ne 0){throw 'A suíte offline falhou. Confira o relatório acima.'}
    Write-Output ('Build de teste preservado em: '+$reaOutput)
}
finally {
    if($reaCert){$reaCert.Dispose()}
    if($reaRsa){$reaRsa.Dispose()}
    if(Test-Path -LiteralPath $reaPfx){Remove-Item -LiteralPath $reaPfx}
}

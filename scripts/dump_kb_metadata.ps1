param(
    [string]$KBPath = "C:\KBs\academicoLocal",
    [string]$OutputPath = "C:\Projetos\GenexusMCP\publish\kb_dump.json"
)

$gxPath = "C:\Program Files (x86)\GeneXus\GeneXus18"
$assemblies = @(
    "Artech.Common.Helpers.dll",
    "Artech.Architecture.Common.dll",
    "Artech.Genexus.Common.dll",
    "Artech.FrameworkDE.dll"
)

foreach ($dll in $assemblies) {
    [Reflection.Assembly]::LoadFrom((Join-Path $gxPath $dll)) | Out-Null
}

try {
    Write-Host "Opening KB: $KBPath"
    $options = New-Object Artech.Architecture.Common.Objects.KnowledgeBase+OpenOptions -ArgumentList $KBPath
    $kb = [Artech.Architecture.Common.Objects.KnowledgeBase]::Open($options)
    $model = $kb.DesignModel

    Write-Host "Dumping Objects..."
    $results = @()
    $count = 0
    
    # We use GetHeaders() because it's the fastest way to get metadata without loading the object body
    foreach ($header in $model.Objects.GetHeaders()) {
        $results += @{
            Guid = $header.Guid.ToString()
            Name = $header.Name
            Type = $header.TypeDescriptor.Name
        }
        $count++
        if ($count % 500 -eq 0) { 
            Write-Host "Processed $count objects..." 
            # Clear internal SDK cache to keep memory low
            $model.ClearCache()
        }
    }

    $results | ConvertTo-Json | Out-File $OutputPath -Encoding utf8
    Write-Host "Dump complete: $count objects saved to $OutputPath"
    
    $kb.Close()
} catch {
    Write-Error "Dump failed: $($_.Exception.Message)"
}

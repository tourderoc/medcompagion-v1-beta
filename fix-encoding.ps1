# Script de correction automatique de l'encodage UTF-8
# Corrige les caractères français mal encodés dans tous les fichiers .cs

# Liste des remplacements (pattern brut -> caractère correct)
$replacements = @(
    @{Pattern='Ã©'; Replace='é'},
    @{Pattern='Ã¨'; Replace='è'},
    @{Pattern='Ã '; Replace='à'},
    @{Pattern='Ãª'; Replace='ê'},
    @{Pattern='Ã»'; Replace='û'},
    @{Pattern='Ã´'; Replace='ô'},
    @{Pattern='Ã®'; Replace='î'},
    @{Pattern='Ã¯'; Replace='ï'},
    @{Pattern='Ã§'; Replace='ç'},
    @{Pattern='Ã‰'; Replace='É'},
    @{Pattern='Ãˆ'; Replace='È'},
    @{Pattern='ÃŠ'; Replace='Ê'},
    @{Pattern='â†''; Replace='→'},
    @{Pattern='âœ"'; Replace='✓'},
    @{Pattern='âœ…'; Replace='✅'},
    @{Pattern='âŒ'; Replace='❌'},
    @{Pattern='â³'; Replace='⏳'},
    @{Pattern='â"'; Replace='❓'},
    @{Pattern='â"â"'; Replace='━━'},
    @{Pattern='â€¢'; Replace='•'}
)

# Trouver tous les fichiers .cs dans MedCompanion/
$files = Get-ChildItem -Path "MedCompanion" -Filter "*.cs" -Recurse

$totalFiles = 0
$totalReplacements = 0

Write-Host "Analyse des fichiers..." -ForegroundColor Cyan
Write-Host ""

foreach ($file in $files) {
    try {
        $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
        $originalContent = $content
        $fileReplacements = 0
        
        foreach ($item in $replacements) {
            $pattern = $item.Pattern
            $replace = $item.Replace
            
            $matches = [regex]::Matches($content, [regex]::Escape($pattern))
            if ($matches.Count -gt 0) {
                $content = $content -replace [regex]::Escape($pattern), $replace
                $fileReplacements += $matches.Count
            }
        }
        
        if ($fileReplacements -gt 0) {
            # Réécrire le fichier en UTF-8 avec BOM
            $utf8WithBom = New-Object System.Text.UTF8Encoding $true
            [System.IO.File]::WriteAllText($file.FullName, $content, $utf8WithBom)
            
            Write-Host "OK $($file.Name)" -ForegroundColor Green
            Write-Host "   => $fileReplacements remplacement(s)" -ForegroundColor Gray
            
            $totalFiles++
            $totalReplacements += $fileReplacements
        }
    }
    catch {
        Write-Host "ERREUR $($file.Name): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Correction terminee !" -ForegroundColor Green
Write-Host "   Fichiers corriges: $totalFiles" -ForegroundColor Yellow
Write-Host "   Remplacements: $totalReplacements" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

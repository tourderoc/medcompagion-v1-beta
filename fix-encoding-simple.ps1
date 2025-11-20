# Correction encodage UTF-8 - Version simple
$file = "MedCompanion\MainWindow.Patient.cs"

# Lire le fichier
$content = [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)

# Compter les caractères mal encodés
$count = ([regex]::Matches($content, "Ã[©èàêû]|â†|âœ|ðŸ")).Count
Write-Host "Caracteres mal encodes trouves: $count" -ForegroundColor Yellow

# Remplacements de base (lettres accentuées)
$content = $content.Replace([char]0xC3 + [char]0xA9, [char]0xE9)  # é
$content = $content.Replace([char]0xC3 + [char]0xA8, [char]0xE8)  # è  
$content = $content.Replace([char]0xC3 + [char]0xA0, [char]0xE0)  # à
$content = $content.Replace([char]0xC3 + [char]0xAA, [char]0xEA)  # ê
$content = $content.Replace([char]0xC3 + [char]0xBB, [char]0xFB)  # û
$content = $content.Replace([char]0xC3 + [char]0x89, [char]0xC9)  # É
$content = $content.Replace([char]0xC3 + [char]0x8A, [char]0xCA)  # Ê

# Sauvegarder avec UTF-8 BOM
$utf8 = New-Object System.Text.UTF8Encoding $true
[IO.File]::WriteAllText($file, $content, $utf8)

Write-Host "Fichier corrige et sauvegarde en UTF-8 avec BOM" -ForegroundColor Green

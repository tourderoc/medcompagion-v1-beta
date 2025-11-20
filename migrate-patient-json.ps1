# Script de migration des fichiers patient.json vers info_patient/
# Ce script déplace les fichiers patient.json de la racine des dossiers patients
# vers le nouveau dossier info_patient/

Write-Host "=== Migration patient.json vers info_patient/ ===" -ForegroundColor Cyan
Write-Host ""

# Chemin vers le dossier patients
$documentsPath = [Environment]::GetFolderPath("MyDocuments")
$patientsRoot = Join-Path $documentsPath "MedCompanion\patients"

if (-not (Test-Path $patientsRoot)) {
    Write-Host "❌ Dossier patients non trouvé : $patientsRoot" -ForegroundColor Red
    exit 1
}

Write-Host "📁 Dossier patients : $patientsRoot" -ForegroundColor Gray
Write-Host ""

# Compteurs
$migratedCount = 0
$skippedCount = 0
$errorCount = 0
$alreadyMigratedCount = 0

# Scanner tous les dossiers patients
$patientDirs = Get-ChildItem -Path $patientsRoot -Directory

Write-Host "🔍 Analyse de $($patientDirs.Count) dossiers patients..." -ForegroundColor Yellow
Write-Host ""

foreach ($patientDir in $patientDirs) {
    $patientName = $patientDir.Name
    $oldJsonPath = Join-Path $patientDir.FullName "patient.json"
    $infoPatientDir = Join-Path $patientDir.FullName "info_patient"
    $newJsonPath = Join-Path $infoPatientDir "patient.json"
    
    # Vérifier si patient.json existe à la racine
    if (Test-Path $oldJsonPath) {
        # Vérifier si déjà migré
        if (Test-Path $newJsonPath) {
            Write-Host "⚠️  $patientName : Déjà migré (fichier existe dans les deux emplacements)" -ForegroundColor Yellow
            Write-Host "    Ancien : $oldJsonPath" -ForegroundColor Gray
            Write-Host "    Nouveau : $newJsonPath" -ForegroundColor Gray
            
            # Demander confirmation pour supprimer l'ancien
            $response = Read-Host "    Supprimer l'ancien fichier à la racine ? (o/N)"
            if ($response -eq "o" -or $response -eq "O") {
                try {
                    Remove-Item $oldJsonPath -Force
                    Write-Host "    ✅ Ancien fichier supprimé" -ForegroundColor Green
                }
                catch {
                    Write-Host "    ❌ Erreur suppression : $($_.Exception.Message)" -ForegroundColor Red
                    $errorCount++
                }
            }
            else {
                Write-Host "    ⏭️  Ancien fichier conservé" -ForegroundColor Gray
            }
            
            $alreadyMigratedCount++
            Write-Host ""
            continue
        }
        
        # Migration nécessaire
        try {
            # Créer le dossier info_patient s'il n'existe pas
            if (-not (Test-Path $infoPatientDir)) {
                New-Item -Path $infoPatientDir -ItemType Directory -Force | Out-Null
            }
            
            # Déplacer le fichier
            Move-Item -Path $oldJsonPath -Destination $newJsonPath -Force
            
            Write-Host "✅ $patientName : Migré avec succès" -ForegroundColor Green
            Write-Host "   De : $oldJsonPath" -ForegroundColor Gray
            Write-Host "   Vers : $newJsonPath" -ForegroundColor Gray
            Write-Host ""
            
            $migratedCount++
        }
        catch {
            Write-Host "❌ $patientName : Erreur de migration" -ForegroundColor Red
            Write-Host "   $($_.Exception.Message)" -ForegroundColor Red
            Write-Host ""
            $errorCount++
        }
    }
    else {
        # Vérifier si déjà dans info_patient
        if (Test-Path $newJsonPath) {
            Write-Host "✓  $patientName : Déjà dans info_patient/" -ForegroundColor DarkGreen
            $skippedCount++
        }
        else {
            Write-Host "⚠️  $patientName : Aucun patient.json trouvé" -ForegroundColor Yellow
            $skippedCount++
        }
    }
}

# Résumé
Write-Host ""
Write-Host "=== Résumé de la migration ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Total dossiers analysés : $($patientDirs.Count)" -ForegroundColor White
Write-Host "✅ Migrés avec succès : $migratedCount" -ForegroundColor Green
Write-Host "✓  Déjà migrés : $alreadyMigratedCount" -ForegroundColor DarkGreen
Write-Host "⏭️  Ignorés/Sautés : $skippedCount" -ForegroundColor Gray
Write-Host "❌ Erreurs : $errorCount" -ForegroundColor Red
Write-Host ""

if ($migratedCount -gt 0) {
    Write-Host "🎉 Migration terminée ! $migratedCount patient(s) migré(s) vers info_patient/" -ForegroundColor Green
}
elseif ($alreadyMigratedCount -gt 0) {
    Write-Host "ℹ️  Tous les patients sont déjà migrés" -ForegroundColor Cyan
}
else {
    Write-Host "ℹ️  Aucune migration nécessaire" -ForegroundColor Cyan
}
Write-Host ""

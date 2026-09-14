using System;
using System.Windows;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services.Backup;

namespace MedCompanion;

/// <summary>
/// Branchement du service de sauvegarde sur le cycle de vie de la fenêtre principale.
/// Tout ce qui est ici est volontairement silencieux : la sauvegarde ne doit jamais
/// interrompre une consultation par une fenêtre qui s'ouvre d'elle-même.
/// </summary>
public partial class MainWindow : Window
{
    private BackupScheduler? _backupScheduler;

    private void DemarrerSauvegardeAutomatique()
    {
        try
        {
            _backupScheduler = new BackupScheduler();

            _backupScheduler.StatusChanged += (_, message) =>
            {
                // Simple ligne dans la barre d'état : aucune boîte de dialogue, jamais.
                StatusTextBlock.Text = $"💾 {message}";
                StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Colors.Gray);
            };

            _backupScheduler.Start();
        }
        catch (Exception ex)
        {
            // Une sauvegarde qui ne démarre pas ne doit pas empêcher de travailler.
            System.Diagnostics.Debug.WriteLine($"[Sauvegarde] Démarrage impossible : {ex.Message}");
        }
    }

    /// <summary>
    /// Dernière sauvegarde avant la fermeture, pour ne pas perdre la fin de journée entre
    /// la dernière sauvegarde automatique et le départ du cabinet. Modale et interruptible,
    /// avec un plafond de 90 secondes géré par la fenêtre elle-même.
    /// </summary>
    private void SauvegarderAvantFermeture()
    {
        try
        {
            var reglages = BackupSettings.Load();
            if (!reglages.Enabled || !reglages.RunAtClose) return;
            if (BackupService.EnCours) return;

            // Inutile de retenir l'utilisateur si une sauvegarde vient d'avoir lieu.
            if (reglages.LastRunUtc != null &&
                (DateTime.UtcNow - reglages.LastRunUtc.Value).TotalMinutes < 10) return;

            var (ok, _) = new BackupService().VerifierDestination(reglages);
            if (!ok) return;   // disque absent : on ne bloque pas la fermeture pour autant

            var fenetre = new BackupDialog(reglages, fermetureApplication: true) { Owner = this };
            fenetre.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sauvegarde] Sauvegarde de fermeture impossible : {ex.Message}");
        }
    }

    private void ArreterSauvegardeAutomatique()
    {
        try
        {
            _backupScheduler?.Dispose();
            _backupScheduler = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sauvegarde] Arrêt : {ex.Message}");
        }
    }
}

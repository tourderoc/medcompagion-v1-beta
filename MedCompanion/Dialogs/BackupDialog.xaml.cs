using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MedCompanion.Models;
using MedCompanion.Services.Backup;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Fenêtre de suivi de la sauvegarde.
    ///
    /// La copie tourne sur un thread de fond : la fenêtre ne fait que recevoir l'avancement,
    /// elle ne calcule ni ne copie rien. C'est ce qui garantit qu'elle reste réactive et
    /// que le bouton Annuler répond immédiatement.
    /// </summary>
    public partial class BackupDialog : Window
    {
        private readonly BackupSettings _reglages;
        private readonly BackupService _service = new();
        private readonly CancellationTokenSource _annulation = new();
        private readonly bool _fermetureApplication;

        private BackupResult? _resultat;
        private bool _termine;
        private bool _fermetureDemandee;
        private string? _cheminJournal;

        /// <summary>
        /// Délai au-delà duquel une sauvegarde lancée à la fermeture s'interrompt d'elle-même.
        /// Fermer MedCompanion ne doit jamais devenir une attente indéfinie parce que le disque
        /// de sauvegarde répond mal.
        /// </summary>
        private static readonly TimeSpan PlafondFermeture = TimeSpan.FromSeconds(90);

        /// <param name="fermetureApplication">
        /// Vrai lorsque la sauvegarde est déclenchée par la fermeture de MedCompanion : la fenêtre
        /// se ferme alors d'elle-même en cas de succès, et s'interrompt au bout de 90 secondes.
        /// </param>
        public BackupDialog(BackupSettings? reglages = null, bool fermetureApplication = false)
        {
            InitializeComponent();

            _reglages = reglages ?? BackupSettings.Load();
            _fermetureApplication = fermetureApplication;

            DestinationTextBlock.Text = string.IsNullOrWhiteSpace(_reglages.DestinationRoot)
                ? "Aucune destination configurée."
                : _reglages.DestinationRoot;

            if (_fermetureApplication)
            {
                Title = "💾 Sauvegarde avant fermeture";
                CompteRenduTextBlock.Text =
                    "Dernière sauvegarde avant la fermeture de MedCompanion. " +
                    "Vous pouvez l'interrompre : elle reprendra à la prochaine ouverture.";
            }

            Loaded += async (_, _) => await LancerAsync();
        }

        private async Task LancerAsync()
        {
            BtnFermer.IsEnabled = false;
            BtnAnnuler.IsEnabled = true;

            if (_fermetureApplication)
                _annulation.CancelAfter(PlafondFermeture);

            var avancement = new Progress<BackupProgress>(Afficher);

            try
            {
                _resultat = await _service.RunAsync(_reglages, avancement, _annulation.Token);
            }
            catch (OperationCanceledException)
            {
                _resultat = new BackupResult { Success = false, Cancelled = true };
            }
            catch (Exception ex)
            {
                _resultat = new BackupResult { Success = false, Error = ex.Message };
            }

            AfficherCompteRendu(_resultat);
        }

        private void Afficher(BackupProgress p)
        {
            PhaseTextBlock.Text = p.Phase switch
            {
                BackupPhase.Verification => "Vérification du disque de sauvegarde",
                BackupPhase.Analyse => "Comparaison des fichiers",
                BackupPhase.Copie => "Copie en cours",
                BackupPhase.Nettoyage => "Nettoyage des anciennes versions",
                _ => "Terminé"
            };

            if (!string.IsNullOrEmpty(p.Message) && p.Phase != BackupPhase.Copie)
                PhaseTextBlock.Text = p.Message;

            ProgressBarPrincipale.IsIndeterminate = p.Phase == BackupPhase.Analyse;
            if (!ProgressBarPrincipale.IsIndeterminate)
                ProgressBarPrincipale.Value = p.Percent;

            FichierTextBlock.Text = p.CurrentFile;

            if (p.FilesTotal > 0)
                CompteurFichiers.Text = $"{p.FilesDone}/{p.FilesTotal}";

            if (p.BytesTotal > 0)
                CompteurOctets.Text = $"{p.BytesDone / 1024d / 1024d:N0}/{p.BytesTotal / 1024d / 1024d:N0} Mo";

            CompteurEcoule.Text = Formater(p.Elapsed);
            CompteurRestant.Text = p.Eta.HasValue ? Formater(p.Eta.Value) : "—";
        }

        private static string Formater(TimeSpan t) =>
            t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s" : $"{t.Seconds}s";

        private void AfficherCompteRendu(BackupResult r)
        {
            _termine = true;
            BtnAnnuler.IsEnabled = false;
            BtnFermer.IsEnabled = true;
            ProgressBarPrincipale.IsIndeterminate = false;

            var sb = new StringBuilder();

            if (r.Cancelled)
            {
                PhaseTextBlock.Text = "Sauvegarde interrompue";
                ProgressBarPrincipale.Foreground = System.Windows.Media.Brushes.Orange;
                sb.AppendLine("La sauvegarde a été interrompue.");
                sb.AppendLine();
                sb.AppendLine("Les fichiers déjà copiés sont valides et conservés. Les autres seront repris à la prochaine sauvegarde.");
            }
            else if (!r.Success)
            {
                PhaseTextBlock.Text = "Sauvegarde impossible";
                ProgressBarPrincipale.Foreground = System.Windows.Media.Brushes.IndianRed;
                ProgressBarPrincipale.Value = 0;
                sb.AppendLine(r.Error ?? "Erreur inconnue.");
            }
            else
            {
                PhaseTextBlock.Text = "Sauvegarde terminée";
                ProgressBarPrincipale.Value = 100;

                if (r.FilesCopied == 0)
                {
                    sb.AppendLine("Aucun changement depuis la dernière sauvegarde — tout était déjà à jour.");
                }
                else
                {
                    sb.AppendLine($"{r.FilesCopied} fichier(s) sauvegardé(s), soit {r.BytesCopied / 1024d / 1024d:N1} Mo, en {r.Duration.TotalSeconds:N0} seconde(s).");
                    if (r.FilesVersioned > 0)
                        sb.AppendLine($"{r.FilesVersioned} fichier(s) modifié(s) : la version précédente a été conservée dans {BackupService.DossierVersions}, récupérable pendant {_reglages.KeepVersionsDays} jours.");
                }

                sb.AppendLine($"{r.FilesUpToDate} fichier(s) déjà à jour, non recopiés.");

                if (r.FilesInUse > 0)
                    sb.AppendLine($"{r.FilesInUse} fichier(s) étaient ouverts et seront repris à la prochaine sauvegarde.");

                if (r.FilesFailed > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"⚠ {r.FilesFailed} fichier(s) n'ont pas pu être copiés. Le détail figure dans le journal.");
                }

                _cheminJournal = r.JournalPath;
                BtnJournal.Visibility = string.IsNullOrEmpty(_cheminJournal) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (r.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Détail :");
                var plafond = Math.Min(r.Warnings.Count, 15);
                for (int i = 0; i < plafond; i++) sb.AppendLine("  • " + r.Warnings[i]);
                if (r.Warnings.Count > plafond) sb.AppendLine($"  … et {r.Warnings.Count - plafond} autre(s).");
            }

            CompteRenduTextBlock.Text = sb.ToString().TrimEnd();
            FichierTextBlock.Text = "";
            CompteurRestant.Text = "—";

            // L'utilisateur avait déjà demandé la fermeture pendant la copie : on l'exécute
            // maintenant que l'interruption est effective.
            if (_fermetureDemandee)
            {
                Close();
                return;
            }

            // Déclenchée par la fermeture de l'application : on ne retient pas l'utilisateur
            // quand tout s'est bien passé.
            if (_fermetureApplication && r.Success)
            {
                var fermeture = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                fermeture.Tick += (s, _) => { ((DispatcherTimer)s!).Stop(); Close(); };
                fermeture.Start();
            }
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            BtnAnnuler.IsEnabled = false;
            PhaseTextBlock.Text = "Interruption…";
            _annulation.Cancel();
        }

        private void BtnFermer_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnJournal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_cheminJournal) && System.IO.File.Exists(_cheminJournal))
                    Process.Start(new ProcessStartInfo(_cheminJournal) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible d'ouvrir le journal :\n{ex.Message}",
                    "Journal", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Fermer la fenêtre pendant la copie revient à annuler : on ne laisse pas une
            // sauvegarde continuer sans que rien ne l'affiche. La fenêtre se refermera seule
            // dès que l'interruption aura été prise en compte.
            if (!_termine)
            {
                _fermetureDemandee = true;
                _annulation.Cancel();
                PhaseTextBlock.Text = "Interruption…";
                e.Cancel = true;
                return;
            }

            _annulation.Dispose();
        }
    }
}

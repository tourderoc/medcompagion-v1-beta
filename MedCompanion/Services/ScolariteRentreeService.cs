using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Pose la question de rentrée au bon moment, et enregistre la réponse.
    ///
    /// Un seul point d'entrée — <see cref="DemanderSiNecessaire"/> — appelé au début de chaque
    /// séance qui suppose quelqu'un en face : cartographie de l'enfant, environnement,
    /// restitution, note de suivi. PAS à l'ouverture d'un dossier consulté pour vérifier une
    /// information : il n'y a alors personne à qui poser la question.
    ///
    /// PAS au 1er entretien non plus : c'est là que la fiche administrative est remplie, il n'y a
    /// rien à confirmer.
    ///
    /// La restitution EST dans la liste, et ce n'est pas un détail : c'est la séance où la
    /// couverture s'imprime. Sans elle, la classe de l'an dernier partirait sur le document que la
    /// famille emporte et transmet à l'école.
    /// </summary>
    public static class ScolariteRentreeService
    {
        /// <summary>
        /// Ouvre la question si la scolarité n'a pas été confirmée depuis la dernière rentrée.
        /// Renvoie true si la fiche a été mise à jour (donc si l'appelant doit se rafraîchir).
        ///
        /// Prend le RÉPERTOIRE du patient et non un service de chemins : les appelants sont des
        /// écrans de consultation, qui ont le dossier courant sous la main et pas toujours le
        /// reste. Une dépendance de moins sur un chemin qui ne fait qu'ouvrir une question.
        ///
        /// Ne lève jamais : une question de rentrée qui échoue ne doit pas empêcher une séance de
        /// commencer.
        /// </summary>
        public static bool DemanderSiNecessaire(string? patientDirectoryPath, Window? owner = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(patientDirectoryPath)) return false;

                var chemin = Path.Combine(patientDirectoryPath, "info_patient", "patient.json");
                if (!File.Exists(chemin))
                {
                    // Ancienne structure : patient.json à la racine du dossier patient.
                    chemin = Path.Combine(patientDirectoryPath, "patient.json");
                    if (!File.Exists(chemin)) return false;
                }

                var meta = Charger(chemin);
                if (meta == null) return false;

                if (!Scolarite.DoitConfirmer(meta.DateConfirmationScolarite, DateTime.Today))
                    return false;

                var dlg = new Dialogs.RentreeScolaireDialog(meta, DateTime.Today);
                if (owner != null) dlg.Owner = owner;
                dlg.ShowDialog();

                // « Plus tard » n'écrit rien : la question doit revenir. C'est ce qui distingue
                // un report d'une confirmation.
                if (dlg.Choix == Dialogs.RentreeScolaireDialog.Reponse.PlusTard) return false;

                if (dlg.Choix == Dialogs.RentreeScolaireDialog.Reponse.MiseAJour)
                {
                    meta.Ecole  = dlg.Ecole;
                    meta.Classe = dlg.Classe;
                }

                meta.DateConfirmationScolarite = DateTime.Today;
                Enregistrer(chemin, meta);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScolariteRentree] {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pose la date de confirmation sans rien demander — pour les écrans où le médecin vient
        /// de saisir lui-même l'école et la classe. Enregistrer la fiche administrative EST une
        /// confirmation : il vient de les regarder.
        /// </summary>
        public static void MarquerConfirmee(PatientMetadata meta)
            => meta.DateConfirmationScolarite = DateTime.Today;

        // Lecture/écriture directes : patient.json a deux écrivains dans l'application
        // (l'index et la fiche administrative), et passer par l'un d'eux ferait dépendre la
        // question de rentrée d'un chemin qui ne la concerne pas.

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static PatientMetadata? Charger(string chemin)
        {
            try { return JsonSerializer.Deserialize<PatientMetadata>(File.ReadAllText(chemin), _opts); }
            catch { return null; }
        }

        private static void Enregistrer(string chemin, PatientMetadata meta)
            => File.WriteAllText(chemin, JsonSerializer.Serialize(meta, _opts), System.Text.Encoding.UTF8);
    }
}

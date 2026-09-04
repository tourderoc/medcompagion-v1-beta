using System;
using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Demande de confirmation de la scolarité après une rentrée.
    ///
    /// Elle s'ouvre au DÉBUT D'UNE SÉANCE, jamais à la simple ouverture d'un dossier : la réponse
    /// suppose un parent en face. Une boîte sans interlocuteur est une boîte qu'on ferme, et une
    /// boîte qu'on ferme par habitude cesse d'être lue.
    ///
    /// TROIS RÉPONSES, DEUX COMPORTEMENTS :
    ///  • Mettre à jour   → écrit école/classe, pose la date de confirmation
    ///  • Rien n'a changé → pose la date seule. Indispensable : un enfant qui redouble garde sa
    ///                      classe, et sans cette réponse la question reviendrait toute l'année.
    ///  • Plus tard       → n'écrit rien, redemandera à la prochaine séance
    /// </summary>
    public partial class RentreeScolaireDialog : Window
    {
        /// <summary>Ce que le médecin a répondu.</summary>
        public enum Reponse { PlusTard, Inchange, MiseAJour }

        public Reponse Choix  { get; private set; } = Reponse.PlusTard;
        public string? Ecole  { get; private set; }
        public string? Classe { get; private set; }

        public RentreeScolaireDialog(PatientMetadata meta, DateTime aujourdhui)
        {
            InitializeComponent();

            var age = string.IsNullOrWhiteSpace(meta.Dob) ? "" : $", {AgeDe(meta.Dob!)} ans";
            var derniere = meta.DateConfirmationScolarite.HasValue
                ? $"Dernière confirmation : {meta.DateConfirmationScolarite.Value:dd/MM/yyyy}"
                : "Scolarité jamais confirmée dans ce dossier";

            SousTitreTb.Text = $"{meta.NomComplet}{age} — année {Scolarite.AnneeScolaireDe(aujourdhui)}\n{derniere}";

            EcoleTb.Text  = meta.Ecole  ?? "";
            ClasseTb.Text = meta.Classe ?? "";
        }

        private static int AgeDe(string dob)
        {
            if (!DateTime.TryParse(dob, out var d)) return 0;
            var age = DateTime.Today.Year - d.Year;
            if (d.Date > DateTime.Today.AddYears(-age)) age--;
            return Math.Max(age, 0);
        }

        private void MettreAJour_Click(object sender, RoutedEventArgs e)
        {
            Choix  = Reponse.MiseAJour;
            Ecole  = string.IsNullOrWhiteSpace(EcoleTb.Text)  ? null : EcoleTb.Text.Trim();
            Classe = string.IsNullOrWhiteSpace(ClasseTb.Text) ? null : ClasseTb.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void Inchange_Click(object sender, RoutedEventArgs e)
        {
            Choix = Reponse.Inchange;
            DialogResult = true;
            Close();
        }

        private void PlusTard_Click(object sender, RoutedEventArgs e)
        {
            Choix = Reponse.PlusTard;
            DialogResult = false;
            Close();
        }
    }
}

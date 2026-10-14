using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MedCompanion.Models.Carte;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Validation de ce que Med a extrait des notes pour la carte du patient
    /// (PLAN_CARTE_PATIENT.md, étape 2). Seuls les champs que la carte n'avait pas sont proposés.
    /// </summary>
    public partial class CarteCompletionDialog : Window
    {
        public class Proposition
        {
            public string Cle { get; init; } = "";
            public string Libelle { get; init; } = "";
            public string Valeur { get; set; } = "";
            public string Note { get; init; } = "";
            public string Provenance => Note.Length > 0 ? $"lu dans la note du {Note}" : "rien trouvé dans les notes";
        }

        private readonly List<Proposition> _propositions;

        /// <summary>Ce que le médecin a validé, champ par champ (vide compris). Null si annulé.</summary>
        public Dictionary<string, ChampCarte>? Valides { get; private set; }

        public CarteCompletionDialog(string patient, IEnumerable<string> cles, Dictionary<string, ChampCarte> extraits)
        {
            InitializeComponent();
            TitreTexte.Text = $"Carte de {patient} — ce que Med a trouvé";

            var voulues = cles.ToHashSet();
            _propositions = CompletionCarte.Liste
                .Where(c => voulues.Contains(c.cle))
                .Select(c => new Proposition
                {
                    Cle = c.cle,
                    Libelle = c.libelle,
                    Valeur = extraits.TryGetValue(c.cle, out var e) ? e.Valeur : "",
                    Note = extraits.TryGetValue(c.cle, out var e2) && e2.Valeur.Length > 0 ? e2.Note : ""
                })
                .ToList();
            Champs.ItemsSource = _propositions;
        }

        private void Valider_Click(object sender, RoutedEventArgs e)
        {
            Valides = _propositions.ToDictionary(
                p => p.Cle,
                p => new ChampCarte { Valeur = (p.Valeur ?? "").Trim(), Note = (p.Valeur ?? "").Trim().Length > 0 ? p.Note : "" });
            DialogResult = true;
        }

        private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}

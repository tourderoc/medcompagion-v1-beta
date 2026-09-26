using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media.Imaging;
using MedCompanion.Models.Infographies;
using MedCompanion.Services.Infographies;
using MedCompanion.ViewModels.Infographies;

namespace MedCompanion.ViewModels.Restitutions
{
    /// <summary>
    /// Annexes du dossier : les fiches de la bibliothèque d'infographies.
    ///
    /// Med en propose — une fiche est proposée quand l'un de ses sujets est cité dans le dossier
    /// rédigé — mais n'en joint aucune. Le médecin coche, et seules les fiches cochées partent
    /// en annexe. Voir PLAN_BIBLIOTHEQUE_INFOGRAPHIES.md.
    /// </summary>
    public partial class RestitutionEditorViewModel
    {
        public ObservableCollection<AnnexeInfographieViewModel> AnnexesInfographies { get; } = new();

        public string AnnexesInfographiesResume
        {
            get
            {
                var jointes = AnnexesInfographies.Count(a => a.EstJointe);
                if (AnnexesInfographies.Count == 0)
                    return "Aucune fiche validée dans la bibliothèque. Ajoutez-en depuis le Bureau → Infographies.";
                return jointes == 0
                    ? "Aucune fiche jointe à ce dossier."
                    : $"{jointes} fiche(s) jointe(s), une par page, après l'annexe contacts.";
            }
        }

        /// <summary>
        /// Construit la liste : fiches proposées d'abord, puis les autres. Une fiche cochée qui
        /// n'est plus validée reste affichée et cochée, pour que le médecin voie ce qui manque.
        /// </summary>
        private void ChargerAnnexesInfographies()
        {
            AnnexesInfographies.Clear();

            var (ok, fiches, _) = new InfographieService().ListFiches();
            if (!ok) return;

            var texte = TexteDuDossierNormalise();
            var choisies = _dossier.InfographiesIds;

            var lignes = fiches
                .Where(f => f.EstValidee || choisies.Contains(f.Id))
                .Select(f => new AnnexeInfographieViewModel(f, SujetCite(f, texte), choisies.Contains(f.Id), BasculerAnnexe))
                .OrderByDescending(a => a.EstJointe)
                .ThenByDescending(a => a.Suggeree)
                .ThenBy(a => a.Titre, StringComparer.CurrentCultureIgnoreCase);

            foreach (var ligne in lignes) AnnexesInfographies.Add(ligne);
            OnPropertyChanged(nameof(AnnexesInfographiesResume));
        }

        /// <summary>Tout le texte rédigé du dossier, sans accents ni majuscules.</summary>
        private string TexteDuDossierNormalise()
            => InfographiesViewModel.Normaliser(
                string.Join(" ", _dossier.Blocs.Select(b =>
                    string.IsNullOrWhiteSpace(b.ContenuValide) ? b.ContenuPreremplit : b.ContenuValide)));

        /// <summary>Le premier sujet de la fiche cité dans le dossier, ou null. Les sujets très
        /// courts sont ignorés : « TSA » dans un mot quelconque ferait des propositions fausses.</summary>
        private static string? SujetCite(Infographie fiche, string texteNormalise)
        {
            foreach (var sujet in fiche.Sujets)
            {
                var n = InfographiesViewModel.Normaliser(sujet);
                if (n.Length < 4) continue;
                if (texteNormalise.Contains(n)) return sujet;
            }
            return null;
        }

        private void BasculerAnnexe(AnnexeInfographieViewModel annexe)
        {
            _dossier.InfographiesIds.Remove(annexe.Fiche.Id);
            if (annexe.EstJointe) _dossier.InfographiesIds.Add(annexe.Fiche.Id);

            OnPropertyChanged(nameof(AnnexesInfographiesResume));
            PreviewRefreshRequested?.Invoke();
        }
    }

    /// <summary>Une fiche de la bibliothèque, telle que proposée dans l'éditeur du dossier.</summary>
    public class AnnexeInfographieViewModel : INotifyPropertyChanged
    {
        private readonly Action<AnnexeInfographieViewModel> _auChangement;
        private bool _estJointe;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <param name="estJointe">État de départ, posé sans prévenir l'éditeur : rien n'a changé.</param>
        public AnnexeInfographieViewModel(Infographie fiche, string? sujetCite, bool estJointe,
                                          Action<AnnexeInfographieViewModel> auChangement)
        {
            Fiche         = fiche;
            SujetCite     = sujetCite;
            _estJointe    = estJointe;
            _auChangement = auChangement;
        }

        public Infographie Fiche { get; }
        public string Titre => Fiche.Titre;
        public string? SujetCite { get; }
        public bool Suggeree => SujetCite != null;

        public BitmapImage? Miniature => InfographieImpression.Charger(Fiche.ImagePath, 200);

        public string Resume
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Fiche.Type)) parts.Add(Fiche.Type);
                if (Fiche.Sujets.Count > 0) parts.Add(string.Join(", ", Fiche.Sujets));
                if (!Fiche.EstValidee) parts.Add("⚠ n'est plus validée");
                return string.Join(" · ", parts);
            }
        }

        public string RaisonSuggestion => Suggeree ? $"Proposée : « {SujetCite} » est cité dans le dossier" : "";

        public bool EstJointe
        {
            get => _estJointe;
            set
            {
                if (_estJointe == value) return;
                _estJointe = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EstJointe)));
                _auChangement(this);
            }
        }
    }
}

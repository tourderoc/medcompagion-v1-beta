using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Synthesis
{
    /// <summary>
    /// Une section de la Synthèse Globale. Identifiée par une clé technique stable
    /// (utilisée pour le diff entre versions et pour le binding XAML) et un titre
    /// affiché. Contient le texte clinique (markdown libre) + son statut diff dans
    /// la version courante.
    ///
    /// Le contenu est éditable manuellement par le psy après proposition Med.
    /// Le statut DiffSuggere est utilisé uniquement pendant la phase de revue d'un
    /// brouillon. Pour une version VALIDÉE, toutes les sections sont en Inchangee.
    /// </summary>
    public class SyntheseSection : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>Clé technique stable (ex: "hypotheses", "enfant", "environnement"…).</summary>
        public string Key { get; }

        /// <summary>Titre affiché (ex: "Hypothèses diagnostiques retenues").</summary>
        public string Titre { get; }

        private string _contenu = "";
        /// <summary>Contenu clinique en markdown libre. Éditable.</summary>
        public string Contenu
        {
            get => _contenu;
            set { if (_contenu != value) { _contenu = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasContenu)); } }
        }
        public bool HasContenu => !string.IsNullOrWhiteSpace(_contenu);

        private SectionUpdateStatus _diffSuggere = SectionUpdateStatus.Inchangee;
        /// <summary>Statut diff dans une proposition de patch Med (utilisé pendant la revue).</summary>
        public SectionUpdateStatus DiffSuggere
        {
            get => _diffSuggere;
            set { if (_diffSuggere != value) { _diffSuggere = value; OnPropertyChanged(); } }
        }

        private string _contenuPrecedent = "";
        /// <summary>Contenu de v(n) avant la proposition de patch (pour affichage diff).</summary>
        public string ContenuPrecedent
        {
            get => _contenuPrecedent;
            set { if (_contenuPrecedent != value) { _contenuPrecedent = value ?? ""; OnPropertyChanged(); } }
        }

        private string _diffResume = "";
        /// <summary>Résumé court du changement proposé par Med (1 phrase).</summary>
        public string DiffResume
        {
            get => _diffResume;
            set { if (_diffResume != value) { _diffResume = value ?? ""; OnPropertyChanged(); } }
        }

        public SyntheseSection(string key, string titre)
        {
            Key   = key;
            Titre = titre;
        }
    }
}

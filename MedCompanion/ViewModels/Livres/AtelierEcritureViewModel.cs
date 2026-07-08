using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using MedCompanion.Models.Livres;
using MedCompanion.Services.Livres;
using MedCompanion.Services.LLM;

namespace MedCompanion.ViewModels.Livres
{
    /// <summary>
    /// ViewModel de l'Atelier d'écriture (mode Bureau) : bibliothèque de livres,
    /// édition de chapitres avec auto-sauvegarde + preview HTML live, et
    /// interaction Med (continuer / reformuler / chat) ancrée dans la mémoire du livre.
    /// </summary>
    public class AtelierEcritureViewModel : INotifyPropertyChanged
    {
        private readonly LivreService _livreService = new();
        private readonly LivreHtmlPreviewService _previewService = new();
        private LivreAssistantService? _assistant;

        // Historique de chat de la session courante (remis à zéro au changement de livre)
        private readonly List<(string role, string content)> _historique = new();

        // Auto-save + refresh preview débouncés pendant la frappe
        private readonly DispatcherTimer _editDebounce;
        private bool _isDirty;
        private bool _isLoadingChapitre; // évite de marquer dirty pendant un chargement

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? PreviewRefreshRequested;

        public AtelierEcritureViewModel()
        {
            _editDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _editDebounce.Tick += (s, e) =>
            {
                _editDebounce.Stop();
                SauvegarderChapitreCourant();
                PreviewRefreshRequested?.Invoke();
            };
        }

        public void Initialize(LLMServiceFactory llmFactory)
        {
            _assistant = new LivreAssistantService(llmFactory);
        }

        // ── Bibliothèque ────────────────────────────────────────────────────

        public ObservableCollection<Livre> Livres { get; } = new();
        public ObservableCollection<ChapitreLivre> Chapitres { get; } = new();

        private Livre? _selectedLivre;
        public Livre? SelectedLivre
        {
            get => _selectedLivre;
            set
            {
                if (ReferenceEquals(_selectedLivre, value)) return;
                SauvegarderChapitreCourant();
                _selectedLivre = value;
                _historique.Clear();
                OnPropertyChanged();
                RefreshMiseEnPageBindings();
                ChargerChapitres();
            }
        }

        private ChapitreLivre? _selectedChapitre;
        public ChapitreLivre? SelectedChapitre
        {
            get => _selectedChapitre;
            set
            {
                if (ReferenceEquals(_selectedChapitre, value)) return;
                SauvegarderChapitreCourant();
                _selectedChapitre = value;
                OnPropertyChanged();
                ChargerContenuChapitre();
            }
        }

        private string _contenuChapitre = "";
        public string ContenuChapitre
        {
            get => _contenuChapitre;
            set
            {
                if (_contenuChapitre == value) return;
                _contenuChapitre = value;
                OnPropertyChanged();
                if (!_isLoadingChapitre)
                {
                    _isDirty = true;
                    _editDebounce.Stop();
                    _editDebounce.Start();
                }
            }
        }

        public void ChargerBibliotheque()
        {
            var (success, livres, error) = _livreService.ListLivres();
            if (!success) { StatusMessage = error ?? "Erreur bibliothèque."; return; }

            Livres.Clear();
            foreach (var l in livres) Livres.Add(l);
            if (SelectedLivre == null && Livres.Count > 0)
                SelectedLivre = Livres[0];
        }

        public (bool success, string? error) CreerLivre(string titre, string auteur)
        {
            var (success, livre, error) = _livreService.CreateLivre(titre, auteur);
            if (!success || livre == null) return (false, error);

            Livres.Insert(0, livre);
            SelectedLivre = livre;
            StatusMessage = $"Livre « {livre.Titre} » créé.";
            return (true, null);
        }

        public (bool success, string? error) CreerChapitre(string titre)
        {
            if (SelectedLivre == null) return (false, "Sélectionnez un livre d'abord.");

            var (success, chapitre, error) = _livreService.AddChapitre(SelectedLivre, titre);
            if (!success || chapitre == null) return (false, error);

            Chapitres.Add(chapitre);
            SelectedChapitre = chapitre;
            StatusMessage = $"Chapitre « {chapitre.Titre} » ajouté.";
            return (true, null);
        }

        /// <summary>
        /// Importe un texte existant (docx, txt, md, pdf) comme nouveau chapitre
        /// du livre sélectionné.
        /// </summary>
        public (bool success, string? error) ImporterChapitre(string filePath, string titre)
        {
            if (SelectedLivre == null) return (false, "Sélectionnez un livre d'abord.");

            var import = new LivreImportService();
            var (okTexte, texte, errTexte) = import.ExtraireTexte(filePath);
            if (!okTexte) return (false, errTexte);

            var (okChap, err) = CreerChapitre(titre);
            if (!okChap) return (false, err);

            // CreerChapitre a sélectionné le nouveau chapitre (vide) : on y met le texte
            ContenuChapitre = texte;
            SauvegarderChapitreCourant();
            PreviewRefreshRequested?.Invoke();
            StatusMessage = $"📂 « {titre} » importé ({texte.Length:N0} caractères).";
            return (true, null);
        }

        public (bool success, string? error) SupprimerChapitre(ChapitreLivre chapitre)
        {
            if (SelectedLivre == null) return (false, "Aucun livre sélectionné.");

            var (success, error) = _livreService.DeleteChapitre(SelectedLivre, chapitre);
            if (!success) return (false, error);

            if (ReferenceEquals(SelectedChapitre, chapitre))
            {
                _selectedChapitre = null;
                _isLoadingChapitre = true;
                ContenuChapitre = "";
                _isLoadingChapitre = false;
                OnPropertyChanged(nameof(SelectedChapitre));
            }
            Chapitres.Remove(chapitre);
            PreviewRefreshRequested?.Invoke();
            return (true, null);
        }

        private void ChargerChapitres()
        {
            Chapitres.Clear();
            _selectedChapitre = null;
            OnPropertyChanged(nameof(SelectedChapitre));

            if (SelectedLivre == null)
            {
                _isLoadingChapitre = true;
                ContenuChapitre = "";
                _isLoadingChapitre = false;
                PreviewRefreshRequested?.Invoke();
                return;
            }

            foreach (var c in SelectedLivre.Chapitres.OrderBy(c => c.Ordre))
                Chapitres.Add(c);

            SelectedChapitre = Chapitres.FirstOrDefault();
            PreviewRefreshRequested?.Invoke();
        }

        private void ChargerContenuChapitre()
        {
            _isLoadingChapitre = true;
            if (SelectedLivre != null && SelectedChapitre != null)
            {
                var (_, contenu, _) = _livreService.LoadChapitre(SelectedLivre, SelectedChapitre);
                ContenuChapitre = contenu;
            }
            else
            {
                ContenuChapitre = "";
            }
            _isLoadingChapitre = false;
            _isDirty = false;
            PreviewRefreshRequested?.Invoke();
        }

        /// <summary>Sauvegarde immédiate du chapitre courant s'il a été modifié.</summary>
        public void SauvegarderChapitreCourant()
        {
            _editDebounce.Stop();
            if (!_isDirty || SelectedLivre == null || SelectedChapitre == null) return;

            var (success, error) = _livreService.SaveChapitre(SelectedLivre, SelectedChapitre, ContenuChapitre);
            _isDirty = !success;
            StatusMessage = success
                ? $"💾 Sauvegardé à {DateTime.Now:HH:mm:ss}"
                : (error ?? "Erreur de sauvegarde.");
        }

        // ── Mise en page (proxies bindables → MiseEnPageLivre) ──────────────

        private MiseEnPageLivre? Mep => SelectedLivre?.MiseEnPage;

        public string FormatPage
        {
            get => Mep?.Format ?? "A5";
            set { if (Mep != null && Mep.Format != value) { Mep.Format = value; MiseEnPageChanged(); } }
        }
        public string Police
        {
            get => Mep?.Police ?? "Georgia";
            set { if (Mep != null && Mep.Police != value) { Mep.Police = value; MiseEnPageChanged(); } }
        }
        public double TaillePt
        {
            get => Mep?.TaillePt ?? 11.5;
            set { if (Mep != null && Math.Abs(Mep.TaillePt - value) > 0.01) { Mep.TaillePt = value; MiseEnPageChanged(); } }
        }
        public double Interligne
        {
            get => Mep?.Interligne ?? 1.6;
            set { if (Mep != null && Math.Abs(Mep.Interligne - value) > 0.01) { Mep.Interligne = value; MiseEnPageChanged(); } }
        }
        public double MargeHautMm
        {
            get => Mep?.MargeHautMm ?? 20;
            set { if (Mep != null && Math.Abs(Mep.MargeHautMm - value) > 0.01) { Mep.MargeHautMm = value; MiseEnPageChanged(); } }
        }
        public double MargeBasMm
        {
            get => Mep?.MargeBasMm ?? 20;
            set { if (Mep != null && Math.Abs(Mep.MargeBasMm - value) > 0.01) { Mep.MargeBasMm = value; MiseEnPageChanged(); } }
        }
        public double MargeGaucheMm
        {
            get => Mep?.MargeGaucheMm ?? 18;
            set { if (Mep != null && Math.Abs(Mep.MargeGaucheMm - value) > 0.01) { Mep.MargeGaucheMm = value; MiseEnPageChanged(); } }
        }
        public double MargeDroiteMm
        {
            get => Mep?.MargeDroiteMm ?? 18;
            set { if (Mep != null && Math.Abs(Mep.MargeDroiteMm - value) > 0.01) { Mep.MargeDroiteMm = value; MiseEnPageChanged(); } }
        }
        public bool Justifie
        {
            get => Mep?.Justifie ?? true;
            set { if (Mep != null && Mep.Justifie != value) { Mep.Justifie = value; MiseEnPageChanged(); } }
        }
        public bool RetraitPremiereLigne
        {
            get => Mep?.RetraitPremiereLigne ?? true;
            set { if (Mep != null && Mep.RetraitPremiereLigne != value) { Mep.RetraitPremiereLigne = value; MiseEnPageChanged(); } }
        }

        private void MiseEnPageChanged()
        {
            if (SelectedLivre != null) _livreService.SaveLivre(SelectedLivre);
            PreviewRefreshRequested?.Invoke();
        }

        private void RefreshMiseEnPageBindings()
        {
            OnPropertyChanged(nameof(FormatPage));
            OnPropertyChanged(nameof(Police));
            OnPropertyChanged(nameof(TaillePt));
            OnPropertyChanged(nameof(Interligne));
            OnPropertyChanged(nameof(MargeHautMm));
            OnPropertyChanged(nameof(MargeBasMm));
            OnPropertyChanged(nameof(MargeGaucheMm));
            OnPropertyChanged(nameof(MargeDroiteMm));
            OnPropertyChanged(nameof(Justifie));
            OnPropertyChanged(nameof(RetraitPremiereLigne));
        }

        // ── Preview / export ────────────────────────────────────────────────

        /// <summary>
        /// Construit le HTML complet du livre (tous chapitres) avec le contenu
        /// en cours d'édition (non encore sauvegardé) pour le chapitre courant.
        /// </summary>
        public string BuildPreviewHtml()
        {
            if (SelectedLivre == null)
                return "<html><body style='font-family:Segoe UI;color:#95A5A6;text-align:center;padding-top:80px'>Créez ou sélectionnez un livre pour commencer.</body></html>";

            var chapitres = new List<(ChapitreLivre, string)>();
            foreach (var c in SelectedLivre.Chapitres.OrderBy(c => c.Ordre))
            {
                string contenu = ReferenceEquals(c, SelectedChapitre)
                    ? ContenuChapitre
                    : _livreService.LoadChapitre(SelectedLivre, c).contenu;
                chapitres.Add((c, contenu));
            }

            return _previewService.BuildPreviewHtml(SelectedLivre, chapitres);
        }

        // ── Med : chat / continuer / reformuler / mémoire ───────────────────

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
        }

        public bool IsNotBusy => !_isBusy;

        private string _chatInput = "";
        public string ChatInput
        {
            get => _chatInput;
            set { _chatInput = value; OnPropertyChanged(); }
        }

        private string _medReponse = "";
        public string MedReponse
        {
            get => _medReponse;
            set { _medReponse = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public async Task EnvoyerChatAsync()
        {
            if (_assistant == null || SelectedLivre == null || string.IsNullOrWhiteSpace(ChatInput) || IsBusy) return;

            var message = ChatInput.Trim();
            ChatInput = "";
            IsBusy = true;
            StatusMessage = "Med réfléchit...";

            try
            {
                var memoire = _livreService.LoadMemoire(SelectedLivre);
                var (success, result, error) = await _assistant.ChatAsync(
                    SelectedLivre, memoire,
                    SelectedChapitre?.Titre ?? "",
                    ContenuChapitre,
                    _historique, message);

                if (success)
                {
                    MedReponse = result;
                    _historique.Add(("user", message));
                    _historique.Add(("assistant", result));
                    // Limiter l'historique aux 10 derniers échanges pour contenir le contexte
                    while (_historique.Count > 20) _historique.RemoveAt(0);
                    StatusMessage = "";
                }
                else
                {
                    StatusMessage = error ?? "Erreur Med.";
                }
            }
            finally { IsBusy = false; }
        }

        /// <summary>Med continue le chapitre ; le texte saisi dans le chat sert de consigne optionnelle.</summary>
        public async Task ContinuerAsync()
        {
            if (_assistant == null || SelectedLivre == null || SelectedChapitre == null || IsBusy) return;

            var consigne = string.IsNullOrWhiteSpace(ChatInput) ? null : ChatInput.Trim();
            ChatInput = "";
            IsBusy = true;
            StatusMessage = "Med écrit la suite...";

            try
            {
                var memoire = _livreService.LoadMemoire(SelectedLivre);
                var (success, result, error) = await _assistant.ContinuerAsync(
                    SelectedLivre, memoire, SelectedChapitre.Titre, ContenuChapitre, consigne);

                if (success) { MedReponse = result; StatusMessage = "Proposition prête — « Insérer » pour l'ajouter au chapitre."; }
                else StatusMessage = error ?? "Erreur Med.";
            }
            finally { IsBusy = false; }
        }

        /// <summary>Med reformule le passage sélectionné ; le chat sert de consigne optionnelle.</summary>
        public async Task ReformulerAsync(string passage)
        {
            if (_assistant == null || SelectedLivre == null || IsBusy) return;
            if (string.IsNullOrWhiteSpace(passage))
            {
                StatusMessage = "Sélectionnez d'abord un passage dans l'éditeur.";
                return;
            }

            var consigne = string.IsNullOrWhiteSpace(ChatInput) ? null : ChatInput.Trim();
            ChatInput = "";
            IsBusy = true;
            StatusMessage = "Med reformule...";

            try
            {
                var memoire = _livreService.LoadMemoire(SelectedLivre);
                var (success, result, error) = await _assistant.ReformulerAsync(
                    SelectedLivre, memoire, SelectedChapitre?.Titre ?? "", ContenuChapitre, passage, consigne);

                if (success) { MedReponse = result; StatusMessage = "Reformulation prête — « Insérer » remplace la sélection."; }
                else StatusMessage = error ?? "Erreur Med.";
            }
            finally { IsBusy = false; }
        }

        public async Task MettreAJourMemoireAsync()
        {
            if (_assistant == null || SelectedLivre == null || IsBusy) return;

            SauvegarderChapitreCourant();
            IsBusy = true;
            StatusMessage = "Med relit tout le livre et met à jour sa mémoire...";

            try
            {
                var chapitres = SelectedLivre.Chapitres.OrderBy(c => c.Ordre)
                    .Select(c => (c, ReferenceEquals(c, SelectedChapitre)
                        ? ContenuChapitre
                        : _livreService.LoadChapitre(SelectedLivre, c).contenu))
                    .ToList();

                var (success, memoire, error) = await _assistant.GenererMemoireAsync(SelectedLivre, chapitres);
                if (success)
                {
                    _livreService.SaveMemoire(SelectedLivre, memoire);
                    MedReponse = memoire;
                    StatusMessage = "🧠 Mémoire du livre mise à jour.";
                }
                else StatusMessage = error ?? "Erreur génération mémoire.";
            }
            finally { IsBusy = false; }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

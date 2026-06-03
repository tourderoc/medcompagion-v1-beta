using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Synthesis;
using MedCompanion.Services.Synthesis;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel d'édition d'une Synthèse Globale (V0.1 — édition manuelle).
    /// Gère l'auto-save du brouillon avec debounce 500 ms, la validation et la
    /// fermeture sans validation.
    ///
    /// Une seule instance partagée à la fois dans ConsultationModeViewModel —
    /// nettoyée à la fermeture du panneau.
    /// </summary>
    public class SyntheseGlobaleViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private readonly SyntheseGlobaleService _service;
        private readonly SyntheseGlobaleSuggesterService? _suggester;
        private SyntheseGlobale? _synthese;
        private CancellationTokenSource? _autoSaveCts;
        private string _patientDirectoryPath = "";

        public SyntheseGlobaleViewModel(SyntheseGlobaleService service, SyntheseGlobaleSuggesterService? suggester = null)
        {
            _service   = service;
            _suggester = suggester;

            ValiderCommand   = new RelayCommand(_ => Valider(),              _ => CanValider);
            FermerCommand    = new RelayCommand(_ => FermerSansValider(),    _ => Synthese != null);
            ProposerCommand  = new RelayCommand(async _ => await ProposerAsync(),
                                                _ => CanProposer);
            PatchCommand     = new RelayCommand(async _ => await PatchAsync(),
                                                _ => CanPatch);
            AccepterSectionCommand = new RelayCommand(param => AccepterSection(param as SyntheseSection),
                                                     param => SectionAUneProposition(param as SyntheseSection));
            RejeterSectionCommand  = new RelayCommand(param => RejeterSection(param as SyntheseSection),
                                                     param => SectionAUneProposition(param as SyntheseSection));
        }

        public ICommand ProposerCommand        { get; }
        public ICommand PatchCommand           { get; }
        public ICommand AccepterSectionCommand { get; }
        public ICommand RejeterSectionCommand  { get; }

        private bool CanPatch
            => _suggester != null
            && Synthese != null
            && Synthese.IsBrouillon
            && Synthese.Version > 1                        // patch ne fait sens qu'à partir de v2
            && !IsProposerEnCours;

        public bool PatchVisible => _suggester != null;

        /// <summary>True si version > 1 (donc le bouton Patch est plus pertinent que Proposer initial).</summary>
        public bool IsModePatch => Synthese != null && Synthese.Version > 1;

        private bool _isProposerEnCours;
        public bool IsProposerEnCours
        {
            get => _isProposerEnCours;
            private set { if (_isProposerEnCours != value) { _isProposerEnCours = value; OnPropertyChanged();
                (ProposerCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        }

        private bool CanProposer
            => _suggester != null
            && Synthese != null
            && Synthese.IsBrouillon
            && !IsProposerEnCours;

        public bool ProposerVisible => _suggester != null;

        /// <summary>Synthèse en cours d'édition (brouillon).</summary>
        public SyntheseGlobale? Synthese
        {
            get => _synthese;
            set
            {
                if (_synthese != value)
                {
                    DetachSectionHandlers();
                    _synthese = value;
                    AttachSectionHandlers();
                    OnPropertyChanged();
                    NotifyAll();
                }
            }
        }

        /// <summary>Notifié à la fermeture du panneau (validation OU annulation).</summary>
        public event Action? Closed;

        /// <summary>Notifié à la création d'un nouveau brouillon (pour rafraîchir la frise).</summary>
        public event Action? BrouillonCreated;

        /// <summary>
        /// Notifié à la validation d'une nouvelle version (pour reset du tracker incrémental).
        /// Paramètre : NomComplet du patient.
        /// </summary>
        public event Action<string>? SyntheseValidated;

        // ── Affichage ────────────────────────────────────────────────────────

        public string TitreVue
            => Synthese == null
                ? ""
                : Synthese.IsValidee
                    ? $"🧭 Synthèse Globale v{Synthese.Version} — validée {Synthese.DateValidation:dd/MM/yyyy}"
                    : $"🧭 Synthèse Globale v{Synthese.Version} — brouillon";

        public string StatutLabel
            => Synthese == null ? ""
             : Synthese.IsValidee ? "Validée (lecture seule)"
             : "Brouillon (édition libre, auto-save)";

        public bool IsReadOnly => Synthese?.IsValidee ?? false;
        public bool IsBrouillon => Synthese?.IsBrouillon ?? false;

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value ?? ""; OnPropertyChanged(); } }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ValiderCommand { get; }
        public ICommand FermerCommand  { get; }

        private bool CanValider
            => Synthese != null && Synthese.IsBrouillon && Synthese.HasAnyContenu;

        // ── Cycle de vie ─────────────────────────────────────────────────────

        /// <summary>
        /// Ouvre une synthèse pour un patient : reprend le brouillon courant s'il existe,
        /// sinon crée un nouveau brouillon vide v(N+1) où N = dernière version validée.
        /// </summary>
        public void OuvrirBrouillonOuCreer(string patientNomComplet, string psychiatre, string patientDirectoryPath = "")
        {
            _patientDirectoryPath = patientDirectoryPath ?? "";
            if (string.IsNullOrWhiteSpace(patientNomComplet)) return;

            var brouillonMeta = _service.GetBrouillon(patientNomComplet);
            if (brouillonMeta != null)
            {
                var loaded = _service.Load(brouillonMeta.FilePath);
                if (loaded != null)
                {
                    Synthese = loaded;
                    StatusMessage = $"Brouillon v{loaded.Version} repris.";
                    return;
                }
            }

            // Nouveau brouillon : v(N+1) où N = dernière validée
            var derniere = _service.GetDerniereValidee(patientNomComplet);
            var nouvelleVersion = (derniere?.Version ?? 0) + 1;

            var nouveau = new SyntheseGlobale
            {
                Version           = nouvelleVersion,
                PatientNomComplet = patientNomComplet,
                Psychiatre        = psychiatre,
                Statut            = SyntheseStatut.Brouillon,
                DateRedaction     = DateTime.Now,
                VersionPrecedenteFichier = derniere?.FileName
            };

            // V0.4 — Si une version validée existe, hériter de son contenu pour permettre
            // un mode patch (Med modifie seulement ce qui change). On garde aussi le
            // ContenuPrecedent par section pour pouvoir afficher le diff.
            if (derniere != null)
            {
                var dernFull = _service.Load(derniere.FilePath);
                if (dernFull != null)
                {
                    foreach (var section in nouveau.Sections)
                    {
                        var src = dernFull.GetSection(section.Key);
                        if (src != null)
                        {
                            section.Contenu          = src.Contenu;
                            section.ContenuPrecedent = src.Contenu;
                            section.DiffSuggere      = SectionUpdateStatus.Inchangee;
                        }
                    }
                    nouveau.IncrementsDepuisRevisionMajeure = dernFull.IncrementsDepuisRevisionMajeure + 1;
                }
            }

            _service.SaveBrouillon(nouveau);
            Synthese = nouveau;
            StatusMessage = derniere != null
                ? $"Nouveau brouillon v{nouvelleVersion} créé — hérite du contenu de v{derniere.Version}."
                : $"Nouveau brouillon v{nouvelleVersion} créé.";
            BrouillonCreated?.Invoke();
        }

        /// <summary>
        /// Demande à Med de proposer une v1 à partir du dossier complet (V0.2 — génération
        /// initiale). REMPLACE le contenu du brouillon. Le psy peut ensuite éditer librement.
        /// </summary>
        private async Task ProposerAsync()
        {
            if (_suggester == null || Synthese == null || Synthese.IsValidee) return;

            // Confirmation si du contenu existe déjà
            if (Synthese.HasAnyContenu)
            {
                var r = System.Windows.MessageBox.Show(
                    "Le brouillon contient déjà du contenu. La proposition de Med va le REMPLACER.\n\nContinuer ?",
                    "Proposer (Med)",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (r != System.Windows.MessageBoxResult.Yes) return;
            }

            IsProposerEnCours = true;
            StatusMessage = "⏳ Med analyse le dossier complet...";

            try
            {
                var (ok, sug, error) = await _suggester.GenerateInitialAsync(
                    Synthese.PatientNomComplet, _patientDirectoryPath, CancellationToken.None);
                if (!ok || sug == null)
                {
                    StatusMessage = $"❌ Proposition impossible : {error ?? "réponse vide"}";
                    return;
                }

                Synthese.Hypotheses.Contenu    = sug.Hypotheses;
                Synthese.Enfant.Contenu        = sug.Enfant;
                Synthese.Environnement.Contenu = sug.Environnement;
                Synthese.Articulation.Contenu  = sug.Articulation;
                Synthese.Conclusion.Contenu    = sug.Conclusion;
                // Évolution reste vide en v1 (se remplit à partir de v2 en mode patch)

                Synthese.SourcesEvaluations    = sug.EvaluationsSources;
                Synthese.SourcesNombreNotes    = sug.NotesUtilisees;

                _service.SaveBrouillon(Synthese);
                StatusMessage = $"✓ Proposition Med insérée — {sug.EvaluationsSources.Count} évaluation(s) consultée(s). Éditez librement.";
                (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsProposerEnCours = false;
            }
        }

        /// <summary>
        /// V0.4 — Demande à Med un PATCH (diff par section) plutôt qu'une refonte complète.
        /// Pour chaque section, statut Inchangée/Modifiée/Nouvelle/Supprimée + nouveau contenu.
        /// Le contenu de v(N) reste dans ContenuPrecedent pour affichage diff.
        /// </summary>
        private async Task PatchAsync()
        {
            if (_suggester == null || Synthese == null || Synthese.IsValidee) return;

            IsProposerEnCours = true;
            StatusMessage = "⏳ Med relit la synthèse et les nouveaux éléments...";

            try
            {
                // Sauvegarder le contenu actuel comme "précédent" pour le diff visuel
                foreach (var s in Synthese.Sections)
                    s.ContenuPrecedent = s.Contenu;

                var (ok, patch, error) = await _suggester.SuggestPatchAsync(
                    Synthese, _patientDirectoryPath, CancellationToken.None);
                if (!ok || patch == null)
                {
                    StatusMessage = $"❌ Patch impossible : {error ?? "réponse vide"}";
                    return;
                }

                int modifs = 0, nouveau = 0, inchangees = 0;
                foreach (var sp in patch.Sections)
                {
                    var target = Synthese.GetSection(sp.Cle);
                    if (target == null) continue;

                    var statut = MapStatut(sp.Statut);
                    target.DiffSuggere = statut;
                    target.DiffResume  = sp.DiffResume ?? "";

                    if (statut == SectionUpdateStatus.Modifiee || statut == SectionUpdateStatus.Nouvelle)
                    {
                        if (!string.IsNullOrWhiteSpace(sp.Contenu))
                            target.Contenu = sp.Contenu.Trim();
                        if (statut == SectionUpdateStatus.Modifiee) modifs++;
                        else nouveau++;
                    }
                    else if (statut == SectionUpdateStatus.Supprimee)
                    {
                        target.Contenu = "";
                    }
                    else
                    {
                        inchangees++;
                    }
                }

                _service.SaveBrouillon(Synthese);
                StatusMessage = $"✓ Patch Med proposé — {modifs} modif(s), {nouveau} nouvelle(s), {inchangees} inchangée(s). Acceptez/rejetez section par section.";
                (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsProposerEnCours = false;
            }
        }

        private static SectionUpdateStatus MapStatut(string s) => s switch
        {
            "modifiee"  => SectionUpdateStatus.Modifiee,
            "nouvelle"  => SectionUpdateStatus.Nouvelle,
            "supprimee" => SectionUpdateStatus.Supprimee,
            _           => SectionUpdateStatus.Inchangee
        };

        // ── Accepter / Rejeter une section patchée ──────────────────────────

        private static bool SectionAUneProposition(SyntheseSection? s)
            => s != null && s.DiffSuggere != SectionUpdateStatus.Inchangee;

        private void AccepterSection(SyntheseSection? s)
        {
            if (s == null) return;
            // Accepter = on garde le nouveau contenu, on remet le statut à Inchangée
            s.DiffSuggere      = SectionUpdateStatus.Inchangee;
            s.ContenuPrecedent = s.Contenu;
            s.DiffResume       = "";
            if (Synthese != null && Synthese.IsBrouillon) _service.SaveBrouillon(Synthese);
        }

        private void RejeterSection(SyntheseSection? s)
        {
            if (s == null) return;
            // Rejeter = on revient au contenu précédent
            s.Contenu          = s.ContenuPrecedent;
            s.DiffSuggere      = SectionUpdateStatus.Inchangee;
            s.DiffResume       = "";
            if (Synthese != null && Synthese.IsBrouillon) _service.SaveBrouillon(Synthese);
        }

        private void Valider()
        {
            if (Synthese == null) return;
            try
            {
                var patientNom = Synthese.PatientNomComplet;
                _service.Validate(Synthese);
                StatusMessage = $"✓ Synthèse v{Synthese.Version} validée — nouvelle source de vérité.";
                NotifyAll();
                SyntheseValidated?.Invoke(patientNom);   // déclenche reset du tracker
                Closed?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur validation : {ex.Message}";
            }
        }

        private void FermerSansValider()
        {
            DetachSectionHandlers();
            Synthese = null;
            Closed?.Invoke();
        }

        // ── Auto-save debounced ──────────────────────────────────────────────

        private void AttachSectionHandlers()
        {
            if (Synthese == null) return;
            foreach (var s in Synthese.Sections)
                s.PropertyChanged += OnSectionChanged;
        }

        private void DetachSectionHandlers()
        {
            if (Synthese == null) return;
            foreach (var s in Synthese.Sections)
                s.PropertyChanged -= OnSectionChanged;
        }

        private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SyntheseSection.Contenu)) return;
            if (Synthese == null || Synthese.IsValidee) return;
            ScheduleAutoSave();
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ScheduleAutoSave()
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts = new CancellationTokenSource();
            var ct = _autoSaveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, ct);
                    if (ct.IsCancellationRequested || Synthese == null || Synthese.IsValidee) return;
                    _service.SaveBrouillon(Synthese);
                }
                catch { /* meilleur effort */ }
            });
        }

        private void NotifyAll()
        {
            OnPropertyChanged(nameof(Synthese));
            OnPropertyChanged(nameof(TitreVue));
            OnPropertyChanged(nameof(StatutLabel));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(IsBrouillon));
            OnPropertyChanged(nameof(IsModePatch));
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FermerCommand  as RelayCommand)?.RaiseCanExecuteChanged();
            (PatchCommand    as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}

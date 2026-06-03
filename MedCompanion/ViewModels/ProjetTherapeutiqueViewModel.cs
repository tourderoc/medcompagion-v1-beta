using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Therapeutique;
using MedCompanion.Services.Therapeutique;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// V1.0 — ViewModel d'édition manuelle d'un Projet Thérapeutique.
    ///
    /// Architecture parallèle à SyntheseGlobaleViewModel :
    /// - Auto-save debounce 500 ms
    /// - Commandes Valider / Fermer
    /// - Commandes ajout/suppression d'actions structurées par section
    /// - Commande de cycle de statut sur une action (clic = ⚪→🟡→✅→⛔→⚪)
    /// </summary>
    public class ProjetTherapeutiqueViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private readonly ProjetTherapeutiqueService _service;
        private readonly ProjetTherapeutiqueSuggesterService? _suggester;
        private ProjetTherapeutique? _projet;
        private CancellationTokenSource? _autoSaveCts;
        private string _patientDirectoryPath = "";

        public ProjetTherapeutiqueViewModel(ProjetTherapeutiqueService service,
                                            ProjetTherapeutiqueSuggesterService? suggester = null)
        {
            _service   = service;
            _suggester = suggester;

            ValiderCommand    = new RelayCommand(_ => Valider(),                _ => CanValider);
            FermerCommand     = new RelayCommand(_ => FermerSansValider(),      _ => Projet != null);
            ProposerCommand   = new RelayCommand(async _ => await ProposerAsync(), _ => CanProposer);
            PatchCommand      = new RelayCommand(async _ => await PatchAsync(),    _ => CanPatch);
            AccepterActionCommand = new RelayCommand(param => AccepterAction(param as ProjetAction),
                                                      param => param is ProjetAction a && a.HasDiff);
            RejeterActionCommand  = new RelayCommand(param => RejeterAction(param as ProjetAction),
                                                      param => param is ProjetAction a && a.HasDiff);

            AjouterActionMedicaleCommand         = new RelayCommand(_ => AjouterAction("medicale"),         _ => Projet?.IsBrouillon == true);
            AjouterActionPsychologiqueCommand    = new RelayCommand(_ => AjouterAction("psychologique"),    _ => Projet?.IsBrouillon == true);
            AjouterActionDeveloppementaleCommand = new RelayCommand(_ => AjouterAction("developpementale"), _ => Projet?.IsBrouillon == true);
            AjouterActionEnvironnementaleCommand = new RelayCommand(_ => AjouterAction("environnementale"), _ => Projet?.IsBrouillon == true);

            SupprimerActionCommand = new RelayCommand(param => SupprimerAction(param as ProjetAction),
                                                      param => param is ProjetAction && Projet?.IsBrouillon == true);

            CyclerStatutCommand = new RelayCommand(param => CyclerStatut(param as ProjetAction),
                                                   param => param is ProjetAction && Projet?.IsBrouillon == true);
        }

        public ProjetTherapeutique? Projet
        {
            get => _projet;
            set
            {
                if (_projet != value)
                {
                    DetachHandlers();
                    _projet = value;
                    AttachHandlers();
                    OnPropertyChanged();
                    NotifyAll();
                }
            }
        }

        public event Action? Closed;
        public event Action? BrouillonCreated;
        public event Action<string>? ProjetValidated;

        // ── Affichage ────────────────────────────────────────────────────────

        public string TitreVue
            => Projet == null ? ""
             : Projet.IsValidee ? $"🎯 Projet Thérapeutique v{Projet.Version} — validé {Projet.DateValidation:dd/MM/yyyy}"
             : $"🎯 Projet Thérapeutique v{Projet.Version} — brouillon";

        public string StatutLabel
            => Projet == null ? ""
             : Projet.IsValidee ? "Validé (lecture seule)"
             : "Brouillon (édition libre, auto-save)";

        public bool IsReadOnly  => Projet?.IsValidee ?? false;
        public bool IsBrouillon => Projet?.IsBrouillon ?? false;

        public bool IsReevaluationPassee => Projet?.IsReevaluationPassee ?? false;

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value ?? ""; OnPropertyChanged(); } }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ValiderCommand { get; }
        public ICommand FermerCommand  { get; }

        public ICommand AjouterActionMedicaleCommand         { get; }
        public ICommand AjouterActionPsychologiqueCommand    { get; }
        public ICommand AjouterActionDeveloppementaleCommand { get; }
        public ICommand AjouterActionEnvironnementaleCommand { get; }

        public ICommand SupprimerActionCommand { get; }
        public ICommand CyclerStatutCommand    { get; }
        public ICommand ProposerCommand        { get; }
        public ICommand PatchCommand           { get; }
        public ICommand AccepterActionCommand  { get; }
        public ICommand RejeterActionCommand   { get; }

        private bool CanPatch
            => _suggester != null
            && Projet != null
            && Projet.IsBrouillon
            && Projet.Version > 1
            && !IsProposerEnCours;

        /// <summary>True quand v > 1 (mode patch incrémental).</summary>
        public bool IsModePatch => Projet != null && Projet.Version > 1;

        private bool _isProposerEnCours;
        public bool IsProposerEnCours
        {
            get => _isProposerEnCours;
            private set { if (_isProposerEnCours != value) { _isProposerEnCours = value; OnPropertyChanged();
                (ProposerCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        }

        public bool ProposerVisible => _suggester != null;

        private bool CanProposer
            => _suggester != null
            && Projet != null
            && Projet.IsBrouillon
            && !IsProposerEnCours;

        private bool CanValider
            => Projet != null
            && Projet.IsBrouillon
            && Projet.HasAnyContenu;

        // ── Cycle de vie ─────────────────────────────────────────────────────

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
                    Projet = loaded;
                    StatusMessage = $"Brouillon v{loaded.Version} repris.";
                    return;
                }
            }

            var derniere = _service.GetDerniereValidee(patientNomComplet);
            var nouvelleVersion = (derniere?.Version ?? 0) + 1;

            var nouveau = new ProjetTherapeutique
            {
                Version           = nouvelleVersion,
                PatientNomComplet = patientNomComplet,
                Psychiatre        = psychiatre,
                Statut            = ProjetStatut.Brouillon,
                DateRedaction     = DateTime.Now,
                VersionPrecedenteFichier = derniere?.FileName
            };

            // V1.2 — Si une version validée existe, hériter de son contenu pour permettre
            // le mode patch (Med modifie seulement ce qui change). On garde aussi le
            // LibellePrecedent / contenu d'origine par action pour préparer le diff.
            if (derniere != null)
            {
                var dernFull = _service.Load(derniere.FilePath);
                if (dernFull != null)
                {
                    // Sections texte libre
                    nouveau.ObjectifsPrioritaires = dernFull.ObjectifsPrioritaires;
                    nouveau.RessourcesASoutenir   = dernFull.RessourcesASoutenir;
                    nouveau.ReevaluationChecklist = dernFull.ReevaluationChecklist;
                    nouveau.CoConstructionFamille = dernFull.CoConstructionFamille;
                    nouveau.DateReevaluationPrevue = dernFull.DateReevaluationPrevue;
                    nouveau.SyntheseGlobaleSourceFichier = dernFull.SyntheseGlobaleSourceFichier;

                    // Actions — hériter avec leur statut courant
                    foreach (var a in dernFull.ActionsMedicales)         nouveau.ActionsMedicales.Add(CloneAction(a));
                    foreach (var a in dernFull.ActionsPsychologiques)    nouveau.ActionsPsychologiques.Add(CloneAction(a));
                    foreach (var a in dernFull.ActionsDeveloppementales) nouveau.ActionsDeveloppementales.Add(CloneAction(a));
                    foreach (var a in dernFull.ActionsEnvironnementales) nouveau.ActionsEnvironnementales.Add(CloneAction(a));
                }
            }

            _service.SaveBrouillon(nouveau);
            Projet = nouveau;
            StatusMessage = derniere != null
                ? $"Nouveau brouillon v{nouvelleVersion} créé — hérite du contenu de v{derniere.Version}."
                : $"Nouveau brouillon v{nouvelleVersion} créé.";
            BrouillonCreated?.Invoke();
        }

        /// <summary>
        /// Clone une action en conservant l'ID (pour le matching inter-versions) et son
        /// statut courant. Les dates restent immuables (la décision n'est pas re-prise).
        /// </summary>
        private static ProjetAction CloneAction(ProjetAction src)
            => new ProjetAction
            {
                Id                     = src.Id,
                Libelle                = src.Libelle,
                Description            = src.Description,
                Statut                 = src.Statut,
                DateDecision           = src.DateDecision,
                DateDernierStatut      = src.DateDernierStatut,
                MotifDernierChangement = src.MotifDernierChangement,
                LienSyntheseSection    = src.LienSyntheseSection,
                IndicateurReussite     = src.IndicateurReussite,
            };

        /// <summary>
        /// V1.1 — Demande à Med de proposer le contenu du projet à partir de la dernière
        /// Synthèse Globale validée + évaluations + notes. REMPLACE le contenu actuel.
        /// </summary>
        private async Task ProposerAsync()
        {
            if (_suggester == null || Projet == null || Projet.IsValidee) return;

            if (Projet.HasAnyContenu)
            {
                var r = System.Windows.MessageBox.Show(
                    "Le brouillon contient déjà du contenu. La proposition de Med va le REMPLACER.\n\nContinuer ?",
                    "Proposer (Med)",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (r != System.Windows.MessageBoxResult.Yes) return;
            }

            IsProposerEnCours = true;
            StatusMessage = "⏳ Med lit la Synthèse Globale et le dossier pour proposer un projet...";

            try
            {
                var (ok, sug, error) = await _suggester.GenerateInitialAsync(
                    Projet.PatientNomComplet, _patientDirectoryPath, CancellationToken.None);
                if (!ok || sug == null)
                {
                    StatusMessage = $"❌ Proposition impossible : {error ?? "réponse vide"}";
                    return;
                }

                // Textes
                Projet.ObjectifsPrioritaires = sug.ObjectifsPrioritaires;
                Projet.RessourcesASoutenir   = sug.RessourcesASoutenir;
                Projet.ReevaluationChecklist = sug.ReevaluationChecklist;
                Projet.CoConstructionFamille = sug.CoConstructionFamille;

                // Listes d'actions : on REMPLACE les collections
                ReplaceActions(Projet.ActionsMedicales,         sug.ActionsMedicales);
                ReplaceActions(Projet.ActionsPsychologiques,    sug.ActionsPsychologiques);
                ReplaceActions(Projet.ActionsDeveloppementales, sug.ActionsDeveloppementales);
                ReplaceActions(Projet.ActionsEnvironnementales, sug.ActionsEnvironnementales);

                // Lien Synthèse source (traçabilité)
                if (!string.IsNullOrWhiteSpace(sug.SyntheseGlobaleSourceFichier))
                    Projet.SyntheseGlobaleSourceFichier = sug.SyntheseGlobaleSourceFichier;

                _service.SaveBrouillon(Projet);
                StatusMessage = sug.SyntheseGlobaleSourceVersion > 0
                    ? $"✓ Projet proposé à partir de la Synthèse Globale v{sug.SyntheseGlobaleSourceVersion}. Validez/modifiez chaque action."
                    : "✓ Projet proposé (sans synthèse de référence). Validez/modifiez chaque action.";
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

        private void ReplaceActions(ObservableCollection<ProjetAction> target,
                                    System.Collections.Generic.List<ProjetTherapeutiqueSuggesterService.ActionSuggestion> source)
        {
            // Détacher les handlers actuels
            foreach (var a in target) DetachActionHandlers(a);
            target.Clear();
            foreach (var s in source)
            {
                if (string.IsNullOrWhiteSpace(s.Libelle)) continue;
                var a = new ProjetAction
                {
                    Libelle             = s.Libelle.Trim(),
                    Description         = (s.Description ?? "").Trim(),
                    IndicateurReussite  = (s.IndicateurReussite ?? "").Trim(),
                    LienSyntheseSection = (s.LienSyntheseSection ?? "").Trim(),
                    Statut              = ActionStatut.AVenir,
                    DateDecision        = DateTime.Now,
                    DateDernierStatut   = DateTime.Now,
                };
                AttachActionHandlers(a);
                target.Add(a);
            }
        }

        /// <summary>
        /// V1.2 — Demande à Med un PATCH du projet existant (diff par section et par action).
        /// Le contenu de v(N) sert de base, Med modifie chirurgicalement.
        /// </summary>
        private async Task PatchAsync()
        {
            if (_suggester == null || Projet == null || Projet.IsValidee) return;

            IsProposerEnCours = true;
            StatusMessage = "⏳ Med relit le projet et les nouveaux éléments du dossier...";

            try
            {
                var (ok, patch, error) = await _suggester.SuggestPatchAsync(
                    Projet, _patientDirectoryPath, CancellationToken.None);
                if (!ok || patch == null)
                {
                    StatusMessage = $"❌ Patch impossible : {error ?? "réponse vide"}";
                    return;
                }

                int modifs = 0, nouveaux = 0, archives = 0, inchangees = 0;

                // Sections texte libre
                ApplyTextPatch(patch.Objectifs,      v => Projet.ObjectifsPrioritaires = v, ref modifs);
                ApplyTextPatch(patch.Ressources,     v => Projet.RessourcesASoutenir   = v, ref modifs);
                ApplyTextPatch(patch.Reevaluation,   v => Projet.ReevaluationChecklist = v, ref modifs);
                ApplyTextPatch(patch.CoConstruction, v => Projet.CoConstructionFamille = v, ref modifs);

                // Actions par section
                ApplyActionPatches(patch.ActionsMedicales,         Projet.ActionsMedicales,         ref modifs, ref nouveaux, ref archives, ref inchangees);
                ApplyActionPatches(patch.ActionsPsychologiques,    Projet.ActionsPsychologiques,    ref modifs, ref nouveaux, ref archives, ref inchangees);
                ApplyActionPatches(patch.ActionsDeveloppementales, Projet.ActionsDeveloppementales, ref modifs, ref nouveaux, ref archives, ref inchangees);
                ApplyActionPatches(patch.ActionsEnvironnementales, Projet.ActionsEnvironnementales, ref modifs, ref nouveaux, ref archives, ref inchangees);

                _service.SaveBrouillon(Projet);
                StatusMessage = $"✓ Patch Med proposé — {modifs} modif(s), {nouveaux} nouvelle(s), {archives} à archiver, {inchangees} inchangée(s). Acceptez/rejetez action par action.";
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

        private static void ApplyTextPatch(
            ProjetTherapeutiqueSuggesterService.SectionTextPatch p,
            Action<string> setter,
            ref int modifs)
        {
            if (p == null) return;
            if (p.Statut == "modifiee" && !string.IsNullOrWhiteSpace(p.Contenu))
            {
                setter(p.Contenu.Trim());
                modifs++;
            }
        }

        private void ApplyActionPatches(
            List<ProjetTherapeutiqueSuggesterService.ActionPatch> patches,
            ObservableCollection<ProjetAction> target,
            ref int modifs, ref int nouveaux, ref int archives, ref int inchangees)
        {
            foreach (var p in patches)
            {
                switch (p.Statut)
                {
                    case "nouvelle":
                        if (string.IsNullOrWhiteSpace(p.Libelle)) continue;
                        var newAction = new ProjetAction
                        {
                            Libelle             = p.Libelle.Trim(),
                            Description         = (p.Description ?? "").Trim(),
                            IndicateurReussite  = (p.IndicateurReussite ?? "").Trim(),
                            LienSyntheseSection = (p.LienSyntheseSection ?? "").Trim(),
                            DiffSuggere         = ActionDiffStatut.Nouvelle,
                            DiffResume          = p.DiffResume ?? "",
                            Statut              = ActionStatut.AVenir,
                            DateDecision        = DateTime.Now,
                            DateDernierStatut   = DateTime.Now,
                        };
                        AttachActionHandlers(newAction);
                        target.Add(newAction);
                        nouveaux++;
                        break;

                    case "modifiee":
                        {
                            var existing = target.FirstOrDefault(a => a.Id == p.Id);
                            if (existing == null) continue;
                            existing.LibellePrecedent       = existing.Libelle;
                            existing.DescriptionPrecedente  = existing.Description;
                            if (!string.IsNullOrWhiteSpace(p.Libelle))            existing.Libelle = p.Libelle.Trim();
                            if (!string.IsNullOrWhiteSpace(p.Description))        existing.Description = p.Description.Trim();
                            if (!string.IsNullOrWhiteSpace(p.IndicateurReussite)) existing.IndicateurReussite = p.IndicateurReussite.Trim();
                            if (!string.IsNullOrWhiteSpace(p.LienSyntheseSection)) existing.LienSyntheseSection = p.LienSyntheseSection.Trim();
                            existing.DiffSuggere = ActionDiffStatut.Modifiee;
                            existing.DiffResume  = p.DiffResume ?? "";
                            modifs++;
                            break;
                        }

                    case "a_archiver":
                        {
                            var existing = target.FirstOrDefault(a => a.Id == p.Id);
                            if (existing == null) continue;
                            existing.DiffSuggere = ActionDiffStatut.AArchiver;
                            existing.DiffResume  = p.DiffResume ?? "";
                            archives++;
                            break;
                        }

                    default:
                        inchangees++;
                        break;
                }
            }
        }

        private void AccepterAction(ProjetAction? a)
        {
            if (a == null || Projet == null || Projet.IsValidee) return;
            if (a.DiffSuggere == ActionDiffStatut.AArchiver)
            {
                // Accepter l'archivage = supprimer l'action
                SupprimerAction(a);
                return;
            }
            // Sinon = on garde le nouveau contenu, on remet le statut diff à Inchangée
            a.DiffSuggere      = ActionDiffStatut.Inchangee;
            a.LibellePrecedent = a.Libelle;
            a.DescriptionPrecedente = a.Description;
            a.DiffResume       = "";
            ScheduleAutoSave();
        }

        private void RejeterAction(ProjetAction? a)
        {
            if (a == null || Projet == null || Projet.IsValidee) return;
            switch (a.DiffSuggere)
            {
                case ActionDiffStatut.Nouvelle:
                    // Rejeter une nouvelle action = la supprimer
                    SupprimerAction(a);
                    break;
                case ActionDiffStatut.Modifiee:
                    // Rejeter = revenir au contenu précédent
                    if (!string.IsNullOrEmpty(a.LibellePrecedent))      a.Libelle     = a.LibellePrecedent;
                    if (!string.IsNullOrEmpty(a.DescriptionPrecedente)) a.Description = a.DescriptionPrecedente;
                    a.DiffSuggere = ActionDiffStatut.Inchangee;
                    a.DiffResume  = "";
                    ScheduleAutoSave();
                    break;
                case ActionDiffStatut.AArchiver:
                    // Rejeter l'archivage = on garde l'action telle quelle
                    a.DiffSuggere = ActionDiffStatut.Inchangee;
                    a.DiffResume  = "";
                    ScheduleAutoSave();
                    break;
            }
        }

        private void Valider()
        {
            if (Projet == null) return;
            try
            {
                var patientNom = Projet.PatientNomComplet;
                _service.Validate(Projet);
                StatusMessage = $"✓ Projet Thérapeutique v{Projet.Version} validé.";
                NotifyAll();
                ProjetValidated?.Invoke(patientNom);
                Closed?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur validation : {ex.Message}";
            }
        }

        private void FermerSansValider()
        {
            DetachHandlers();
            Projet = null;
            Closed?.Invoke();
        }

        // ── Actions structurées ──────────────────────────────────────────────

        private void AjouterAction(string section)
        {
            if (Projet == null || Projet.IsValidee) return;
            var action = new ProjetAction { Libelle = "(nouvelle action)" };
            switch (section)
            {
                case "medicale":         Projet.ActionsMedicales.Add(action); break;
                case "psychologique":    Projet.ActionsPsychologiques.Add(action); break;
                case "developpementale": Projet.ActionsDeveloppementales.Add(action); break;
                case "environnementale": Projet.ActionsEnvironnementales.Add(action); break;
            }
            AttachActionHandlers(action);
            NotifyProgression();
            ScheduleAutoSave();
        }

        private void SupprimerAction(ProjetAction? a)
        {
            if (a == null || Projet == null || Projet.IsValidee) return;
            Projet.ActionsMedicales.Remove(a);
            Projet.ActionsPsychologiques.Remove(a);
            Projet.ActionsDeveloppementales.Remove(a);
            Projet.ActionsEnvironnementales.Remove(a);
            DetachActionHandlers(a);
            NotifyProgression();
            ScheduleAutoSave();
        }

        /// <summary>
        /// Clic sur le chip statut → cycle ⚪ → 🟡 → ✅ → ⛔ → ⚪.
        /// La date est auto-mise à jour dans le setter de Statut.
        /// </summary>
        private void CyclerStatut(ProjetAction? a)
        {
            if (a == null || Projet == null || Projet.IsValidee) return;
            a.Statut = a.Statut switch
            {
                ActionStatut.AVenir    => ActionStatut.EnCours,
                ActionStatut.EnCours   => ActionStatut.Fait,
                ActionStatut.Fait      => ActionStatut.Abandonne,
                ActionStatut.Abandonne => ActionStatut.AVenir,
                _                      => ActionStatut.AVenir
            };
            NotifyProgression();
            ScheduleAutoSave();
        }

        // ── Auto-save debounced ──────────────────────────────────────────────

        private void AttachHandlers()
        {
            if (Projet == null) return;
            Projet.PropertyChanged += OnProjetChanged;
            foreach (var a in Projet.ToutesActions) AttachActionHandlers(a);
            // Collection changes
            Projet.ActionsMedicales.CollectionChanged         += OnActionsCollectionChanged;
            Projet.ActionsPsychologiques.CollectionChanged    += OnActionsCollectionChanged;
            Projet.ActionsDeveloppementales.CollectionChanged += OnActionsCollectionChanged;
            Projet.ActionsEnvironnementales.CollectionChanged += OnActionsCollectionChanged;
        }

        private void DetachHandlers()
        {
            if (Projet == null) return;
            Projet.PropertyChanged -= OnProjetChanged;
            foreach (var a in Projet.ToutesActions) DetachActionHandlers(a);
            Projet.ActionsMedicales.CollectionChanged         -= OnActionsCollectionChanged;
            Projet.ActionsPsychologiques.CollectionChanged    -= OnActionsCollectionChanged;
            Projet.ActionsDeveloppementales.CollectionChanged -= OnActionsCollectionChanged;
            Projet.ActionsEnvironnementales.CollectionChanged -= OnActionsCollectionChanged;
        }

        private void AttachActionHandlers(ProjetAction a) => a.PropertyChanged += OnActionChanged;
        private void DetachActionHandlers(ProjetAction a) => a.PropertyChanged -= OnActionChanged;

        private void OnProjetChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Projet?.IsValidee == true) return;
            ScheduleAutoSave();
        }

        private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Projet?.IsValidee == true) return;
            ScheduleAutoSave();
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnActionsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (Projet?.IsValidee == true) return;
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
                    if (ct.IsCancellationRequested || Projet == null || Projet.IsValidee) return;
                    _service.SaveBrouillon(Projet);
                }
                catch { /* meilleur effort */ }
            });
        }

        private void NotifyProgression()
        {
            OnPropertyChanged(nameof(Projet));
            if (Projet == null) return;
            // Propager les calculs de progression au binding
            typeof(ProjetTherapeutique).GetMethod("OnPropertyChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            // Force notification des propriétés agrégées
            // Note: le modèle ne notifie pas ses props calculées (ProgressionPct etc.) — on
            // se contente de notifier l'objet entier ici, le binding {Binding Projet.ProgressionPct}
            // sera réévalué.
        }

        private void NotifyAll()
        {
            OnPropertyChanged(nameof(Projet));
            OnPropertyChanged(nameof(TitreVue));
            OnPropertyChanged(nameof(StatutLabel));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(IsBrouillon));
            OnPropertyChanged(nameof(IsReevaluationPassee));
            OnPropertyChanged(nameof(IsModePatch));
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FermerCommand  as RelayCommand)?.RaiseCanExecuteChanged();
            (ProposerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PatchCommand    as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}

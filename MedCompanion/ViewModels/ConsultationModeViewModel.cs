using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Models.Urgences;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services;
using MedCompanion.Services.Consultation;
using MedCompanion.Services.Evaluations;
using MedCompanion.Services.Synthesis;
using MedCompanion.Services.Therapeutique;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Urgence;
using MedCompanion.ViewModels.Restitutions;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour le Mode Consultation
    /// Gère l'état de l'interface adaptative (travail/dossier) et Med assistant
    /// </summary>
    public class ConsultationModeViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Émis dès qu'une note est sauvegardée dans le dossier patient depuis le mode Consultation
        /// (1ère consult, observations, suivi). Permet au mode Console de rafraîchir sa liste de notes.
        /// </summary>
        public event EventHandler? NoteSavedToPatient;
        private void RaiseNoteSavedToPatient()
            => NoteSavedToPatient?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Émis après qu'un PDF de restitution 1er entretien a été sauvegardé.
        /// MainWindow s'abonne pour l'enregistrer dans le panel DOCUMENTS (dossier bleu).
        /// </summary>
        public event EventHandler<string>? RestitutionPdfSavedToPatient;

        /// <summary>
        /// Émis quand l'utilisateur choisit un patient dans le drawer « Consultations récentes ».
        /// MainWindow s'abonne pour exécuter son flux COMPLET de sélection (en-tête patient +
        /// tous les panneaux), pas seulement le dossier consultation — sinon l'en-tête du haut
        /// resterait figé sur le patient précédent.
        /// </summary>
        public event EventHandler<PatientIndexEntry>? PatientSwitchRequested;


        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region Layout State

        private ConsultationViewState _currentState = ConsultationViewState.Consultation;
        /// <summary>
        /// État actuel de l'affichage (FocusTravail, Consultation, FocusDossier)
        /// </summary>
        public ConsultationViewState CurrentState
        {
            get => _currentState;
            set
            {
                if (SetProperty(ref _currentState, value))
                {
                    UpdateLayoutProportions();
                    OnPropertyChanged(nameof(IsFocusTravail));
                    OnPropertyChanged(nameof(IsConsultation));
                    OnPropertyChanged(nameof(IsFocusDossier));
                }
            }
        }

        public bool IsFocusTravail => CurrentState == ConsultationViewState.FocusTravail;
        public bool IsConsultation => CurrentState == ConsultationViewState.Consultation;
        public bool IsFocusDossier => CurrentState == ConsultationViewState.FocusDossier;

        private double _workspaceWidth = 0.67;
        /// <summary>
        /// Proportion de l'espace de travail (0 à 1)
        /// </summary>
        public double WorkspaceWidth
        {
            get => _workspaceWidth;
            set => SetProperty(ref _workspaceWidth, value);
        }

        private double _dossierWidth = 0.33;
        /// <summary>
        /// Proportion du dossier patient (0 à 1)
        /// </summary>
        public double DossierWidth
        {
            get => _dossierWidth;
            set => SetProperty(ref _dossierWidth, value);
        }

        /// <summary>
        /// Met à jour les proportions selon l'état actuel
        /// </summary>
        private void UpdateLayoutProportions()
        {
            switch (CurrentState)
            {
                case ConsultationViewState.FocusTravail:
                    WorkspaceWidth = 1.0;
                    DossierWidth = 0.0;
                    break;
                case ConsultationViewState.Consultation:
                    WorkspaceWidth = 0.67;
                    DossierWidth = 0.33;
                    break;
                case ConsultationViewState.FocusDossier:
                    WorkspaceWidth = 0.0;
                    DossierWidth = 1.0;
                    break;
            }
        }

        #endregion

        #region Med Assistant State

        private MedConsultationMode _medMode = MedConsultationMode.Suggestions;
        /// <summary>
        /// Mode de comportement de Med (Silencieux, Suggestions, Checklist)
        /// </summary>
        public MedConsultationMode MedMode
        {
            get => _medMode;
            set
            {
                if (SetProperty(ref _medMode, value))
                {
                    OnPropertyChanged(nameof(IsMedSilencieux));
                    OnPropertyChanged(nameof(IsMedSuggestions));
                    OnPropertyChanged(nameof(IsMedChecklist));
                }
            }
        }

        public bool IsMedSilencieux => MedMode == MedConsultationMode.Silencieux;
        public bool IsMedSuggestions => MedMode == MedConsultationMode.Suggestions;
        public bool IsMedChecklist => MedMode == MedConsultationMode.Checklist;

        // Toggle indépendant pour la voix de Med (UI seulement pour l'instant — pas d'effet sur le mode)
        private bool _isMedVoiceOn = false;
        public bool IsMedVoiceOn
        {
            get => _isMedVoiceOn;
            set => SetProperty(ref _isMedVoiceOn, value);
        }

        private bool _isMedExpanded = true;
        /// <summary>
        /// Indique si le panneau Med est étendu ou réduit
        /// </summary>
        public bool IsMedExpanded
        {
            get => _isMedExpanded;
            set
            {
                if (SetProperty(ref _isMedExpanded, value))
                {
                    OnPropertyChanged(nameof(IsMedCollapsed));
                }
            }
        }

        /// <summary>
        /// Inverse de IsMedExpanded pour le binding
        /// </summary>
        public bool IsMedCollapsed => !IsMedExpanded;

        /// <summary>
        /// Suggestions contextuelles de Med
        /// </summary>
        public ObservableCollection<MedSuggestion> Suggestions { get; } = new();

        /// <summary>
        /// Items de la checklist
        /// </summary>
        public ObservableCollection<ChecklistItem> ChecklistItems { get; } = new();

        #endregion

        #region Dossier Patient State

        private DossierTab _activeDossierTab = DossierTab.Synthese;
        /// <summary>
        /// Intercalaire actif du dossier
        /// </summary>
        public DossierTab ActiveDossierTab
        {
            get => _activeDossierTab;
            set
            {
                if (SetProperty(ref _activeDossierTab, value))
                {
                    OnPropertyChanged(nameof(IsCouvertureActive));
                    OnPropertyChanged(nameof(IsSyntheseActive));
                    OnPropertyChanged(nameof(IsAdminActive));
                    OnPropertyChanged(nameof(IsConsultationsActive));
                    OnPropertyChanged(nameof(IsProjetActive));
                    OnPropertyChanged(nameof(IsBilansActive));
                    OnPropertyChanged(nameof(IsDocumentsActive));

                    if (value == DossierTab.Administratif || value == DossierTab.Consultations)
                        RefreshAdminInfo();
                }
            }
        }

        public bool IsCouvertureActive => ActiveDossierTab == DossierTab.Couverture;
        public bool IsSyntheseActive => ActiveDossierTab == DossierTab.Synthese;
        public bool IsAdminActive => ActiveDossierTab == DossierTab.Administratif;
        public bool IsConsultationsActive => ActiveDossierTab == DossierTab.Consultations;
        public bool IsProjetActive => ActiveDossierTab == DossierTab.ProjetTherapeutique;
        public bool IsBilansActive => ActiveDossierTab == DossierTab.Bilans;
        public bool IsDocumentsActive => ActiveDossierTab == DossierTab.Documents;

        // ── Page Administratif du dossier (infos réelles depuis patient.json) ────

        private string _adminInfoText = "Aucune information administrative.";
        public string AdminInfoText { get => _adminInfoText; private set => SetProperty(ref _adminInfoText, value); }

        private string _ecoleContactText = "";
        public string EcoleContactText { get => _ecoleContactText; private set => SetProperty(ref _ecoleContactText, value); }

        private bool _hasEcoleContact;
        public bool HasEcoleContact { get => _hasEcoleContact; private set => SetProperty(ref _hasEcoleContact, value); }

        // ── Contacts parents (formulaire_data.json) ──
        private bool _hasParentsContact;
        public bool HasParentsContact { get => _hasParentsContact; private set => SetProperty(ref _hasParentsContact, value); }
        private string _contactParentsPereText = "";
        public string ContactParentsPereText { get => _contactParentsPereText; private set => SetProperty(ref _contactParentsPereText, value); }
        private string _contactParentsMereText = "";
        public string ContactParentsMereText { get => _contactParentsMereText; private set => SetProperty(ref _contactParentsMereText, value); }

        // ── Adresse lieu de vie (formulaire_data.json) ──
        private bool _hasAdresseLieuVie;
        public bool HasAdresseLieuVie { get => _hasAdresseLieuVie; private set => SetProperty(ref _hasAdresseLieuVie, value); }
        private string _adresseLieuVieText = "";
        public string AdresseLieuVieText { get => _adresseLieuVieText; private set => SetProperty(ref _adresseLieuVieText, value); }

        // ── Antécédents familiaux (formulaire_data.json) ──
        private bool _hasAntecedentsFamiliaux;
        public bool HasAntecedentsFamiliaux { get => _hasAntecedentsFamiliaux; private set => SetProperty(ref _hasAntecedentsFamiliaux, value); }
        private string _antecedentsFamiliauxText = "";
        public string AntecedentsFamiliauxText { get => _antecedentsFamiliauxText; private set => SetProperty(ref _antecedentsFamiliauxText, value); }

        /// <summary>
        /// Recharge les infos de la page Administratif depuis patient.json
        /// (appelé à l'ouverture de l'intercalaire et après mise à jour des métadonnées).
        /// </summary>
        private void RefreshAdminInfo()
        {
            if (_patientIndex == null || CurrentPatient == null)
            {
                AdminInfoText = "Aucun patient sélectionné.";
                EcoleContactText = "";
                HasEcoleContact = false;
                return;
            }

            var m = _patientIndex.GetMetadata(CurrentPatient.Id);
            if (m == null)
            {
                AdminInfoText = "Aucune information administrative enregistrée.";
                EcoleContactText = "";
                HasEcoleContact = false;
                return;
            }

            var sb = new System.Text.StringBuilder();
            void Add(string label, string? val) { if (!string.IsNullOrWhiteSpace(val)) sb.AppendLine($"{label} : {val.Trim()}"); }

            Add("Nom", m.NomComplet);
            Add("Date de naissance", m.DobFormatted);
            Add("Sexe", m.Sexe);
            Add("N° dossier", m.NumeroDossier);
            Add("Adresse", string.Join(" ", new[] { m.AdresseRue, m.AdresseCodePostal, m.AdresseVille }
                                                .Where(s => !string.IsNullOrWhiteSpace(s))));
            Add("Sécurité sociale", m.NumeroSecuriteSociale);

            var acc = $"{m.AccompagnantPrenom} {m.AccompagnantNom}".Trim();
            if (!string.IsNullOrWhiteSpace(m.AccompagnantLien))
                acc = string.IsNullOrWhiteSpace(acc) ? m.AccompagnantLien : $"{acc} ({m.AccompagnantLien})";
            Add("Accompagnant", acc);
            Add("Tél. accompagnant", m.AccompagnantTelephone);
            Add("Email accompagnant", m.AccompagnantEmail);
            Add("Médecin traitant", $"{m.MedecinTraitantPrenom} {m.MedecinTraitantNom}".Trim());
            Add("École", m.Ecole);
            Add("Classe", m.Classe);

            AdminInfoText = sb.Length > 0 ? sb.ToString().TrimEnd() : "Aucune information administrative enregistrée.";

            // Zone contact école
            var esb = new System.Text.StringBuilder();
            void AddE(string label, string? val) { if (!string.IsNullOrWhiteSpace(val)) esb.AppendLine($"{label} : {val.Trim()}"); }
            AddE("Établissement", m.Ecole);
            AddE("Adresse", string.Join(" ", new[] { m.EcoleAdresse, m.EcoleCodePostal, m.EcoleCommune }
                                              .Where(s => !string.IsNullOrWhiteSpace(s))));
            AddE("Téléphone", m.EcoleTelephone);
            AddE("Email", m.EcoleEmail);
            AddE("Code UAI", m.EcoleUai);

            EcoleContactText = esb.ToString().TrimEnd();
            HasEcoleContact = esb.Length > 0;

            // ── Formulaire parents (formulaire_data.json) ──
            var fds = new MedCompanion.Services.Consultation.FormulaireDataService();
            var fd  = fds.Load(CurrentPatient.DirectoryPath);
            bool hasFormData = fds.HasData(CurrentPatient.DirectoryPath);

            // Contacts père
            var pereNom = $"{fd.PerePrenom} {fd.PereNom}".Trim();
            var pereLines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(pereNom))  pereLines.Add(pereNom);
            if (!string.IsNullOrWhiteSpace(fd.PereTel))   pereLines.Add($"📞 {fd.PereTel}");
            if (!string.IsNullOrWhiteSpace(fd.PereEmail)) pereLines.Add($"✉ {fd.PereEmail}");
            ContactParentsPereText = string.Join("\n", pereLines);

            // Contacts mère
            var mereNom = $"{fd.MerePrenom} {fd.MereNom}".Trim();
            var mereLines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(mereNom))  mereLines.Add(mereNom);
            if (!string.IsNullOrWhiteSpace(fd.MereTel))   mereLines.Add($"📞 {fd.MereTel}");
            if (!string.IsNullOrWhiteSpace(fd.MereEmail)) mereLines.Add($"✉ {fd.MereEmail}");
            ContactParentsMereText = string.Join("\n", mereLines);

            HasParentsContact = pereLines.Count > 0 || mereLines.Count > 0;

            // Adresse lieu de vie
            var adLignes = new System.Collections.Generic.List<string>();

            static string GardeLabel(string g) => g switch {
                "mere"  => "chez la mère",
                "pere"  => "chez le père",
                "autre" => "autre",
                _       => "" };

            var adr1 = string.Join(", ", new[] { fd.Adresse, $"{fd.CodePostal} {fd.Ville}".Trim() }
                                         .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(adr1))
            {
                var g1 = GardeLabel(fd.GardeAdresse1);
                adLignes.Add("Adresse 1" + (g1.Length > 0 ? $" ({g1})" : "") + " : " + adr1);
            }

            var adr2 = string.Join(", ", new[] { fd.Adresse2, $"{fd.CodePostal2} {fd.Ville2}".Trim() }
                                         .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(adr2))
            {
                var g2 = GardeLabel(fd.GardeAdresse2);
                adLignes.Add("Adresse 2" + (g2.Length > 0 ? $" ({g2})" : "") + " : " + adr2);
            }

            if (!string.IsNullOrWhiteSpace(fd.SituationFamiliale))
                adLignes.Add("Situation : " + fd.SituationFamiliale switch {
                    "ensemble"       => "Parents ensemble",
                    "separes"        => "Séparés",
                    "divorces"       => "Divorcés",
                    "garde_alternee" => "Garde alternée",
                    "recomposee"     => "Famille recomposée",
                    _                => fd.SituationFamiliale });
            if (!string.IsNullOrWhiteSpace(fd.GardePrincipale))
                adLignes.Add("Garde principale : " + fd.GardePrincipale switch {
                    "parents" => "Les deux parents",
                    "mere"    => "Mère",
                    "pere"    => "Père",
                    _         => fd.GardePrincipale });
            AdresseLieuVieText = string.Join("\n", adLignes);
            HasAdresseLieuVie  = adLignes.Count > 0;

            // Antécédents familiaux — on affiche seulement OUI et NSP
            var antec = new System.Collections.Generic.List<string>();
            void AddA(string label, string val)
            {
                if (val == "oui") antec.Add($"• {label} : OUI");
                else if (val == "nsp") antec.Add($"• {label} : NSP");
            }
            AddA("TDAH / Troubles de l'attention", fd.Tdah);
            AddA("Dyslexie / Troubles des apprentissages", fd.Dyslexie);
            AddA("TSA", fd.Tsa);
            AddA("Troubles anxieux", fd.TroublesAnxieux);
            AddA("Dépression", fd.Depression);
            AddA("Bipolarité", fd.Bipolarite);
            AddA("Addictions", fd.Addictions);
            AddA("Tentative de suicide", fd.TentativeSuicide);
            AntecedentsFamiliauxText = antec.Count > 0
                ? string.Join("\n", antec)
                : hasFormData ? "Aucun antécédent familial signalé." : "";
            HasAntecedentsFamiliaux = hasFormData;
        }

        private PatientIndexEntry? _currentPatient;
        /// <summary>
        /// Patient actuellement en consultation
        /// </summary>
        public PatientIndexEntry? CurrentPatient
        {
            get => _currentPatient;
            set
            {
                if (SetProperty(ref _currentPatient, value))
                {
                    OnPropertyChanged(nameof(HasPatient));
                    OnPropertyChanged(nameof(PatientDisplayName));
                    OnPropertyChanged(nameof(PatientAge));
                    _contextCompletionChecked = false;
                }
            }
        }

        public bool HasPatient => CurrentPatient != null;
        public string PatientDisplayName => CurrentPatient != null
            ? $"{CurrentPatient.Nom} {CurrentPatient.Prenom}"
            : "Aucun patient";
        public string PatientAge => !string.IsNullOrEmpty(CurrentPatient?.Dob)
            ? $"Né(e) le {CurrentPatient.Dob}"
            : "";

        #endregion

        #region Note de Consultation

        private string _noteContent = "";
        /// <summary>
        /// Contenu de la note de consultation en cours
        /// </summary>
        public string NoteContent
        {
            get => _noteContent;
            set
            {
                if (SetProperty(ref _noteContent, value))
                {
                    OnPropertyChanged(nameof(HasNoteContent));
                    // Auto-save trigger pourrait être ajouté ici
                }
            }
        }

        public bool HasNoteContent => !string.IsNullOrWhiteSpace(NoteContent);

        // ── Note finale : relecture/correction par cartouches éditables (V0d) ────

        /// <summary>
        /// Sections affichées à l'étape de relecture : uniquement les blocs remplis et non masqués.
        /// Snapshot pris à l'entrée en mode FinalNote ; l'utilisateur édite les FreeText en place
        /// (mêmes instances que <see cref="InterrogatoireBlocks"/>, donc aucune divergence).
        /// </summary>
        public ObservableCollection<ConsultationBlockViewModel> FinalNoteBlocks { get; } = new();

        /// <summary>Blocs disponibles à l'ajout manuel (vides ou masqués, pas encore affichés).</summary>
        public IEnumerable<ConsultationBlockViewModel> AvailableSectionsToAdd =>
            InterrogatoireBlocks.Where(b => !FinalNoteBlocks.Contains(b));

        /// <summary>Y a-t-il des sections ajoutables ? (pilote la visibilité du bouton « + Ajouter »)</summary>
        public bool HasSectionsToAdd => AvailableSectionsToAdd.Any();

        private void PopulateFinalNoteBlocks()
        {
            FinalNoteBlocks.Clear();
            foreach (var b in InterrogatoireBlocks.Where(b => !b.IsHidden && b.IsNotEmpty))
                FinalNoteBlocks.Add(b);
            OnPropertyChanged(nameof(AvailableSectionsToAdd));
            OnPropertyChanged(nameof(HasSectionsToAdd));
        }

        /// <summary>Ajoute manuellement une section (vide) à la relecture — bouton « + Ajouter une section ».</summary>
        private void AddFinalNoteSection(ConsultationBlockViewModel? block)
        {
            if (block == null || FinalNoteBlocks.Contains(block)) return;
            block.IsHidden = false;
            FinalNoteBlocks.Add(block);
            OnPropertyChanged(nameof(AvailableSectionsToAdd));
            OnPropertyChanged(nameof(HasSectionsToAdd));
        }

        /// <summary>
        /// Reconstruit <see cref="NoteContent"/> (markdown) depuis les FreeText des blocs.
        /// Appelé avant la sauvegarde : les cartouches sont l'unique source de vérité.
        /// </summary>
        private void RebuildNoteContentFromBlocks()
        {
            var blocks = InterrogatoireBlocks.Select(vm => new ConsultationBlock
            {
                Key = vm.Key, Title = vm.Title, FreeText = vm.FreeText,
                ExpectedThemes = vm.ExpectedThemes, CoveredThemes = vm.CoveredThemes
            }).ToList();
            NoteContent = InterrogatoireExtractorService.BuildFinalNote(blocks, ConsultationDate);
        }

        private DateTime _consultationDate = DateTime.Now;
        /// <summary>
        /// Date de la consultation en cours
        /// </summary>
        public DateTime ConsultationDate
        {
            get => _consultationDate;
            set => SetProperty(ref _consultationDate, value);
        }

        #endregion

        #region Interrogatoire V0b (Blocs Adaptatifs)

        private bool _contextCompletionChecked = false;
        private ILLMService? _llmService;
        private StorageService? _storageService;
        private readonly InterrogatoireExtractorService  _extractor            = new();
        private readonly IncrementalExtractorService     _incrementalExtractor = new();
        private readonly QualityCheckService             _qualityChecker       = new();
        private WhisperStreamingService? _whisperService;

        // ── V0b : services adaptatifs ──────────────────────────────────────────
        private readonly BlockSetResolver            _blockSetResolver     = new();
        private readonly MotifPrincipalDetector       _motifDetector        = new();
        private readonly ContextualBlockSuggester     _blockSuggester;
        private readonly BlockPrefiller               _blockPrefiller       = new();

        // ── Brouillon 1ère consultation (persistance multi-session) ───────────
        private readonly MedCompanion.Services.Consultation.PremiereConsultationDraftService _draftSvc = new();
        private System.Threading.CancellationTokenSource? _draftSaveCts;

        /// <summary>Âge confirmé du patient (null si pas encore confirmé)</summary>
        private int? _confirmedAge;
        private int? _ageFromInterrogatoire;   // âge dit pendant l'interrogatoire (extrait du bloc "age")
        public int? ConfirmedAge
        {
            get => _confirmedAge;
            set
            {
                if (SetProperty(ref _confirmedAge, value))
                {
                    OnPropertyChanged(nameof(HasConfirmedAge));
                    if (value.HasValue)
                        OnAgeConfirmed(value.Value);
                }
            }
        }

        private bool _isAgeConfirmed;
        public bool HasConfirmedAge => _isAgeConfirmed && _confirmedAge.HasValue;

        /// <summary>Motif principal détecté</summary>
        private string _detectedMotif = "";
        public string DetectedMotif
        {
            get => _detectedMotif;
            set
            {
                if (SetProperty(ref _detectedMotif, value))
                    OnPropertyChanged(nameof(HasDetectedMotif));
            }
        }
        public bool HasDetectedMotif => !string.IsNullOrWhiteSpace(_detectedMotif);

        /// <summary>Suggestions de blocs supplémentaires (chips)</summary>
        public ObservableCollection<BlockSuggestionViewModel> BlockSuggestions { get; } = new();
        public bool HasBlockSuggestions => BlockSuggestions.Count > 0;

        /// <summary>Indique si la structure est gelée (après ~90s)</summary>
        private bool _isStructureFrozen;
        public bool IsStructureFrozen
        {
            get => _isStructureFrozen;
            set => SetProperty(ref _isStructureFrozen, value);
        }

        // File d'extraction : jamais deux appels LLM simultanés
        private readonly SemaphoreSlim _extractionLock    = new(1, 1);
        private string                 _pendingSegment    = "";

        private PatientIndexService? _patientIndex;

        /// <summary>
        /// Injecte le service d'index patients (partagé avec le mode Console).
        /// Utilisé par le drawer "Patients récents" (bord gauche).
        /// </summary>
        public void InjectPatientIndex(PatientIndexService patientIndex)
        {
            _patientIndex = patientIndex;
        }

        private UrgenceDispatcher? _urgenceDispatcher;
        private UrgenceLogService? _urgenceLogService;

        private EvaluationPhaseViewModel? _evaluationPhase;
        /// <summary>
        /// VM du panneau "Phase d'évaluation" (Étape 1 V0). Toujours présent une fois injecté ;
        /// son état interne (CanStart / CanResume / IsWorkingPreparation) détermine ce qui s'affiche.
        /// </summary>
        public EvaluationPhaseViewModel? EvaluationPhase
        {
            get => _evaluationPhase;
            private set => SetProperty(ref _evaluationPhase, value);
        }

        private EvaluationPhaseService? _evaluationPhaseService;

        public void InjectEvaluationServices(EvaluationPhaseService phaseService,
                                             PreparationSuggesterService? suggester,
                                             AxesSuggesterService? axesSuggester = null,
                                             AxisExtractorService?  axisExtractor = null,
                                             BilanFinalSuggesterService? bilanFinalSuggester = null,
                                             FeuilleLectureService? feuilleLecture = null,
                                             BrancheEnvironnementLectureService? brancheLecture = null)
        {
            _evaluationPhaseService = phaseService;
            EvaluationPhase = new EvaluationPhaseViewModel(
                phaseService, suggester, axesSuggester, axisExtractor, _whisperService, bilanFinalSuggester, feuilleLecture, brancheLecture);
            // À chaque création/clôture d'évaluation, rafraîchit la frise + les blocs de synthèse
            EvaluationPhase.PhaseStateChanged += LoadEvaluationCards;
            // Quand l'utilisateur ferme la vue lecture seule (« ✕ Fermer la vue »), on sort
            // du mode évaluation pour revenir à la frise normale — sinon on resterait sur
            // l'écran « Aucune évaluation en cours — Commencer ».
            EvaluationPhase.ReadOnlyViewClosed += () => IsEvaluationPhaseMode = false;
            // Si un patient est déjà chargé, on lui passe tout de suite
            if (_currentPatient != null) EvaluationPhase.SetCurrentPatient(_currentPatient);
        }

        private bool _isEvaluationPhaseMode;
        /// <summary>
        /// True quand le médecin a ouvert le panneau "Phase d'évaluation" via le combo "+".
        /// </summary>
        public bool IsEvaluationPhaseMode
        {
            get => _isEvaluationPhaseMode;
            set => SetProperty(ref _isEvaluationPhaseMode, value);
        }

        private bool _isSyntheseGlobaleMode;
        /// <summary>
        /// True quand le médecin a ouvert le panneau "Synthèse Globale" via le combo "+".
        /// </summary>
        public bool IsSyntheseGlobaleMode
        {
            get => _isSyntheseGlobaleMode;
            set => SetProperty(ref _isSyntheseGlobaleMode, value);
        }

        private bool _isProjetTherapeutiqueMode;
        /// <summary>True quand le médecin a ouvert le panneau "Projet Thérapeutique".</summary>
        public bool IsProjetTherapeutiqueMode
        {
            get => _isProjetTherapeutiqueMode;
            set => SetProperty(ref _isProjetTherapeutiqueMode, value);
        }

        private bool _isReadingPastConsultationMode;
        /// <summary>True quand le médecin consulte une note passée sur le plan de travail.</summary>
        public bool IsReadingPastConsultationMode
        {
            get => _isReadingPastConsultationMode;
            set => SetProperty(ref _isReadingPastConsultationMode, value);
        }

        private bool _isReadingPastPremiereConsultationMode;
        /// <summary>True quand le médecin consulte une 1ère consultation passée (avec étapes).</summary>
        public bool IsReadingPastPremiereConsultationMode
        {
            get => _isReadingPastPremiereConsultationMode;
            set => SetProperty(ref _isReadingPastPremiereConsultationMode, value);
        }

        private string? _pastPremiereFilePath;

        private bool _isEditingPastPremiere;
        /// <summary>True quand l'utilisateur modifie le contenu d'une 1ère consultation passée.</summary>
        public bool IsEditingPastPremiere
        {
            get => _isEditingPastPremiere;
            set => SetProperty(ref _isEditingPastPremiere, value);
        }

        private int _pastPremiereStep = 1;
        public int PastPremiereStep
        {
            get => _pastPremiereStep;
            set
            {
                if (SetProperty(ref _pastPremiereStep, value))
                {
                    OnPropertyChanged(nameof(IsPastPremiereStep1));
                    OnPropertyChanged(nameof(IsPastPremiereStep2));
                    OnPropertyChanged(nameof(IsPastPremiereStep3));
                }
            }
        }

        public bool IsPastPremiereStep1 => PastPremiereStep == 1;
        public bool IsPastPremiereStep2 => PastPremiereStep == 2;
        public bool IsPastPremiereStep3 => PastPremiereStep == 3;

        private string _pastInterrogatoireText = "";
        public string PastInterrogatoireText
        {
            get => _pastInterrogatoireText;
            set => SetProperty(ref _pastInterrogatoireText, value);
        }

        private string _pastObservationsNarrative = "";
        public string PastObservationsNarrative
        {
            get => _pastObservationsNarrative;
            set => SetProperty(ref _pastObservationsNarrative, value);
        }

        private string _pastSynthesisContent = "";
        public string PastSynthesisContent
        {
            get => _pastSynthesisContent;
            set => SetProperty(ref _pastSynthesisContent, value);
        }

        private double _pastInterrogatoireWeight = 0.5;
        public double PastInterrogatoireWeight
        {
            get => _pastInterrogatoireWeight;
            set => SetProperty(ref _pastInterrogatoireWeight, value);
        }

        private double _pastObservationsWeight = 0.8;
        public double PastObservationsWeight
        {
            get => _pastObservationsWeight;
            set => SetProperty(ref _pastObservationsWeight, value);
        }

        private double _pastAverageWeight = 0.65;
        public double PastAverageWeight
        {
            get => _pastAverageWeight;
            set => SetProperty(ref _pastAverageWeight, value);
        }

        private string _pastLLMJustification = "";
        public string PastLLMJustification
        {
            get => _pastLLMJustification;
            set => SetProperty(ref _pastLLMJustification, value);
        }

        public ObservableCollection<PastInterrogatoireBlock> PastInterrogatoireBlocks { get; } = new();
        public ObservableCollection<PastClinicalCard> PastClinicalCards { get; } = new();

        private ConsultationNoteViewModel? _selectedPastConsultation;
        /// <summary>La note de consultation passée actuellement affichée en lecture seule.</summary>
        public ConsultationNoteViewModel? SelectedPastConsultation
        {
            get => _selectedPastConsultation;
            set => SetProperty(ref _selectedPastConsultation, value);
        }

        private ProjetTherapeutiqueViewModel? _projetTherapeutiqueVM;
        public ProjetTherapeutiqueViewModel? ProjetTherapeutiqueVM
        {
            get => _projetTherapeutiqueVM;
            private set => SetProperty(ref _projetTherapeutiqueVM, value);
        }

        private ProjetTherapeutiqueService? _projetTherapeutiqueService;

        /// <summary>Injecte les services Projet Thérapeutique (V1.0 → V1.4).</summary>
        public void InjectProjetTherapeutiqueService(ProjetTherapeutiqueService service,
                                                     ProjetTherapeutiqueSuggesterService? suggester = null,
                                                     ProjetTherapeutiquePilotageService? pilotage = null,
                                                     ProjetTherapeutiqueRelectureService? relecteur = null)
        {
            _projetTherapeutiqueService = service;
            ProjetTherapeutiqueVM = new ProjetTherapeutiqueViewModel(service, suggester, pilotage, relecteur);
            ProjetTherapeutiqueVM.Closed += () =>
            {
                IsProjetTherapeutiqueMode = false;
                LoadProjetTherapeutiqueCards();
            };
            ProjetTherapeutiqueVM.BrouillonCreated += LoadProjetTherapeutiqueCards;
        }

        private void OuvrirProjetTherapeutique()
        {
            if (_projetTherapeutiqueService == null || ProjetTherapeutiqueVM == null)
            {
                System.Windows.MessageBox.Show("Service Projet Thérapeutique non initialisé.",
                    "Projet Thérapeutique",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            if (_currentPatient == null)
            {
                System.Windows.MessageBox.Show("Sélectionnez d'abord un patient.",
                    "Projet Thérapeutique",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            Suivi.Reset();
            ConsultationType = ConsultationType.Normal;
            IsEditingConsultation = false;
            ResetWorkspaceModes();

            ProjetTherapeutiqueVM.OuvrirBrouillonOuCreer(
                _currentPatient.NomComplet,
                psychiatre: "",
                patientDirectoryPath: _currentPatient.DirectoryPath ?? "");
            IsProjetTherapeutiqueMode = true;
        }

        private void OpenProjetTherapeutiqueCard(ProjetTherapeutiqueCardViewModel card)
        {
            if (_projetTherapeutiqueService == null || ProjetTherapeutiqueVM == null || _currentPatient == null) return;
            var full = _projetTherapeutiqueService.Load(card.FilePath);
            if (full == null)
            {
                System.Windows.MessageBox.Show("Impossible de charger ce projet (fichier introuvable).",
                    "Projet Thérapeutique",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            Suivi.Reset();
            ConsultationType = ConsultationType.Normal;
            IsEditingConsultation = false;
            ResetWorkspaceModes();
            ProjetTherapeutiqueVM.Projet = full;
            ProjetTherapeutiqueVM.StatusMessage = full.IsValidee
                ? $"Lecture seule : v{full.Version} validé le {full.DateValidation:dd/MM/yyyy}."
                : $"Brouillon v{full.Version} repris.";
            IsProjetTherapeutiqueMode = true;
        }

        private void DeleteSyntheseGlobaleCard(SyntheseGlobaleCardViewModel card)
        {
            if (string.IsNullOrEmpty(card.FilePath)) return;

            var label = card.IsActive
                ? $"le brouillon de synthèse (Version {card.Version})"
                : $"la synthèse validée v{card.Version} du {card.DateValidation:dd/MM/yyyy}";

            var r = System.Windows.MessageBox.Show(
                $"Supprimer définitivement {label} ?\n\nCette action est irréversible.",
                "Supprimer la synthèse globale",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                if (System.IO.File.Exists(card.FilePath))
                {
                    System.IO.File.Delete(card.FilePath);
                }

                if (IsSyntheseGlobaleMode && SyntheseGlobaleVM?.Synthese?.FilePath == card.FilePath)
                {
                    ResetWorkspaceModes();
                }

                LoadSyntheseGlobaleCards();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Le fichier de synthèse n'a pas pu être supprimé : {ex.Message}",
                    "Erreur",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void DeleteProjetTherapeutiqueCard(ProjetTherapeutiqueCardViewModel card)
        {
            if (string.IsNullOrEmpty(card.FilePath)) return;

            var label = card.IsActive
                ? $"le brouillon de projet (Version {card.Version})"
                : $"le projet validé v{card.Version} du {card.DateValidation:dd/MM/yyyy}";

            var r = System.Windows.MessageBox.Show(
                $"Supprimer définitivement {label} ?\n\nCette action est irréversible.",
                "Supprimer le projet thérapeutique",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                if (System.IO.File.Exists(card.FilePath))
                {
                    System.IO.File.Delete(card.FilePath);
                }

                if (IsProjetTherapeutiqueMode && ProjetTherapeutiqueVM?.Projet?.FilePath == card.FilePath)
                {
                    ResetWorkspaceModes();
                }

                LoadProjetTherapeutiqueCards();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Le fichier de projet n'a pas pu être supprimé : {ex.Message}",
                    "Erreur",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadProjetTherapeutiqueCards()
        {
            ProjetTherapeutiqueCards.Clear();
            ProjetTherapeutiqueBlocks.Clear();

            if (_projetTherapeutiqueService == null || _currentPatient == null
                || string.IsNullOrEmpty(_currentPatient.NomComplet)) return;

            var versions = _projetTherapeutiqueService.ListVersions(_currentPatient.NomComplet);

            // Frise : toutes versions, ordre chronologique ancien → récent
            foreach (var v in versions.OrderBy(x => x.DateRedaction))
                ProjetTherapeutiqueCards.Add(new ProjetTherapeutiqueCardViewModel(v));

            // Onglet PROJET du dossier bleu : versions validées, plus récente d'abord
            foreach (var v in versions.Where(x => x.IsValidee).OrderByDescending(x => x.Version))
            {
                var full = _projetTherapeutiqueService.Load(v.FilePath);
                if (full != null)
                    ProjetTherapeutiqueBlocks.Add(new ProjetTherapeutiqueBilanCardViewModel(full));
            }

            OnPropertyChanged(nameof(HasNoTimelineCards));
            OnPropertyChanged(nameof(HasProjetTherapeutiqueBlocks));
            RefreshFriseStages();
        }

        private SyntheseGlobaleViewModel? _syntheseGlobaleVM;
        /// <summary>
        /// ViewModel d'édition de la Synthèse Globale (V0.1).
        /// Instancié à l'injection du SyntheseGlobaleService.
        /// </summary>
        public SyntheseGlobaleViewModel? SyntheseGlobaleVM
        {
            get => _syntheseGlobaleVM;
            private set => SetProperty(ref _syntheseGlobaleVM, value);
        }

        private SyntheseGlobaleService? _syntheseGlobaleService;
        private SynthesisWeightTracker? _synthesisWeightTracker;

        /// <summary>
        /// Injecte les services Synthèse Globale (V0.1 + V0.2 + V0.3). Construit le
        /// ViewModel d'édition, branche le rafraîchissement à la fermeture, et abonne
        /// le tracker incrémental pour mettre à jour le badge 🔔.
        /// </summary>
        public void InjectSyntheseGlobaleService(SyntheseGlobaleService service,
                                                 SyntheseGlobaleSuggesterService? suggester = null,
                                                 SynthesisWeightTracker? weightTracker = null,
                                                 SyntheseGlobaleRelectureService? relecteur = null)
        {
            _syntheseGlobaleService  = service;
            _synthesisWeightTracker  = weightTracker;
            SyntheseGlobaleVM = new SyntheseGlobaleViewModel(service, suggester, relecteur);
            SyntheseGlobaleVM.Closed += () =>
            {
                IsSyntheseGlobaleMode = false;
                LoadSyntheseGlobaleCards();   // rafraîchit frise + blocs SYNTHESE
                RefreshSyntheseUpdateBadge();
            };
            SyntheseGlobaleVM.BrouillonCreated += LoadSyntheseGlobaleCards;
            SyntheseGlobaleVM.SyntheseValidated += OnSyntheseValidated;

            if (_synthesisWeightTracker != null)
                _synthesisWeightTracker.WeightUpdated += (_, patient) =>
                {
                    if (_currentPatient != null
                        && string.Equals(patient, _currentPatient.NomComplet, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshSyntheseUpdateBadge);
                    }
                };
        }

        /// <summary>
        /// Réévalue le badge 🔔 en lisant le tracker pour le patient courant.
        /// Appelé : ouverture patient, ajout d'un élément avec poids, validation d'une
        /// nouvelle synthèse (reset → badge éteint).
        /// </summary>
        public void RefreshSyntheseUpdateBadge()
        {
            if (_synthesisWeightTracker == null || _currentPatient == null
                || string.IsNullOrEmpty(_currentPatient.NomComplet))
            {
                SyntheseUpdateRecommandee = false;
                _syntheseUpdateBadgeText = "";
                OnPropertyChanged(nameof(SyntheseUpdateBadge));
                return;
            }
            var (shouldUpdate, weight, items) =
                _synthesisWeightTracker.CheckUpdateNeeded(_currentPatient.NomComplet, 1.0);
            SyntheseUpdateRecommandee = shouldUpdate;
            _syntheseUpdateBadgeText  = shouldUpdate
                ? $"{items.Count} nouvel(s) élément(s) depuis la dernière synthèse (poids {weight:F1}/1.0)"
                : "";
            OnPropertyChanged(nameof(SyntheseUpdateBadge));
        }

        private void OnSyntheseValidated(string patientNomComplet)
        {
            // Reset du tracker à chaque synthèse validée : nouvelle source de vérité.
            if (_synthesisWeightTracker != null && !string.IsNullOrWhiteSpace(patientNomComplet))
                _synthesisWeightTracker.ResetAfterSynthesisUpdate(patientNomComplet);
            RefreshSyntheseUpdateBadge();
        }

        /// <summary>
        /// Ouvre une carte de Synthèse Globale au clic depuis la frise. Charge le fichier
        /// (brouillon → édition libre, validée → lecture seule pour l'instant).
        /// </summary>
        private void OpenSyntheseGlobaleCard(SyntheseGlobaleCardViewModel card)
        {
            if (_syntheseGlobaleService == null || SyntheseGlobaleVM == null || _currentPatient == null) return;
            var full = _syntheseGlobaleService.Load(card.FilePath);
            if (full == null)
            {
                System.Windows.MessageBox.Show("Impossible de charger cette synthèse (fichier introuvable).",
                    "Synthèse Globale",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            Suivi.Reset();
            ConsultationType = ConsultationType.Normal;
            IsEditingConsultation = false;
            ResetWorkspaceModes();

            SyntheseGlobaleVM.Synthese = full;
            SyntheseGlobaleVM.StatusMessage = full.IsValidee
                ? $"Lecture seule : v{full.Version} validée le {full.DateValidation:dd/MM/yyyy}."
                : $"Brouillon v{full.Version} repris.";
            IsSyntheseGlobaleMode = true;
        }

        private void OuvrirSyntheseGlobale()
        {
            if (_syntheseGlobaleService == null || SyntheseGlobaleVM == null)
            {
                System.Windows.MessageBox.Show(
                    "Service Synthèse Globale non initialisé.",
                    "Synthèse Globale",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            if (_currentPatient == null)
            {
                System.Windows.MessageBox.Show(
                    "Sélectionnez d'abord un patient.",
                    "Synthèse Globale",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            Suivi.Reset();
            ConsultationType = ConsultationType.Normal;
            IsEditingConsultation = false;
            ResetWorkspaceModes();

            SyntheseGlobaleVM.OuvrirBrouillonOuCreer(
                _currentPatient.NomComplet,
                psychiatre: "",
                patientDirectoryPath: _currentPatient.DirectoryPath ?? "");
            IsSyntheseGlobaleMode = true;
        }

        /// <summary>
        /// Injecte le dispatcher d'urgences cliniques + le log service (Mode Urgence V0).
        /// S'abonne à SignalDetected pour faire apparaître le chip.
        /// </summary>
        public void InjectUrgenceDispatcher(UrgenceDispatcher dispatcher, UrgenceLogService logService)
        {
            _urgenceDispatcher = dispatcher;
            _urgenceLogService = logService;
            dispatcher.SignalDetected += OnUrgenceSignalDetected;
        }

        private UrgenceChipViewModel? _currentUrgenceChip;
        /// <summary>
        /// Chip d'urgence actuellement affiché en haut de la zone Note. Null = rien à afficher.
        /// </summary>
        public UrgenceChipViewModel? CurrentUrgenceChip
        {
            get => _currentUrgenceChip;
            private set => SetProperty(ref _currentUrgenceChip, value);
        }

        private void OnUrgenceSignalDetected(UrgenceSignal signal)
        {
            // Dispatcher peut fire depuis un thread arrière → marshalling UI obligatoire
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                CurrentUrgenceChip = new UrgenceChipViewModel(
                    signal,
                    onOpenEvaluation: () => OnChipOpenEvaluation(signal),
                    onDismiss:        motif => OnChipDismiss(signal, motif));

                // Le fichier _signal_*.md vient d'être créé → rafraîchir la liste pour le voir apparaître
                _ = RefreshConsultationNotesAsync();
            });
        }

        private UrgenceEvaluationViewModel? _currentUrgenceEvaluation;
        /// <summary>
        /// Évaluation actuellement ouverte dans le panneau Mode Urgence (Étape 4). Null = panneau fermé.
        /// </summary>
        public UrgenceEvaluationViewModel? CurrentUrgenceEvaluation
        {
            get => _currentUrgenceEvaluation;
            private set
            {
                if (SetProperty(ref _currentUrgenceEvaluation, value))
                    OnPropertyChanged(nameof(IsUrgenceEvaluationOpen));
            }
        }
        public bool IsUrgenceEvaluationOpen => CurrentUrgenceEvaluation != null;

        private void OnChipOpenEvaluation(UrgenceSignal signal)
        {
            if (_urgenceLogService == null) return;

            _urgenceLogService.UpdateMedecinAction(signal.SignalFilePath, UrgenceUserAction.OuvertEvaluation);

            var consultLabel = $"Consultation du {ConsultationDate:dd/MM/yyyy}";
            CurrentUrgenceEvaluation = new UrgenceEvaluationViewModel(
                signal,
                _confirmedAge,
                consultLabel,
                medecin: Environment.UserName,
                _urgenceLogService,
                onSaved:  path =>
                {
                    // NB: ne PAS fermer le panneau ici — l'évaluation bascule en mode restitution
                    _ = RefreshConsultationNotesAsync();
                },
                onCancel: ()   => CurrentUrgenceEvaluation = null,
                llmService: _llmService);
        }

        private void OnChipDismiss(UrgenceSignal signal, string motif)
        {
            _urgenceLogService?.UpdateMedecinAction(signal.SignalFilePath, UrgenceUserAction.Ecarte, motif);
        }

        /// <summary>
        /// Déclenche l'analyse d'urgence sur une note fraîchement sauvegardée.
        /// Async fire-and-forget : ne bloque jamais la sauvegarde de la note.
        /// </summary>
        private void TriggerUrgenceAnalysis(string noteFilePath, string noteContent, string consultationType)
        {
            if (_urgenceDispatcher == null) return;
            if (_currentPatient == null) return;
            if (string.IsNullOrWhiteSpace(noteContent)) return;

            var ctx = new UrgenceNoteContext
            {
                PatientNomComplet = _currentPatient.NomComplet,
                PatientAge        = _confirmedAge,
                ConsultationType  = consultationType,
                ConsultationDate  = ConsultationDate,
                NoteContent       = StripYamlHeader(noteContent),
                NoteFilePath      = noteFilePath,
                MotifConsultation = DetectedMotif ?? ""
            };

            _ = Task.Run(async () =>
            {
                try { await _urgenceDispatcher.AnalyzeAsync(ctx); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Urgence] Analyse échouée : {ex.Message}"); }
            });
        }

        private static string StripYamlHeader(string content)
        {
            if (string.IsNullOrEmpty(content) || !content.TrimStart().StartsWith("---")) return content;
            var first  = content.IndexOf("---", StringComparison.Ordinal);
            var second = content.IndexOf("---", first + 3, StringComparison.Ordinal);
            if (second < 0) return content;
            return content.Substring(second + 3).TrimStart('\r', '\n');
        }

        public void InjectServices(ILLMService llmService, StorageService storageService,
                                   WhisperStreamingService? whisperService = null)
        {
            _llmService     = llmService;
            _storageService = storageService;

            if (whisperService != null)
            {
                _whisperService = whisperService;
                _whisperService.StatusChanged         += msg  => Dispatch(() => ExtractionStatus = msg);
                _whisperService.TextAppended          += text => Dispatch(() =>
                {
                    if (IsSuiviMode) Suivi.Transcription += text;
                    else             TranscriptionInput  += text;
                });
                _whisperService.SegmentReady          += seg  => _ = EnqueueExtractionAsync(seg);
                _whisperService.SessionResetRequested += ()   => Dispatch(ResetDictationSession);
                _whisperService.AudioLevelChanged     += lvl  => Dispatch(() => MicLevelPct = Math.Min(100, (int)(lvl * 600)));
                _whisperService.BatchProgressChanged  += pct  => Dispatch(() =>
                {
                    bool wasNearEnd = BatchProgressPct >= 90;
                    BatchProgressPct = pct;
                    if (wasNearEnd && pct < 10)
                        _ = FlashBatchSentAsync();
                });
            }
        }

        /// <summary>
        /// Réinitialise complètement la session de dictée :
        /// - vide la transcription
        /// - vide le segment en attente d'extraction
        /// - réinitialise les blocs (texte + thèmes)
        /// </summary>
        private void ResetDictationSession()
        {
            TranscriptionInput = "";
            _pendingSegment    = "";

            foreach (var block in InterrogatoireBlocks)
                block.Reset();

            // V0b : réinitialiser les détecteurs
            _motifDetector.Reset();
            DetectedMotif = "";
            _isAgeConfirmed = false;
            OnPropertyChanged(nameof(HasConfirmedAge));
            BlockSuggestions.Clear();
            OnPropertyChanged(nameof(HasBlockSuggestions));
            IsStructureFrozen = false;

            // Repasse en mode Saisie si on était dans un autre état
            if (IsInterrogatoireMode)
                InterrogatoireState = InterrogatoireState.Saisie;
        }

        /// <summary>
        /// Accumule les segments et déclenche une extraction incrémentale dès que le LLM est libre.
        /// </summary>
        private async Task EnqueueExtractionAsync(string newSegment)
        {
            _pendingSegment += " " + newSegment;

            if (!await _extractionLock.WaitAsync(0)) return;

            try
            {
                while (!string.IsNullOrWhiteSpace(_pendingSegment))
                {
                    var segment = _pendingSegment.Trim();
                    _pendingSegment = "";
                    await ExtractIncrementalAsync(segment);
                }
            }
            finally
            {
                _extractionLock.Release();
            }
        }

        private async Task ExtractIncrementalAsync(string segment)
        {
            if (_llmService == null || string.IsNullOrWhiteSpace(segment)) return;

            Dispatch(() => ExtractionStatus = "⟳ Extraction en cours...");

            var currentBlocks = InterrogatoireBlocks
                .Select(vm => new ConsultationBlock
                {
                    Key           = vm.Key,
                    Title         = vm.Title,
                    FreeText      = vm.FreeText,
                    ExpectedThemes = new List<string>(vm.ExpectedThemes),
                    CoveredThemes = new List<string>(vm.CoveredThemes)
                }).ToList();

            var (ok, result, err) = await _incrementalExtractor.ExtractAsync(
                _llmService, segment, currentBlocks);

            if (!ok || result == null)
            {
                Dispatch(() => ExtractionStatus = $"Erreur extraction : {err}");
                return;
            }

            Dispatch(() =>
            {
                foreach (var update in result.Updates)
                {
                    var blockVm = InterrogatoireBlocks.FirstOrDefault(b => b.Key == update.BlockKey);
                    if (blockVm == null) continue;

                    if (!string.IsNullOrWhiteSpace(update.AppendText))
                        blockVm.AppendText(update.AppendText);

                    foreach (var theme in update.NewThemes)
                        blockVm.AddTheme(theme);
                }
                UpdateBlockCollections(); // V0c : mettre à jour Active/Completed

                // Confirmation de l'âge via le bloc "age" dédié (thème "age" extrait par le LLM).
                // Tant que ce n'est pas confirmé en consultation, les règles d'auto-masquage
                // basées sur l'âge ne s'appliquent pas (la DOB du profil peut être erronée).
                if (!_isAgeConfirmed)
                {
                    var ageBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "age");
                    if (ageBlock != null && ageBlock.CoveredThemes.Contains("age"))
                    {
                        _isAgeConfirmed = true;

                        // Extraire l'âge numérique mentionné ("10 ans", "âgé de 7 ans", etc.)
                        var ageText = ageBlock.FreeText ?? "";
                        var m = System.Text.RegularExpressions.Regex.Match(
                            ageText, @"\b(\d+)\s*ans?\b",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var ag))
                            _ageFromInterrogatoire = ag;

                        OnPropertyChanged(nameof(HasConfirmedAge));
                        if (_confirmedAge.HasValue) ApplyAutoHideRules(_confirmedAge.Value);
                        System.Diagnostics.Debug.WriteLine($"[Blocks] Âge confirmé via interrogatoire ({_ageFromInterrogatoire} ans mentionnés, {_confirmedAge} ans DDN).");
                    }
                }

                // V0b : vérifier si le motif principal est détecté
                _motifDetector.CheckForMotif(InterrogatoireBlocks);
            });
        }

        private static void Dispatch(Action a)
            => System.Windows.Application.Current?.Dispatcher.Invoke(a);

        // Type de consultation
        private ConsultationType _consultationType = ConsultationType.Normal;
        public ConsultationType ConsultationType
        {
            get => _consultationType;
            set
            {
                if (SetProperty(ref _consultationType, value))
                {
                    OnPropertyChanged(nameof(IsInterrogatoireMode));
                    OnPropertyChanged(nameof(IsSuiviMode));
                    OnPropertyChanged(nameof(IsNormalMode));
                    if (value == ConsultationType.PremiereConsultation)
                        InitInterrogatoireBlocks();
                    // Forcer la notification même si InterrogatoireState n'a pas changé de valeur
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                    OnPropertyChanged(nameof(CanExtract));
                }
            }
        }

        public bool IsInterrogatoireMode => ConsultationType == ConsultationType.PremiereConsultation;
        public bool IsSuiviMode => ConsultationType == ConsultationType.Suivi;
        public bool IsNormalMode => !IsInterrogatoireMode && !IsSuiviMode;

        // Hub : true = une consultation est en cours d'édition (frise réduite en bandeau)
        //       false = vue frise pleine
        private bool _isEditingConsultation;
        public bool IsEditingConsultation
        {
            get => _isEditingConsultation;
            set
            {
                if (SetProperty(ref _isEditingConsultation, value))
                    OnPropertyChanged(nameof(IsNotEditingConsultation));
            }
        }
        public bool IsNotEditingConsultation => !_isEditingConsultation;

        // État interne de l'interrogatoire
        private InterrogatoireState _interrogatoireState = InterrogatoireState.Saisie;
        public InterrogatoireState InterrogatoireState
        {
            get => _interrogatoireState;
            set
            {
                if (SetProperty(ref _interrogatoireState, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                    if (value == InterrogatoireState.FinalNote)
                        PopulateFinalNoteBlocks();
                }
            }
        }

        public bool IsInSaisieMode    => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.Saisie && !IsInClinicalMode && !IsSynthesisMode && !IsInObservationsReviewMode && !IsRestitutionMode && !IsFormulaireMode;
        public bool IsInExtractionMode => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.Extraction && !IsInClinicalMode && !IsSynthesisMode && !IsInObservationsReviewMode && !IsRestitutionMode && !IsFormulaireMode;
        public bool IsInFinalNoteMode  => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.FinalNote && !IsInClinicalMode && !IsSynthesisMode && !IsInObservationsReviewMode && !IsRestitutionMode && !IsFormulaireMode;

        // Transcription
        private string _transcriptionInput = "";
        public string TranscriptionInput
        {
            get => _transcriptionInput;
            set
            {
                if (SetProperty(ref _transcriptionInput, value))
                    OnPropertyChanged(nameof(CanExtract));
            }
        }

        private string _manualNotes = "";
        public string ManualNotes
        {
            get => _manualNotes;
            set
            {
                if (SetProperty(ref _manualNotes, value))
                {
                    OnPropertyChanged(nameof(CanExtract));
                    ScheduleDraftSave();
                }
            }
        }

        public bool CanExtract => IsInSaisieMode
            && (!string.IsNullOrWhiteSpace(TranscriptionInput) || !string.IsNullOrWhiteSpace(ManualNotes))
            && _llmService != null;

        // Mode de dictée (Batch recommandé pour consultation, Streaming en option pour test)
        private bool _useBatchMode = true;
        public bool UseBatchMode
        {
            get => _useBatchMode;
            set => SetProperty(ref _useBatchMode, value);
        }

        // Durée du chunk en mode Batch (60s / 90s / 120s)
        private int _batchDurationSeconds = 90;
        public int BatchDurationSeconds
        {
            get => _batchDurationSeconds;
            set => SetProperty(ref _batchDurationSeconds, value);
        }

        // Enregistrement continu
        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    OnPropertyChanged(nameof(IsNotRecording));
                    OnPropertyChanged(nameof(RecordingStatusColor));
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested);
                }
            }
        }
        public bool IsNotRecording => !_isRecording;

        public string RecordingStatusColor => IsRecording ? "#27AE60" : "#AAAAAA";

        // ── Modèle Whisper sélectionné ────────────────────────────────────────

        private WhisperModelSize _selectedWhisperModel;
        public WhisperModelSize SelectedWhisperModel
        {
            get => _selectedWhisperModel;
            set
            {
                if (SetProperty(ref _selectedWhisperModel, value))
                    OnPropertyChanged(nameof(WhisperModelLabel));
            }
        }
        public string WhisperModelLabel => _selectedWhisperModel == WhisperModelSize.LargeV3 ? "🔊 Large-v3" : "🔊 Medium";

        // ── Indicateurs visuels enregistrement ────────────────────────────────

        private int _micLevelPct;
        public int MicLevelPct
        {
            get => _micLevelPct;
            set => SetProperty(ref _micLevelPct, value);
        }

        private int _batchProgressPct;
        public int BatchProgressPct
        {
            get => _batchProgressPct;
            set => SetProperty(ref _batchProgressPct, value);
        }

        private bool _isBatchSent;
        public bool IsBatchSent
        {
            get => _isBatchSent;
            set => SetProperty(ref _isBatchSent, value);
        }

        private async Task FlashBatchSentAsync()
        {
            IsBatchSent = true;
            await Task.Delay(2000);
            IsBatchSent = false;
        }

        // Blocs interrogatoire
        public ObservableCollection<ConsultationBlockViewModel> InterrogatoireBlocks { get; } = new();

        // V0c : Blocs séparés (actifs vs complétés)
        private ObservableCollection<ConsultationBlockViewModel> _activeBlocks = new();
        public IReadOnlyList<ConsultationBlockViewModel> ActiveBlocks => _activeBlocks;

        private ObservableCollection<ConsultationBlockViewModel> _completedBlocks = new();
        public IReadOnlyList<ConsultationBlockViewModel> CompletedBlocks => _completedBlocks;

        private void InitInterrogatoireBlocks()
        {
            _contextCompletionChecked = false;
            InterrogatoireBlocks.Clear();
            BlockSuggestions.Clear();
            OnPropertyChanged(nameof(HasBlockSuggestions));

            // Tous les blocs sont présents dès le départ (logique « soustraction »).
            // L'âge et le motif permettront un auto-masquage ultérieur (puberté, adolescence…).
            foreach (var d in _blockSetResolver.ResolveWithoutAge())
            {
                var bloc = ConsultationBlockViewModel.FromDefinition(d);
                bloc.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ConsultationBlockViewModel.FreeText))
                        ScheduleDraftSave();
                };
                InterrogatoireBlocks.Add(bloc);
            }

            // Auto-masquage uniquement si l'âge a été CONFIRMÉ en consultation
            // (la DOB du profil patient seule ne suffit pas — elle peut être erronée).
            if (HasConfirmedAge && _confirmedAge.HasValue)
                ApplyAutoHideRules(_confirmedAge.Value);

            _motifDetector.Reset();
            DetectedMotif = "";
            IsStructureFrozen = false;

            InterrogatoireState = InterrogatoireState.Saisie;
            TranscriptionInput = "";
            ManualNotes = "";
            NoteContent = "";
            _noteSaved = false;
            IsInClinicalMode = false;
            IsSynthesisMode = false;

            UpdateBlockCollections();
        }

        // ── Hub de consultations ───────────────────────────────────────────────

        /// <summary>
        /// Helper pour réinitialiser tous les modes d'affichage de l'espace de travail principal.
        /// </summary>
        private void ResetWorkspaceModes()
        {
            IsInClinicalMode = false;
            IsInObservationsReviewMode = false;
            IsSynthesisMode = false;
            IsFormulaireMode = false;
            IsRestitutionMode = false;
            IsRestitutionReviewMode = false;
            IsEvaluationPhaseMode = false;
            IsSyntheseGlobaleMode = false;
            IsProjetTherapeutiqueMode = false;
            IsSelectingRestitutionTypeMode = false;
            IsDossierRestitutionCliniqueMode = false;
            IsReadingPastConsultationMode = false;
            IsReadingPastPremiereConsultationMode = false;
            SelectedPastConsultation = null;
            // Ne touche pas à IsEditingConsultation qui a une sémantique légèrement différente,
            // bien qu'il soit souvent basculé en même temps.
        }

        // ── Brouillon 1ère consultation ───────────────────────────────────────

        /// <summary>Déclenche une sauvegarde différée (2 s) du brouillon en cours.</summary>
        private void ScheduleDraftSave()
        {
            if (!IsInterrogatoireMode || _currentPatient == null) return;
            _draftSaveCts?.Cancel();
            _draftSaveCts = new System.Threading.CancellationTokenSource();
            var cts = _draftSaveCts;
            _ = System.Threading.Tasks.Task.Delay(2000, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                _draftSvc.Save(_currentPatient?.DirectoryPath, BuildDraft());
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        /// <summary>Prend un snapshot de l'état courant pour persistance.</summary>
        private MedCompanion.Models.PremiereConsultationDraft BuildDraft() => new()
        {
            DateConsultation  = ConsultationDate,
            MotifDetecte      = _detectedMotif,
            AgeConfirme       = _confirmedAge,
            IsStructureFrozen = IsStructureFrozen,
            InterrogatoireState = _interrogatoireState.ToString(),
            EtapeActive       = IsInClinicalMode ? "clinical" : IsSynthesisMode ? "synthesis" : "saisie",
            TranscriptionInput    = TranscriptionInput,
            ManualNotes           = ManualNotes,
            NoteContent           = NoteContent,
            ObservationsNarrative = ObservationsNarrative,
            SynthesisContent      = SynthesisContent,
            Blocs = InterrogatoireBlocks.Select(b => new MedCompanion.Models.BlocDraft
            {
                Key          = b.Key,
                FreeText     = b.FreeText,
                CoveredThemes = b.CoveredThemes.ToList(),
                IsHidden     = b.IsHidden,
            }).ToList(),
        };

        /// <summary>
        /// Restaure l'état de la 1ère consultation depuis un brouillon sauvegardé.
        /// Appelé depuis StartNewConsultation quand l'utilisateur choisit "Reprendre".
        /// </summary>
        private void RestoreFromDraft(MedCompanion.Models.PremiereConsultationDraft draft)
        {
            ConsultationDate             = draft.DateConsultation;
            _premiereConsultationFilePath = null;
            _noteSaved                   = false;
            _observationsNoteSaved       = false;
            IsEditingConsultation        = true;
            ExtractionStatus             = "";

            // Crée les blocs vierges (via InitInterrogatoireBlocks), puis on écrase leur contenu
            ConsultationType = ConsultationType.PremiereConsultation;

            // Restaurer le contenu des blocs
            foreach (var bd in draft.Blocs)
            {
                var block = InterrogatoireBlocks.FirstOrDefault(b => b.Key == bd.Key);
                if (block == null) continue;
                block.FreeText      = bd.FreeText;
                block.CoveredThemes = new System.Collections.Generic.List<string>(bd.CoveredThemes);
                block.IsHidden      = bd.IsHidden;
            }

            // Restaurer motif / âge sans repasser par les détecteurs
            if (!string.IsNullOrEmpty(draft.MotifDetecte))
                DetectedMotif = draft.MotifDetecte;
            if (draft.AgeConfirme.HasValue)
            {
                _isAgeConfirmed = true;
                _confirmedAge   = draft.AgeConfirme.Value;
                OnPropertyChanged(nameof(ConfirmedAge));
                OnPropertyChanged(nameof(HasConfirmedAge));
            }
            IsStructureFrozen = draft.IsStructureFrozen;

            // Restaurer textes
            TranscriptionInput    = draft.TranscriptionInput    ?? "";
            ManualNotes           = draft.ManualNotes           ?? "";
            NoteContent           = draft.NoteContent           ?? "";
            ObservationsNarrative = draft.ObservationsNarrative ?? "";
            SynthesisContent      = draft.SynthesisContent      ?? "";

            // Restaurer l'état de l'étape interrogatoire
            if (System.Enum.TryParse<InterrogatoireState>(draft.InterrogatoireState, out var iState))
            {
                _interrogatoireState = iState;
                if (iState == InterrogatoireState.FinalNote)
                    PopulateFinalNoteBlocks();
                OnPropertyChanged(nameof(IsInSaisieMode));
                OnPropertyChanged(nameof(IsInExtractionMode));
                OnPropertyChanged(nameof(IsInFinalNoteMode));
                OnPropertyChanged(nameof(CanExtract));
            }

            // Restaurer l'étape active (clinical / synthesis / saisie)
            switch (draft.EtapeActive)
            {
                case "clinical":
                    if (_clinicalObservations.Cards.Count == 0)
                        InitializeClinicalObservations();
                    ResetWorkspaceModes();
                    IsInClinicalMode = true;
                    break;
                case "synthesis":
                    ResetWorkspaceModes();
                    IsSynthesisMode = true;
                    break;
                // "saisie" → état par défaut après InitInterrogatoireBlocks
            }

            UpdateBlockCollections();
        }

        /// <summary>
        /// Démarre une nouvelle consultation selon le type choisi dans le menu "+".
        /// </summary>
        private void StartNewConsultation(string type)
        {
            switch (type)
            {
                case "premiere":
                    // Si un brouillon existe, proposer de reprendre
                    if (_draftSvc.HasDraft(_currentPatient?.DirectoryPath))
                    {
                        var r = System.Windows.MessageBox.Show(
                            "Une 1ère consultation est en cours pour ce patient.\n\n" +
                            "OUI — Reprendre là où vous en étiez\n" +
                            "NON — Démarrer une NOUVELLE consultation (brouillon supprimé)",
                            "Brouillon en cours",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                        if (r == System.Windows.MessageBoxResult.Yes)
                        {
                            var draft = _draftSvc.Load(_currentPatient?.DirectoryPath);
                            if (draft != null) { RestoreFromDraft(draft); break; }
                        }
                        else
                        {
                            _draftSvc.Delete(_currentPatient?.DirectoryPath);
                        }
                    }
                    _premiereConsultationFilePath = null;
                    NoteContent = "";
                    ObservationsNarrative = "";
                    _noteSaved = false;
                    _observationsNoteSaved = false;
                    ConsultationType = ConsultationType.PremiereConsultation; // déclenche InitInterrogatoireBlocks
                    ConsultationDate = DateTime.Now;
                    IsEditingConsultation = true;
                    ExtractionStatus = "";
                    break;

                case "evaluation":
                    // Bascule en mode "Phase d'évaluation" : sortie des autres modes,
                    // affichage du panneau EvaluationPhaseControl (3 états gérés par sa VM).
                    Suivi.Reset();
                    ConsultationType = ConsultationType.Normal;
                    IsEditingConsultation = false;
                    ResetWorkspaceModes();
                    IsEvaluationPhaseMode = true;
                    // Si le panneau était sur une évaluation clôturée (lecture seule),
                    // on revient au contexte actif pour pouvoir démarrer/reprendre.
                    if (EvaluationPhase?.IsReadOnly == true)
                        EvaluationPhase.ReturnToActiveContext();
                    break;

                case "synthese_globale":
                    // Bascule en mode "Synthèse Globale" : ouvre le brouillon courant
                    // ou crée un nouveau brouillon v(N+1).
                    OuvrirSyntheseGlobale();
                    break;

                case "projet_therapeutique":
                    // V1.0 — Projet Thérapeutique : ouvre le brouillon courant ou crée v(N+1).
                    OuvrirProjetTherapeutique();
                    break;

                case "suivi":
                    // Si une consultation de suivi est déjà en cours, proposer de la reprendre
                    if (HasSuiviInProgress)
                    {
                        var r = System.Windows.MessageBox.Show(
                            "Une consultation de suivi est déjà en cours avec du contenu non sauvegardé.\n\n" +
                            "OUI = Reprendre la consultation en cours\n" +
                            "NON = Démarrer une NOUVELLE consultation (le contenu en cours sera perdu)",
                            "Consultation en cours",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                        if (r == System.Windows.MessageBoxResult.Yes)
                        {
                            ResumeSuivi();
                            return;
                        }
                    }
                    Suivi.Reset();
                    // Reset des autres modes pour éviter toute superposition
                    ConsultationType = ConsultationType.Suivi;
                    ResetWorkspaceModes();
                    ConsultationType = ConsultationType.Suivi;
                    ConsultationDate = DateTime.Now;
                    IsEditingConsultation = true;
                    SuiviStatusMessage = "";
                    break;
            }
        }

        /// <summary>
        /// Détermine s'il y a une consultation en cours avec du contenu non sauvegardé.
        /// </summary>
        private bool HasUnsavedConsultation()
        {
            if (!IsInterrogatoireMode) return false;
            if (_noteSaved) return false;
            // Du contenu existe si transcription saisie ou au moins un bloc rempli
            return !string.IsNullOrWhiteSpace(TranscriptionInput)
                   || InterrogatoireBlocks.Any(b => !string.IsNullOrWhiteSpace(b.FreeText));
        }

        /// <summary>
        /// Ouvre une card d'évaluation depuis la frise.
        /// - Active   : bascule sur le panneau Évaluation en mode reprise à l'étape courante,
        ///              sans toucher au dossier bleu (on respecte le travail en cours).
        /// - Clôturée : bascule sur le panneau Évaluation en lecture seule ET met le dossier bleu
        ///              sur l'onglet SYNTHESE pour afficher la synthèse diagnostique.
        /// </summary>
        private void OpenEvaluationCard(EvaluationCardViewModel card)
        {
            if (EvaluationPhase == null || _evaluationPhaseService == null) return;

            var phase = _evaluationPhaseService.Load(card.FilePath);
            if (phase == null)
            {
                System.Windows.MessageBox.Show(
                    "Impossible de charger cette évaluation (fichier introuvable ou illisible).",
                    "Évaluation",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            ResetWorkspaceModes();
            IsEvaluationPhaseMode = true;
            EvaluationPhase.ShowPhase(phase, readOnly: !phase.IsActive);

            // Évaluation clôturée → on bascule le dossier bleu sur le tab pertinent :
            // - BILANS si la cartographie a du contenu (la chenille est un bilan d'évaluation)
            // - SYNTHESE sinon (uniquement la synthèse diagnostique à montrer)
            if (!phase.IsActive)
            {
                var hasCartographie = phase.CartographieEnfant.IsValidated
                    || phase.CartographieEnfant.Attachement.Score > 0
                    || phase.CartographieEnfant.Psychomotricite.IsRenseigne
                    || phase.CartographieEnfant.Langage.Score > 0
                    || phase.CartographieEnfant.Emotions.Score > 0
                    || phase.CartographieEnfant.Imaginaire.Score > 0
                    || phase.CartographieEnfant.Pensee.Score > 0
                    || phase.CartographieEnfant.Temperament.IsRenseigne
                    || phase.CartographieEnfant.Attention.IsRenseigne;
                ActiveDossierTab = hasCartographie ? DossierTab.Bilans : DossierTab.Synthese;
            }
        }

        /// <summary>
        /// Supprime définitivement une évaluation après confirmation.
        /// Si c'était l'évaluation active, recharge l'état de l'EvaluationPhase pour libérer le slot.
        /// </summary>
        private void DeleteEvaluationCard(EvaluationCardViewModel card)
        {
            if (_evaluationPhaseService == null) return;

            var label = card.IsActive
                ? $"l'évaluation en cours (Étape {(int)card.EtapeCourante})"
                : $"l'évaluation clôturée le {card.DateCloture:dd/MM/yyyy}";

            var r = System.Windows.MessageBox.Show(
                $"Supprimer définitivement {label} ?\n\nCette action est irréversible.",
                "Supprimer l'évaluation",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.Yes) return;

            if (!_evaluationPhaseService.Delete(card.FilePath))
            {
                System.Windows.MessageBox.Show(
                    "Le fichier d'évaluation n'a pas pu être supprimé.",
                    "Erreur",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            // Recharge à la fois les cards de la frise ET les blocs de synthèse du dossier bleu
            // (si on a supprimé une éval clôturée, son bloc disparaît aussi).
            LoadEvaluationCards();

            // Si on a supprimé l'évaluation active, recharger l'état pour que le panneau Évaluation
            // (CanStart / CanResume) reflète la réalité (plus aucune phase active).
            if (card.IsActive && EvaluationPhase != null && CurrentPatient != null)
                EvaluationPhase.SetCurrentPatient(CurrentPatient);
        }

        /// <summary>
        /// Ouvre une consultation passée (lecture). Bascule sur l'onglet Consultations du dossier.
        /// (La carte mentale viendra plus tard.)
        /// </summary>
        private void OpenPastConsultation(ConsultationCardViewModel card)
        {
            ResetWorkspaceModes();
            // Sort du mode édition pour revenir au hub
            IsEditingConsultation = false;

            // Naviguer dans la pagination du dossier vers la note cliquée (sans passer en plein écran)
            if (!string.IsNullOrEmpty(card.FilePath))
            {
                var idx = -1;
                for (int i = 0; i < ConsultationNotes.Count; i++)
                {
                    if (string.Equals(ConsultationNotes[i].FilePath, card.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx >= 0)
                {
                    _singlePageIndex = idx;
                    _spreadIndex     = idx / 2;
                    NotifyPageChange();
                    NotifySpreadChange();

                    SelectedPastConsultation = ConsultationNotes[idx];

                    // Check if this is a first consultation
                    try
                    {
                        var content = File.ReadAllText(card.FilePath, System.Text.Encoding.UTF8);
                        var yamlHeader = ExtractYamlHeader(content) ?? "";
                        var yamlType = ParseYamlField(yamlHeader, "type");
                        if (string.Equals(yamlType, "consultation-premiere", StringComparison.OrdinalIgnoreCase))
                        {
                            var (_, body) = SplitFrontmatter(content);
                            IsReadingPastPremiereConsultationMode = true;
                            PastPremiereStep = 1;
                            LoadPastPremiereConsultationData(yamlHeader, body, card.FilePath);
                        }
                        else
                        {
                            IsReadingPastConsultationMode = true;
                        }
                    }
                    catch
                    {
                        IsReadingPastConsultationMode = true;
                    }
                }
            }

            // Affiche le dossier sur l'onglet Consultations dans le panneau de droite
            ActiveDossierTab = DossierTab.Consultations;
        }

        private static string? ExtractYamlHeader(string raw)
        {
            if (!raw.TrimStart().StartsWith("---")) return null;
            var first = raw.IndexOf("---", StringComparison.Ordinal);
            var second = raw.IndexOf("---", first + 3, StringComparison.Ordinal);
            if (second < 0) return null;
            return raw.Substring(first + 3, second - first - 3);
        }

        private static (string yaml, string body) SplitFrontmatter(string raw)
        {
            if (!raw.TrimStart().StartsWith("---")) return ("", raw);
            var firstEnd = raw.IndexOf('\n');
            if (firstEnd < 0) return ("", raw);
            var secondMarker = raw.IndexOf("---", firstEnd + 1, StringComparison.Ordinal);
            if (secondMarker < 0) return ("", raw);
            var yaml = raw.Substring(firstEnd + 1, secondMarker - firstEnd - 1);
            var body = raw.Substring(secondMarker + 3).TrimStart('\r', '\n');
            return (yaml, body);
        }

        private string? ParseYamlField(string yaml, string key)
        {
            if (string.IsNullOrEmpty(yaml)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(yaml, $@"^\s*{System.Text.RegularExpressions.Regex.Escape(key)}\s*:\s*(.+?)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (!m.Success) return null;
            var val = m.Groups[1].Value.Trim();
            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                val = val.Substring(1, val.Length - 2).Replace("\\\"", "\"");
            return val;
        }

        private double? ParseDoubleField(string yaml, string key)
        {
            var val = ParseYamlField(yaml, key);
            if (val != null && double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            return null;
        }

        private string? ExtractSubBlock(string yaml, string sectionPrefix)
        {
            var lines = yaml.Replace("\r\n", "\n").Split('\n').ToList();
            int start = lines.FindIndex(l => l.TrimEnd() == sectionPrefix || l.TrimEnd().StartsWith(sectionPrefix));
            if (start < 0) return null;
            var sb = new StringBuilder();
            for (int i = start + 1; i < lines.Count; i++)
            {
                if (lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0])) break;
                sb.AppendLine(lines[i]);
            }
            return sb.ToString();
        }

        private string UnindentYamlBlock(string block)
        {
            var lines = block.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line;
                if (trimmed.StartsWith("  "))
                    trimmed = trimmed.Substring(2);
                sb.AppendLine(trimmed);
            }
            return sb.ToString().Trim();
        }

        private void LoadPastPremiereConsultationData(string yaml, string body, string filePath)
        {
            _pastPremiereFilePath = filePath;
            IsEditingPastPremiere = false;

            string interrogatoireText = "";
            string observationsText = "";

            var lines = body.Replace("\r\n", "\n").Split('\n');
            string currentSection = "";
            var sbInt = new StringBuilder();
            var sbObs = new StringBuilder();

            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("## Interrogatoire"))
                {
                    currentSection = "int";
                    continue;
                }
                else if (line.Trim().StartsWith("## Observations cliniques"))
                {
                    currentSection = "obs";
                    continue;
                }
                else if (line.Trim().StartsWith("## "))
                {
                    currentSection = "";
                }

                if (currentSection == "int")
                {
                    sbInt.AppendLine(line);
                }
                else if (currentSection == "obs")
                {
                    sbObs.AppendLine(line);
                }
            }

            interrogatoireText = sbInt.ToString().Trim();
            observationsText = sbObs.ToString().Trim();

            PastInterrogatoireText = interrogatoireText;
            PastObservationsNarrative = observationsText;

            PastInterrogatoireBlocks.Clear();
            PastClinicalCards.Clear();

            bool loadedBlocks = false;
            var blocksBlock = ExtractSubBlock(yaml, "interrogatoire_blocks_json: |");
            if (!string.IsNullOrEmpty(blocksBlock))
            {
                try
                {
                    var cleanJson = UnindentYamlBlock(blocksBlock);
                    var blocksData = System.Text.Json.JsonSerializer.Deserialize<List<PastBlockData>>(cleanJson);
                    if (blocksData != null)
                    {
                        foreach (var b in blocksData)
                        {
                            if (!string.IsNullOrWhiteSpace(b.FreeText))
                            {
                                PastInterrogatoireBlocks.Add(new PastInterrogatoireBlock
                                {
                                    Title = b.Title ?? b.Key,
                                    FreeText = b.FreeText
                                });
                            }
                        }
                        loadedBlocks = PastInterrogatoireBlocks.Count > 0;
                    }
                }
                catch { }
            }

            if (!loadedBlocks && !string.IsNullOrWhiteSpace(interrogatoireText))
            {
                PastInterrogatoireBlocks.Add(new PastInterrogatoireBlock
                {
                    Title = "Notes d'interrogatoire",
                    FreeText = interrogatoireText
                });
            }

            bool loadedObs = false;
            var obsBlock = ExtractSubBlock(yaml, "clinical_observations_json: |");
            if (!string.IsNullOrEmpty(obsBlock))
            {
                try
                {
                    var cleanJson = UnindentYamlBlock(obsBlock);
                    var obsData = System.Text.Json.JsonSerializer.Deserialize<List<PastObsCardData>>(cleanJson);
                    if (obsData != null)
                    {
                        foreach (var c in obsData)
                        {
                            // Compat : nouveau format SelectedOptions[] prioritaire, sinon ancien SelectedOption
                            var selectedOpts = c.SelectedOptions?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                                ?? (string.IsNullOrEmpty(c.SelectedOption)
                                    ? new List<string>()
                                    : new List<string> { c.SelectedOption });
                            PastClinicalCards.Add(new PastClinicalCard
                            {
                                Title = c.Title ?? c.Branch,
                                SelectedOptions = selectedOpts,
                                FreeText = c.FreeText ?? ""
                            });
                        }
                        loadedObs = PastClinicalCards.Count > 0;
                    }
                }
                catch { }
            }

            if (!loadedObs)
            {
                var standardBranches = new[]
                {
                    "Contact/Rapport", "Langage", "Compréhension", "Psychomotricité",
                    "Mimique & Regard", "Profil Cognitif estimé", "Humeur / Anxiété",
                    "Imaginaire / Jeu", "Rapport au cadre", "Vigilance protection"
                };
                foreach (var title in standardBranches)
                {
                    PastClinicalCards.Add(new PastClinicalCard
                    {
                        Title = title,
                        SelectedOptions = new List<string>(),
                        FreeText = ""
                    });
                }
            }

            if (CurrentPatient != null && !string.IsNullOrEmpty(CurrentPatient.DirectoryPath))
            {
                LoadPastSynthesisData(CurrentPatient.DirectoryPath);
            }
        }

        private void LoadPastSynthesisData(string patientDir)
        {
            var synthesePath = Path.Combine(patientDir, "synthese", "synthese.md");
            if (File.Exists(synthesePath))
            {
                try
                {
                    var content = File.ReadAllText(synthesePath, System.Text.Encoding.UTF8);
                    var (yaml, body) = SplitFrontmatter(content);
                    PastSynthesisContent = body.Trim();
                    
                    PastInterrogatoireWeight = ParseDoubleField(yaml, "interrogatoire:") ?? 0.5;
                    PastObservationsWeight = ParseDoubleField(yaml, "observations:") ?? 0.8;
                    PastAverageWeight = ParseDoubleField(yaml, "moyenne:") ?? 0.65;
                    PastLLMJustification = ParseYamlField(yaml, "justification") ?? "";
                }
                catch
                {
                    PastSynthesisContent = "";
                }
            }
        }

        private async System.Threading.Tasks.Task SavePastPremiereAsync()
        {
            if (string.IsNullOrEmpty(_pastPremiereFilePath) || !File.Exists(_pastPremiereFilePath))
            {
                System.Windows.MessageBox.Show("Fichier de consultation introuvable.", "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Lire le fichier original pour récupérer le YAML existant
                var originalContent = File.ReadAllText(_pastPremiereFilePath, System.Text.Encoding.UTF8);
                var origYaml = ExtractYamlHeader(originalContent) ?? "";

                var patientField = ParseYamlField(origYaml, "patient") ?? (CurrentPatient?.NomComplet ?? "");
                var dateField    = ParseYamlField(origYaml, "date")    ?? "";
                var titleField   = ParseYamlField(origYaml, "title")   ?? "1ère consultation";

                // Sérialiser les blocs interrogatoire édités
                var blocksDto = PastInterrogatoireBlocks.Select(b => new { key = b.Title, title = b.Title, freeText = b.FreeText }).ToList();
                var jsonBlocks = System.Text.Json.JsonSerializer.Serialize(blocksDto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var jsonBlocksIndented = string.Join("\n  ", jsonBlocks.Split('\n'));

                // Sérialiser les cartes d'observations éditées
                var obsDto = PastClinicalCards.Select(c => new { title = c.Title, branch = c.Title, selectedOptions = c.SelectedOptions, freeText = c.FreeText }).ToList();
                var jsonObs = System.Text.Json.JsonSerializer.Serialize(obsDto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var jsonObsIndented = string.Join("\n  ", jsonObs.Split('\n'));

                // Reconstruire le body markdown
                var body = new StringBuilder();
                body.AppendLine("## Interrogatoire");
                foreach (var b in PastInterrogatoireBlocks)
                {
                    if (!string.IsNullOrWhiteSpace(b.FreeText))
                    {
                        body.AppendLine($"### {b.Title}");
                        body.AppendLine(b.FreeText);
                        body.AppendLine();
                    }
                }
                body.AppendLine();
                body.AppendLine("## Observations cliniques");
                if (!string.IsNullOrWhiteSpace(PastObservationsNarrative))
                    body.AppendLine(PastObservationsNarrative);

                var newContent = $"---\npatient: \"{patientField}\"\ndate: \"{dateField}\"\ntype: \"consultation-premiere\"\nsource: \"MedCompanion\"\ntitle: \"{titleField}\"\ninterrogatoire_blocks_json: |\n  {jsonBlocksIndented}\nclinical_observations_json: |\n  {jsonObsIndented}\n---\n\n{body.ToString().TrimEnd()}\n";

                await File.WriteAllTextAsync(_pastPremiereFilePath, newContent, System.Text.Encoding.UTF8);

                // Sauvegarder la synthèse si modifiée
                if (CurrentPatient != null && !string.IsNullOrEmpty(PastSynthesisContent))
                {
                    var synthesePath = Path.Combine(CurrentPatient.DirectoryPath, "synthese", "synthese.md");
                    if (File.Exists(synthesePath))
                    {
                        var synContent = File.ReadAllText(synthesePath, System.Text.Encoding.UTF8);
                        var (synYaml, _) = SplitFrontmatter(synContent);
                        var newSynContent = $"---\n{synYaml.Trim()}\n---\n\n{PastSynthesisContent.Trim()}\n";
                        await File.WriteAllTextAsync(synthesePath, newSynContent, System.Text.Encoding.UTF8);
                    }
                }

                IsEditingPastPremiere = false;
                System.Windows.MessageBox.Show("Consultation sauvegardée.", "Enregistrement", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Erreur lors de la sauvegarde : {ex.Message}", "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private class PastBlockData
        {
            public string Key { get; set; } = "";
            public string Title { get; set; } = "";
            public string FreeText { get; set; } = "";
        }

        private class PastObsCardData
        {
            public string Branch { get; set; } = "";
            public string Title { get; set; } = "";
            public List<string>? SelectedOptions { get; set; }   // nouveau format multi-select
            public string? SelectedOption { get; set; }           // ancien format (compat lecture)
            public string FreeText { get; set; } = "";
        }

        private void DeleteConsultationCard(ConsultationCardViewModel card)
        {
            var r = System.Windows.MessageBox.Show(
                $"Supprimer définitivement la consultation/note \"{card.Title}\" ?\n\nCette action est irréversible.",
                "Supprimer la consultation",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                if (File.Exists(card.FilePath))
                {
                    File.Delete(card.FilePath);
                }
                
                // Si la note supprimée est celle qu'on était en train de lire, fermer le mode lecture
                if (SelectedPastConsultation != null && string.Equals(SelectedPastConsultation.FilePath, card.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    ResetWorkspaceModes();
                }

                // Rafraîchir les notes et les cartes
                _ = RefreshConsultationNotesAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Erreur lors de la suppression de la note : {ex.Message}",
                    "Erreur",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void DeleteSelectedPastConsultation()
        {
            if (SelectedPastConsultation == null) return;
            
            var r = System.Windows.MessageBox.Show(
                $"Supprimer définitivement la consultation/note \"{SelectedPastConsultation.Title}\" ?\n\nCette action est irréversible.",
                "Supprimer la consultation",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                var path = SelectedPastConsultation.FilePath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                
                ResetWorkspaceModes();
                _ = RefreshConsultationNotesAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Erreur lors de la suppression de la note : {ex.Message}",
                    "Erreur",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// V0c : Met à jour les collections Active/Completed
        /// appelée après chaque changement de bloc (progression, masquage)
        /// </summary>
        private void UpdateBlockCollections()
        {
            _activeBlocks.Clear();
            _completedBlocks.Clear();

            foreach (var block in InterrogatoireBlocks.Where(b => !b.IsHidden))
            {
                if (block.IsCompleted)
                    _completedBlocks.Add(block);
                else
                    _activeBlocks.Add(block);
            }

            OnPropertyChanged(nameof(ActiveBlocks));
            OnPropertyChanged(nameof(CompletedBlocks));

            // V0c : Geler structure quand age ET motif à 100%
            CheckAndFreezeStructureIfReady();
        }

        /// <summary>
        /// V0c : Gèle la structure dès que les deux blocs clés (age + motif) sont à 100%
        /// plutôt que d'attendre un timer arbitraire.
        /// </summary>
        private void CheckAndFreezeStructureIfReady()
        {
            if (IsStructureFrozen) return;

            var ageBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "age");
            var motifBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "motif");

            if (ageBlock?.IsCompleted == true && motifBlock?.IsCompleted == true)
            {
                IsStructureFrozen = true;
                System.Diagnostics.Debug.WriteLine("[V0c] Structure gelée : age + motif à 100%");
            }
        }

        // ── Gestion de l'âge confirmé ────────────────────────────────────────

        /// <summary>
        /// Appelé quand l'âge du patient est confirmé.
        /// Logique « soustraction » : tous les blocs sont déjà présents — on masque
        /// uniquement ceux dont la tranche d'âge ne correspond pas (puberté, adolescence…).
        /// </summary>
        private void OnAgeConfirmed(int ageYears)
        {
            ApplyAutoHideRules(ageYears);
            System.Diagnostics.Debug.WriteLine($"[Blocks] Âge confirmé ({ageYears}) — règles d'auto-masquage appliquées.");
        }

        // ── Gestion du motif détecté ──────────────────────────────────────────

        /// <summary>
        /// Appelé quand le motif principal est détecté (one-shot).
        /// Logique « soustraction » : tous les blocs sont déjà visibles dès le départ.
        /// On masque automatiquement ceux incompatibles avec l'âge (ex. puberté si &lt; 9 ans).
        /// Le médecin peut toujours réafficher manuellement via le bouton 👁.
        /// </summary>
        private void OnMotifDetected(string motif)
        {
            Dispatch(() =>
            {
                DetectedMotif = motif;

                if (_confirmedAge.HasValue)
                    ApplyAutoHideRules(_confirmedAge.Value);

                BlockSuggestions.Clear();
                OnPropertyChanged(nameof(HasBlockSuggestions));
                IsStructureFrozen = true;
                System.Diagnostics.Debug.WriteLine("[Blocks] Motif détecté, structure gelée.");
            });
        }

        /// <summary>
        /// Masque les blocs incompatibles avec l'âge du patient (règles conservatrices).
        /// </summary>
        private void ApplyAutoHideRules(int ageYears)
        {
            var hideKeys = _blockSetResolver.GetAutoHideKeys(ageYears);
            foreach (var block in InterrogatoireBlocks)
            {
                if (hideKeys.Contains(block.Key) && !block.IsHidden)
                    block.IsHidden = true;
            }
            UpdateBlockCollections();
        }

        // ── V0b : Accept/Dismiss chips ────────────────────────────────────────

        /// <summary>
        /// Accepte un chip : ajoute le bloc et pré-remplit avec le contexte existant.
        /// </summary>
        internal async Task AcceptBlockSuggestionAsync(BlockSuggestion suggestion)
        {
            var definition = _blockSetResolver.GetByKey(suggestion.BlockKey);
            if (definition == null) return;

            suggestion.IsAccepted = true;

            // Ajouter le bloc
            var newBlock = ConsultationBlockViewModel.FromDefinition(definition);
            InterrogatoireBlocks.Add(newBlock);

            // Pré-remplir si un LLM est disponible
            if (_llmService != null)
            {
                ExtractionStatus = $"⟳ Pré-remplissage {definition.Title}...";
                var (ok, prefillText, themes) = await _blockPrefiller.PrefillAsync(
                    _llmService, definition, InterrogatoireBlocks.Where(b => b.Key != definition.Key));

                if (ok && !string.IsNullOrWhiteSpace(prefillText))
                {
                    Dispatch(() =>
                    {
                        newBlock.AppendText(prefillText);
                        foreach (var theme in themes)
                            newBlock.AddTheme(theme);
                        ExtractionStatus = $"✓ {definition.Title} pré-rempli.";
                    });
                }
                else
                {
                    Dispatch(() => ExtractionStatus = $"✓ {definition.Title} ajouté.");
                }
            }

            // Retirer le chip de la liste
            var chipVm = BlockSuggestions.FirstOrDefault(c => c.Suggestion.BlockKey == suggestion.BlockKey);
            if (chipVm != null)
                BlockSuggestions.Remove(chipVm);
            OnPropertyChanged(nameof(HasBlockSuggestions));
        }

        /// <summary>
        /// Rejette un chip : le retire de la liste sans ajouter le bloc.
        /// </summary>
        internal void DismissBlockSuggestion(BlockSuggestion suggestion)
        {
            suggestion.IsDismissed = true;
            var chipVm = BlockSuggestions.FirstOrDefault(c => c.Suggestion.BlockKey == suggestion.BlockKey);
            if (chipVm != null)
                BlockSuggestions.Remove(chipVm);
            OnPropertyChanged(nameof(HasBlockSuggestions));
        }

        private bool _noteSaved = false;

        /// <summary>
        /// Chemin du fichier de la 1ère consultation en cours. Null tant qu'aucune sauvegarde n'a eu lieu.
        /// Réutilisé entre Interrogatoire et Observations pour produire UNE SEULE note avec 2 sections.
        /// </summary>
        private string? _premiereConsultationFilePath;

        /// <summary>
        /// Écrit (ou réécrit) le fichier unique de la 1ère consultation avec les sections
        /// Interrogatoire et Observations actuellement remplies.
        /// </summary>
        private async Task<(bool ok, string? err)> WritePremiereConsultationFileAsync()
        {
            if (CurrentPatient == null || string.IsNullOrEmpty(CurrentPatient.DirectoryPath))
                return (false, "Patient non disponible");

            try
            {
                var notesDir = Path.Combine(CurrentPatient.DirectoryPath,
                    ConsultationDate.Year.ToString(), "notes");
                Directory.CreateDirectory(notesDir);

                // Créer le chemin une seule fois (basé sur ConsultationDate)
                if (string.IsNullOrEmpty(_premiereConsultationFilePath))
                {
                    var stamp = ConsultationDate.ToString("yyyy-MM-dd_HHmm");
                    _premiereConsultationFilePath = Path.Combine(notesDir, $"{stamp}_consultation.md");
                }

                // Construire le contenu unifié
                var body = new System.Text.StringBuilder();
                body.AppendLine($"# 1ère consultation — {ConsultationDate:dd/MM/yyyy}");
                body.AppendLine();

                if (!string.IsNullOrWhiteSpace(NoteContent))
                {
                    body.AppendLine("## Interrogatoire");
                    body.AppendLine();
                    body.AppendLine(NoteContent.Trim());
                    body.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(ObservationsNarrative))
                {
                    body.AppendLine("## Observations cliniques");
                    body.AppendLine();
                    body.AppendLine(ObservationsNarrative.Trim());
                }

                var jsonBlocks = System.Text.Json.JsonSerializer.Serialize(InterrogatoireBlocks.Select(b => new { b.Key, b.FreeText, b.Title }));
                var jsonObs = System.Text.Json.JsonSerializer.Serialize(ClinicalObservations.Cards.Select(c => new
                {
                    Branch = c.Branch.ToString(),
                    c.Title,
                    SelectedOptions = c.OptionItems.Where(o => o.IsSelected).Select(o => o.Label).ToList(),
                    c.FreeText
                }));
                var jsonBlocksIndented = string.Join("\n  ", jsonBlocks.Split('\n'));
                var jsonObsIndented = string.Join("\n  ", jsonObs.Split('\n'));

                var content = $@"---
patient: ""{CurrentPatient.NomComplet}""
date: ""{ConsultationDate:yyyy-MM-ddTHH:mm}""
type: ""consultation-premiere""
source: ""MedCompanion""
title: ""1ère consultation""
interrogatoire_blocks_json: |
  {jsonBlocksIndented}
clinical_observations_json: |
  {jsonObsIndented}
---

{body.ToString().TrimEnd()}
";
                File.WriteAllText(_premiereConsultationFilePath, content, System.Text.Encoding.UTF8);
                await RefreshConsultationNotesAsync();
                RaiseNoteSavedToPatient();
                TriggerUrgenceAnalysis(_premiereConsultationFilePath, content, "consultation-premiere");
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // Extraction
        private string _extractionStatus = "";
        public string ExtractionStatus
        {
            get => _extractionStatus;
            set => SetProperty(ref _extractionStatus, value);
        }

        private async Task ExtractInterrogatoireAsync()
        {
            if (_llmService == null || (string.IsNullOrWhiteSpace(TranscriptionInput) && string.IsNullOrWhiteSpace(ManualNotes)))
                return;

            InterrogatoireState = InterrogatoireState.Extraction;
            ExtractionStatus = "Extraction en cours...";

            var (ok, result, err) = await _extractor.ExtractAsync(_llmService, TranscriptionInput, ManualNotes);

            if (!ok || result == null)
            {
                ExtractionStatus = $"Erreur : {err}";
                InterrogatoireState = InterrogatoireState.Saisie;
                return;
            }

            // Reset complet des blocs avant d'appliquer : la passe LLM sur la transcription
            // complète remplace les extractions incrémentales (évite les doublons).
            foreach (var block in InterrogatoireBlocks)
                block.Reset();

            // Appliquer les mises à jour aux ViewModels des blocs
            foreach (var update in result.Updates)
            {
                var blockVm = InterrogatoireBlocks.FirstOrDefault(b => b.Key == update.BlockKey);
                if (blockVm == null) continue;

                if (!string.IsNullOrWhiteSpace(update.AppendText))
                    blockVm.AppendText(update.AppendText);

                foreach (var theme in update.NewThemes)
                    blockVm.AddTheme(theme);
            }
            UpdateBlockCollections(); // V0c : mettre à jour Active/Completed

            // Construire la note finale
            var blocks = InterrogatoireBlocks.Select(vm => new ConsultationBlock
            {
                Key = vm.Key, Title = vm.Title, FreeText = vm.FreeText,
                ExpectedThemes = vm.ExpectedThemes, CoveredThemes = vm.CoveredThemes
            }).ToList();

            NoteContent = InterrogatoireExtractorService.BuildFinalNote(blocks, ConsultationDate);
            ExtractionStatus = "Extraction terminée.";
            InterrogatoireState = InterrogatoireState.FinalNote;
            ScheduleDraftSave();
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);

            // Déclencher automatiquement l'assistant de complétude du contexte à la fin de l'extraction
            await TriggerContextCompletionAssistantAsync();

            // Passe qualité en arrière-plan (non bloquante)
            _ = RunQualityCheckAsync(NoteContent);
        }

        // ── Contrôle qualité ─────────────────────────────────────────────────

        public ObservableCollection<QualityIssueViewModel> QualityIssues { get; } = new();

        private bool _isRunningQualityCheck;
        public bool IsRunningQualityCheck
        {
            get => _isRunningQualityCheck;
            set { if (SetProperty(ref _isRunningQualityCheck, value)) OnPropertyChanged(nameof(HasQualityIssues)); }
        }

        public bool HasQualityIssues => QualityIssues.Count > 0 || IsRunningQualityCheck;

        private async Task RunQualityCheckAsync(string noteContent)
        {
            if (_llmService == null) return;
            Dispatch(() => { IsRunningQualityCheck = true; QualityIssues.Clear(); });

            var issues = await _qualityChecker.CheckAsync(_llmService, noteContent);

            Dispatch(() =>
            {
                IsRunningQualityCheck = false;
                QualityIssues.Clear();
                foreach (var issue in issues)
                    QualityIssues.Add(new QualityIssueViewModel(issue, this));
                OnPropertyChanged(nameof(HasQualityIssues));
            });
        }

        internal void ApplyQualityIssue(QualityIssue issue)
        {
            // La note finale est désormais éditée par cartouches : on applique la correction
            // directement dans le FreeText du (des) bloc(s) qui contient le texte d'origine,
            // puis on régénère NoteContent pour rester cohérent (contrôle qualité, sauvegarde).
            if (!string.IsNullOrEmpty(issue.Original))
            {
                foreach (var block in InterrogatoireBlocks)
                {
                    if (!string.IsNullOrEmpty(block.FreeText) &&
                        block.FreeText.Contains(issue.Original, System.StringComparison.OrdinalIgnoreCase))
                    {
                        block.FreeText = block.FreeText.Replace(issue.Original, issue.Suggestion,
                            System.StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            RebuildNoteContentFromBlocks();
        }

        internal void DismissQualityIssue(QualityIssueViewModel vm)
        {
            QualityIssues.Remove(vm);
            OnPropertyChanged(nameof(HasQualityIssues));
        }

        // Helper commun pour l'assistant de complétude
        private async Task TriggerContextCompletionAssistantAsync()
        {
            if (_contextCompletionChecked)
                return;

            var age = _confirmedAge ?? CurrentPatient?.Age;
            var needsFullContext = age.HasValue && age.Value >= 3 && age.Value <= 11;

            // Discordance âge : DDN absente OU âge interrogatoire ≠ âge calculé DDN
            var hasDob = !string.IsNullOrEmpty(CurrentPatient?.Dob);
            var ageDiscrepancy = _ageFromInterrogatoire.HasValue
                                 && _ageFromInterrogatoire.Value != (_confirmedAge ?? -999);
            var needsAgeCheck = !hasDob || ageDiscrepancy;

            if (!needsAgeCheck && !needsFullContext)
                return;

            try
            {
                var oldStatus = ExtractionStatus;
                ExtractionStatus = "Vérification de la complétude du contexte patient...";

                MedCompanion.Services.Restitutions.PatientContextDetails prefilled;

                // Extraction LLM complète uniquement pour les 3-11 ans
                if (needsFullContext && _llmService != null)
                {
                    var auditor = new MedCompanion.Services.Restitutions.PatientContextAuditService();
                    prefilled = await auditor.ExtractContextAsync(_llmService, NoteContent);

                    // Fusionner avec les données existantes de patient.json s'il y en a
                    if (_patientIndex != null && CurrentPatient != null)
                    {
                        var adminMeta = _patientIndex.GetMetadata(CurrentPatient.Id);
                        if (adminMeta != null)
                        {
                            if (string.IsNullOrEmpty(prefilled.Ecole) && !string.IsNullOrEmpty(adminMeta.Ecole))
                                prefilled.Ecole = adminMeta.Ecole;
                            if (string.IsNullOrEmpty(prefilled.Classe) && !string.IsNullOrEmpty(adminMeta.Classe))
                                prefilled.Classe = adminMeta.Classe;
                        }
                    }
                }
                else
                {
                    prefilled = new MedCompanion.Services.Restitutions.PatientContextDetails();
                }

                // Renseigner la section vérification d'âge
                prefilled.ShowFullContext    = needsFullContext;
                prefilled.AgeCalcule        = _confirmedAge;
                prefilled.AgeInterrogatoire = _ageFromInterrogatoire;
                prefilled.DateNaissanceActuelle = CurrentPatient?.Dob;
                prefilled.HasAgeDiscrepancy = ageDiscrepancy;
                prefilled.NeedsDobEntry     = !hasDob;

                // Ouvrir la boîte de dialogue WPF modale sur le thread UI principal
                var dialogResult = false;
                MedCompanion.Views.Consultation.ContextCompletionWindow? completionDialog = null;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var activeWindow = System.Windows.Application.Current.MainWindow;
                    completionDialog = new MedCompanion.Views.Consultation.ContextCompletionWindow(prefilled)
                    {
                        Owner = activeWindow
                    };
                    dialogResult = completionDialog.ShowDialog() == true;
                });

                // Indiquer qu'on a fait la vérification (sauvegardé ou ignoré)
                _contextCompletionChecked = true;

                // Si validé et enregistré, appliquer les modifications
                if (dialogResult && completionDialog != null && completionDialog.IsSaved)
                {
                    var result = completionDialog.CompletedDetails;

                    // 0. Mettre à jour la DDN si corrigée
                    if (!string.IsNullOrWhiteSpace(result.DateNaissanceCorrigee) && CurrentPatient != null)
                    {
                        // Normaliser en YYYY-MM-DD
                        string? dobNorm = null;
                        if (DateTime.TryParse(result.DateNaissanceCorrigee,
                                System.Globalization.CultureInfo.CurrentCulture,
                                System.Globalization.DateTimeStyles.None, out var parsedDob))
                            dobNorm = parsedDob.ToString("yyyy-MM-dd");

                        if (dobNorm != null)
                        {
                            // Mettre à jour dans patient.json
                            if (_patientIndex != null)
                            {
                                var adminMeta = _patientIndex.GetMetadata(CurrentPatient.Id);
                                if (adminMeta != null)
                                {
                                    adminMeta.Dob = dobNorm;
                                    _patientIndex.Upsert(adminMeta);
                                }
                            }
                            // Mettre à jour en mémoire
                            CurrentPatient.Dob = dobNorm;
                            var newAge = DateTime.Now.Year - parsedDob.Year;
                            if (DateTime.Now.DayOfYear < parsedDob.DayOfYear) newAge--;
                            ConfirmedAge = newAge;  // passe par le setter → OnAgeConfirmed → ApplyAutoHideRules
                            OnPropertyChanged(nameof(PatientAge));
                        }
                    }

                    // Sections 1-3 : uniquement pour 3-11 ans (données extraites par LLM)
                    if (needsFullContext)
                    {
                        // 1. Enregistrer école, classe et coordonnées dans patient.json (panneau admin)
                        if (_patientIndex != null && CurrentPatient != null)
                        {
                            var adminMeta = _patientIndex.GetMetadata(CurrentPatient.Id);
                            if (adminMeta != null)
                            {
                                adminMeta.Ecole = result.Ecole;
                                adminMeta.Classe = result.Classe;

                                // Coordonnées de l'école (annuaire EN) — n'écraser que si renseigné
                                if (!string.IsNullOrWhiteSpace(result.EcoleLieu))       adminMeta.EcoleCommune    = result.EcoleLieu;
                                if (!string.IsNullOrWhiteSpace(result.EcoleAdresse))    adminMeta.EcoleAdresse    = result.EcoleAdresse;
                                if (!string.IsNullOrWhiteSpace(result.EcoleCodePostal)) adminMeta.EcoleCodePostal = result.EcoleCodePostal;
                                if (!string.IsNullOrWhiteSpace(result.EcoleTelephone))  adminMeta.EcoleTelephone  = result.EcoleTelephone;
                                if (!string.IsNullOrWhiteSpace(result.EcoleEmail))      adminMeta.EcoleEmail      = result.EcoleEmail;
                                if (!string.IsNullOrWhiteSpace(result.EcoleUai))        adminMeta.EcoleUai        = result.EcoleUai;

                                _patientIndex.Upsert(adminMeta);
                                RefreshAdminInfo();   // rafraîchir la page Administratif du dossier
                            }
                        }

                        // 2. Injecter chaque complément vérifié dans SON bloc respectif (fusion LLM).
                        //    On utilise les vraies clés de block_library.json — chaque info atterrit
                        //    dans le bon bloc, même s'il était vide (il réapparaîtra en cartouche).

                        // Famille & contexte social : parents
                        var familleBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "famille");
                        if (familleBlock != null)
                        {
                            var mereText = "";
                            if (!string.IsNullOrEmpty(result.MereNom)) mereText += result.MereNom;
                            if (!string.IsNullOrEmpty(result.MereAge)) mereText += $" ({result.MereAge})";
                            if (!string.IsNullOrEmpty(result.MereJob)) mereText += $", {result.MereJob}";

                            var pereText = "";
                            if (!string.IsNullOrEmpty(result.PereNom)) pereText += result.PereNom;
                            if (!string.IsNullOrEmpty(result.PereAge)) pereText += $" ({result.PereAge})";
                            if (!string.IsNullOrEmpty(result.PereJob)) pereText += $", {result.PereJob}";

                            var familyLines = new System.Collections.Generic.List<string>();
                            if (!string.IsNullOrEmpty(mereText)) familyLines.Add($"- Mère : {mereText}");
                            if (!string.IsNullOrEmpty(pereText)) familyLines.Add($"- Père : {pereText}");

                            if (familyLines.Count > 0)
                            {
                                ExtractionStatus = "Intégration des informations vérifiées (famille)...";
                                await MergeVerifiedFactsIntoBlockAsync(familleBlock, familyLines);
                            }
                        }

                        // Fratrie : bloc dédié
                        var fratrieBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "fratrie");
                        if (fratrieBlock != null && !string.IsNullOrEmpty(result.Fratrie))
                        {
                            ExtractionStatus = "Intégration des informations vérifiées (fratrie)...";
                            await MergeVerifiedFactsIntoBlockAsync(fratrieBlock,
                                new System.Collections.Generic.List<string> { $"- Fratrie : {result.Fratrie}" });
                        }

                        // Développement psychomoteur : marche / langage / propreté
                        var devBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "developpement");
                        if (devBlock != null)
                        {
                            var devLines = new System.Collections.Generic.List<string>();
                            if (!string.IsNullOrEmpty(result.MarcheAge))   devLines.Add($"- Âge de la marche : {result.MarcheAge}");
                            if (!string.IsNullOrEmpty(result.LangageAcq))  devLines.Add($"- Acquisition du langage : {result.LangageAcq}");
                            if (!string.IsNullOrEmpty(result.PropreteAcq)) devLines.Add($"- Statut propreté : {result.PropreteAcq}");

                            if (devLines.Count > 0)
                            {
                                ExtractionStatus = "Intégration des informations vérifiées (développement psychomoteur)...";
                                await MergeVerifiedFactsIntoBlockAsync(devBlock, devLines);
                            }
                        }

                        // Identité : école (+ ville) / classe — c'est ici que vit l'école
                        // (le bloc "identite" attend les thèmes accompagnant/classe/ecole).
                        var identiteBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "identite");
                        if (identiteBlock != null)
                        {
                            var ecoleText = result.Ecole ?? "";
                            if (!string.IsNullOrEmpty(result.EcoleLieu))
                                ecoleText = string.IsNullOrEmpty(ecoleText)
                                    ? result.EcoleLieu
                                    : $"{ecoleText} ({result.EcoleLieu})";

                            var idLines = new System.Collections.Generic.List<string>();
                            if (!string.IsNullOrEmpty(ecoleText))     idLines.Add($"- École : {ecoleText}");
                            if (!string.IsNullOrEmpty(result.Classe)) idLines.Add($"- Classe : {result.Classe}");

                            if (idLines.Count > 0)
                            {
                                ExtractionStatus = "Intégration des informations vérifiées (identité / scolarité)...";
                                await MergeVerifiedFactsIntoBlockAsync(identiteBlock, idLines);
                            }
                        }

                        // 3. Reconstruire la note finale unifiée
                        var blocksList = InterrogatoireBlocks.Select(vm => new ConsultationBlock
                        {
                            Key = vm.Key,
                            Title = vm.Title,
                            FreeText = vm.FreeText,
                            ExpectedThemes = vm.ExpectedThemes,
                            CoveredThemes = vm.CoveredThemes
                        }).ToList();

                        NoteContent = InterrogatoireExtractorService.BuildFinalNote(blocksList, ConsultationDate);
                        UpdateBlockCollections();
                    }

                    // Les compléments (DDN, famille, antécédents…) viennent d'être injectés dans
                    // les blocs APRÈS la prise du snapshot des cartouches : on rafraîchit la liste
                    // pour que ces sections (même celles auparavant vides) apparaissent à la
                    // relecture finale et soient corrigeables par l'utilisateur.
                    PopulateFinalNoteBlocks();
                }
                ExtractionStatus = oldStatus;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PatientContextAssistant] Erreur assistant complétude : {ex.Message}");
            }
        }

        /// <summary>
        /// Fusionne des faits vérifiés par le médecin (pop-up de complétude) dans le texte d'un bloc.
        /// Plutôt que d'empiler un « complément » redondant, on demande au LLM de RÉÉCRIRE proprement
        /// le bloc : intégration des faits (prioritaires), conservation du reste, suppression des
        /// doublons, aucune invention. En cas d'échec LLM, repli sur un ajout simple (info jamais perdue).
        /// </summary>
        private async Task MergeVerifiedFactsIntoBlockAsync(ConsultationBlockViewModel block, List<string> verifiedFacts)
        {
            // Le médecin a fourni des infos pour ce bloc → on le démasque pour qu'il
            // apparaisse en relecture, même s'il avait été auto-masqué ou laissé vide.
            block.IsHidden = false;

            var existing  = (block.FreeText ?? "").Trim();
            var factsText = string.Join("\n", verifiedFacts);

            if (_llmService != null)
            {
                var prompt = $@"Tu es pédopsychiatre. Voici la section « {block.Title} » d'un interrogatoire, extraite automatiquement :

--- TEXTE ACTUEL ---
{(string.IsNullOrEmpty(existing) ? "(vide)" : existing)}
--- FIN ---

Le médecin a vérifié et corrigé les informations suivantes (elles font foi) :
{factsText}

Réécris cette section en un seul texte propre qui :
- intègre les informations vérifiées ci-dessus (elles priment en cas de contradiction) ;
- conserve toutes les autres informations du texte actuel ;
- supprime les redondances et les doublons ;
- n'invente rien qui ne figure pas dans les sources ;
- reste factuel et concis, en français.

Retourne UNIQUEMENT le texte de la section, sans titre ni préambule ni balises de code.";

                try
                {
                    var (ok, raw, _) = await _llmService.GenerateTextAsync(prompt, maxTokens: 800);
                    if (ok && !string.IsNullOrWhiteSpace(raw))
                    {
                        block.FreeText = StripMarkdownFences(raw);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ContextMerge] Échec LLM ({block.Key}) : {ex.Message}");
                }
            }

            // Repli : ajout simple (ancien comportement) pour ne jamais perdre l'info vérifiée.
            block.FreeText = string.IsNullOrEmpty(existing) ? factsText : existing + "\n\n" + factsText;
        }

        private bool _isReformulatingAll;
        /// <summary>Vrai pendant la reformulation globale (désactive le bouton « Reformuler »).</summary>
        public bool IsReformulatingAll
        {
            get => _isReformulatingAll;
            private set => SetProperty(ref _isReformulatingAll, value);
        }

        /// <summary>
        /// Reformule TOUTES les sections remplies avant sauvegarde (un seul bouton).
        /// Traite chaque bloc indépendamment pour préserver la structure et éviter toute
        /// contamination entre sections. Le texte reste éditable après coup.
        /// </summary>
        private async Task ReformulateAllAsync()
        {
            if (IsReformulatingAll || _llmService == null) return;

            IsReformulatingAll = true;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            try
            {
                foreach (var block in FinalNoteBlocks.ToList())
                {
                    if (string.IsNullOrWhiteSpace(block.FreeText)) continue;
                    await ReformulateBlockAsync(block);
                }
                ExtractionStatus = "✓ Reformulation terminée — relisez avant de sauvegarder.";
            }
            finally
            {
                IsReformulatingAll = false;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>
        /// Reformule un bloc via le LLM : correction orthographe/grammaire/ponctuation et mise au
        /// propre, SANS modifier le sens ni inventer/supprimer d'information clinique. Le texte reste éditable.
        /// </summary>
        private async Task ReformulateBlockAsync(ConsultationBlockViewModel? block)
        {
            if (block == null || block.IsBusy || _llmService == null) return;

            var text = (block.FreeText ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            block.IsBusy = true;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            var previousStatus = ExtractionStatus;
            ExtractionStatus = $"Reformulation : {block.Title}...";

            try
            {
                var prompt = $@"Tu es secrétaire médicale en pédopsychiatrie. Corrige et mets au propre le texte ci-dessous :
- corrige l'orthographe, la grammaire, la ponctuation et la casse ;
- améliore légèrement la lisibilité si nécessaire, sans changer le sens ;
- NE MODIFIE PAS, n'ajoute, ne supprime et n'invente AUCUNE information clinique ;
- conserve les chiffres, noms propres, dates, abréviations médicales et la structure (puces, retours à la ligne).

Retourne UNIQUEMENT le texte corrigé, sans préambule ni balises de code.

Texte :
{text}";

                var (ok, raw, _) = await _llmService.GenerateTextAsync(prompt, maxTokens: 1000);
                if (ok && !string.IsNullOrWhiteSpace(raw))
                    block.FreeText = StripMarkdownFences(raw);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reformulate] échec ({block.Key}) : {ex.Message}");
            }
            finally
            {
                block.IsBusy = false;
                ExtractionStatus = previousStatus;
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Retire les éventuelles balises ```…``` autour d'une réponse LLM.</summary>
        private static string StripMarkdownFences(string raw)
        {
            var text = raw.Trim();
            if (!text.StartsWith("```")) return text;

            var lines = text.Split('\n').ToList();
            if (lines.Count > 0) lines.RemoveAt(0);                                   // ouverture ```
            if (lines.Count > 0 && lines[^1].Trim() == "```") lines.RemoveAt(lines.Count - 1); // fermeture ```
            return string.Join("\n", lines).Trim();
        }

        /// <summary>
        /// Génère le « Formulaire de complétion » pré-rempli à remettre aux parents
        /// (bouton après l'Interrogatoire). HTML → Edge headless → PDF, puis ouverture.
        /// </summary>
        private async Task GenerateFormulaireParentsAsync()
        {
            if (_patientIndex == null || CurrentPatient == null || string.IsNullOrEmpty(CurrentPatient.DirectoryPath))
            {
                ExtractionStatus = "Aucun patient sélectionné.";
                return;
            }

            var meta = _patientIndex.GetMetadata(CurrentPatient.Id);
            if (meta == null)
            {
                ExtractionStatus = "Métadonnées patient introuvables.";
                return;
            }

            var svc = new MedCompanion.Services.FormulaireCompletionService();
            if (!svc.TemplateExists)
            {
                ExtractionStatus = "Template du formulaire non installé (Resources/Formulaires/formulaire_completion.html).";
                return;
            }

            // Extraire les prénoms des parents depuis le bloc famille de l'interrogatoire
            string? perePrenom = null;
            string? merePrenom = null;

            var familleBloc = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "famille");
            var familleText = familleBloc?.FreeText?.Trim();

            if (!string.IsNullOrWhiteSpace(familleText) && _llmService != null)
            {
                FormulaireStatusMessage = "⏳ Extraction des prénoms parents...";

                var prompt =
                    $"Voici le texte du bloc Famille d'un interrogatoire pédopsychiatrique :\n\n{familleText}\n\n" +
                    "Extrait uniquement le prénom du père et le prénom de la mère. " +
                    "Réponds UNIQUEMENT avec ce format JSON strict, sans rien d'autre :\n" +
                    "{\"pere_prenom\": \"Prénom\", \"mere_prenom\": \"Prénom\"}\n" +
                    "Si un prénom est inconnu ou absent, utilise une chaîne vide.";

                var (llmOk, raw, _) = await _llmService.GenerateTextAsync(prompt, maxTokens: 100);
                if (llmOk && !string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        var start = raw.IndexOf('{');
                        var end   = raw.LastIndexOf('}');
                        if (start >= 0 && end > start)
                        {
                            var json = raw[start..(end + 1)];
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            perePrenom = doc.RootElement.GetProperty("pere_prenom").GetString()?.Trim();
                            merePrenom = doc.RootElement.GetProperty("mere_prenom").GetString()?.Trim();
                        }
                    }
                    catch { /* extraction best-effort, on laisse les prénoms vides */ }
                }
            }

            // Formulaire vierge à remettre aux parents : généré en temp, pas dans le dossier patient
            var outputDir = Path.Combine(Path.GetTempPath(), "MedCompanion", "formulaires");

            FormulaireStatusMessage = "⏳ Génération du formulaire...";
            var (ok, pdfPath, error) = await svc.GenerateAsync(meta, outputDir, perePrenom, merePrenom);

            if (ok && !string.IsNullOrEmpty(pdfPath))
            {
                FormulaireStatusMessage = "✅ Formulaire généré.";
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true });
                }
                catch { /* ouverture best-effort */ }
                // Refermer le panneau après un court délai
                await Task.Delay(1200);
                IsFormulaireMode = false;
                FormulaireStatusMessage = "";
            }
            else
            {
                FormulaireStatusMessage = $"❌ {error}";
            }
        }

        /// <summary>
        /// Envoie l'image du formulaire au LLM (vision) pour extraction OCR des champs remplis.
        /// Même flux que la photo portrait : OpenFileDialog ou Camera Roll.
        /// </summary>
        private async Task ScanFormulaireAsync(string imagePath)
        {
            if (_llmService == null)
            {
                FormulaireStatusMessage = "❌ Service LLM non disponible.";
                return;
            }

            IsFormulaireOcrProcessing = true;
            FormulaireStatusMessage   = "⏳ Analyse du formulaire en cours...";
            FormulaireOcrResult       = "";

            try
            {
                var imageBytes = await File.ReadAllBytesAsync(imagePath);

                const string prompt =
                    "Tu es assistant médical. Voici une photo du formulaire de complétion de première consultation " +
                    "pédopsychiatrique, rempli à la main par les parents.\n\n" +
                    "Ce formulaire comporte exactement 7 blocs numérotés. Pour chaque bloc, lis ce qui est écrit " +
                    "(manuscrit) ou coché, et restitue uniquement les informations REMPLIES. " +
                    "Ignore les cases vides ou non cochées. Si un mot est illisible, écris [illisible].\n\n" +
                    "BLOC 1 — COORDONNÉES DU PÈRE\n" +
                    "  Champs : Prénom, Nom de famille, Téléphone portable, Email\n\n" +
                    "BLOC 2 — COORDONNÉES DE LA MÈRE\n" +
                    "  Champs : Prénom, Nom de famille, Téléphone portable, Email\n\n" +
                    "BLOC 3 — ADRESSE DU MILIEU PRINCIPAL DE VIE\n" +
                    "  Champs : Adresse (rue, code postal, ville)\n\n" +
                    "BLOC 4 — SITUATION FAMILIALE\n" +
                    "  Cases à cocher : Parents ensemble / Parents séparés / Divorcés / Garde alternée / " +
                    "Famille recomposée / Autre\n" +
                    "  Mode de garde principal : Parents / Mère / Père / Autre\n\n" +
                    "BLOC 5 — ANTÉCÉDENTS FAMILIAUX (dans la famille proche)\n" +
                    "  Tableau avec colonnes OUI / NON / Ne sait pas pour chaque ligne :\n" +
                    "  TDAH / Troubles de l'attention, Dyslexie / Troubles des apprentissages, " +
                    "Troubles du spectre de l'autisme (TSA), Troubles anxieux, Dépression, " +
                    "Bipolarité, Addictions (alcool, etc.), Tentative de suicide\n\n" +
                    "BLOC 6 — PHOTO DE L'ENFANT\n" +
                    "  (zone photo collée par les parents, pas de texte à extraire)\n\n" +
                    "BLOC 7 — AUTORISATIONS ET COMMUNICATION\n" +
                    "  Trois autorisations avec cases OUI / NON à cocher par les parents.\n" +
                    "  Indique quelle case est cochée pour chacune.\n\n" +
                    "Présente le résultat bloc par bloc, en ne mentionnant que ce qui est effectivement rempli. " +
                    "Réponds en français.";

                var (ok, result, error) = await _llmService.AnalyzeImageAsync(prompt, imageBytes);

                if (ok && !string.IsNullOrWhiteSpace(result))
                {
                    FormulaireOcrResult   = result;
                    FormulaireStatusMessage = "✅ Formulaire analysé.";
                    OnPropertyChanged(nameof(HasFormulaireOcrResult));
                }
                else
                {
                    FormulaireStatusMessage = $"❌ Analyse impossible : {error ?? "réponse vide"}";
                }
            }
            catch (Exception ex)
            {
                FormulaireStatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsFormulaireOcrProcessing = false;
            }
        }

        private async Task SaveInterrogatoireNoteAsync()
        {
            if (CurrentPatient == null) return;

            // Déclencher l'assistant de complétude AVANT de figer la note : il peut encore
            // injecter des compléments (DDN, famille, antécédents) dans les blocs.
            // (no-op si déjà effectué à l'extraction.)
            await TriggerContextCompletionAssistantAsync();

            // Les cartouches éditables sont la source de vérité : on régénère la note APRÈS
            // l'assistant, pour que les compléments injectés soient bien écrits dans le .md.
            RebuildNoteContentFromBlocks();

            if (string.IsNullOrWhiteSpace(NoteContent))
                return;

            var (ok, err) = await WritePremiereConsultationFileAsync();
            if (ok)
            {
                _noteSaved = true;
                ExtractionStatus = "Note sauvegardée dans le dossier patient.";
                // Sortie du mode édition → retour à la frise (la nouvelle carte y apparaît)
                IsEditingConsultation = false;
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested);
            }
            else
            {
                ExtractionStatus = $"Erreur sauvegarde : {err}";
            }
        }

        #endregion

        #region Observations Cliniques V0c

        private ClinicalObservationsSession _clinicalObservations = new();
        public ClinicalObservationsSession ClinicalObservations
        {
            get => _clinicalObservations;
            set => SetProperty(ref _clinicalObservations, value);
        }

        private bool _isInClinicalMode = false;
        public bool IsInClinicalMode
        {
            get => _isInClinicalMode;
            set
            {
                if (SetProperty(ref _isInClinicalMode, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                    // Génère les suggestions IA au 1er passage en Étape 2
                    if (value && !_suggestionsGenerated && !string.IsNullOrWhiteSpace(NoteContent))
                        _ = GenerateObservationSuggestionsAsync();
                }
            }
        }

        // ── Mode Formulaire parents (choix Générer / Scanner OCR) ────────────

        private bool _isFormulaireMode;
        public bool IsFormulaireMode
        {
            get => _isFormulaireMode;
            set
            {
                if (SetProperty(ref _isFormulaireMode, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                }
            }
        }

        private bool _isFormulaireOcrProcessing;
        public bool IsFormulaireOcrProcessing
        {
            get => _isFormulaireOcrProcessing;
            set => SetProperty(ref _isFormulaireOcrProcessing, value);
        }

        private string _formulaireStatusMessage = "";
        public string FormulaireStatusMessage
        {
            get => _formulaireStatusMessage;
            set => SetProperty(ref _formulaireStatusMessage, value);
        }

        private string _formulaireOcrResult = "";
        public string FormulaireOcrResult
        {
            get => _formulaireOcrResult;
            set => SetProperty(ref _formulaireOcrResult, value);
        }

        public bool HasFormulaireOcrResult => !string.IsNullOrWhiteSpace(_formulaireOcrResult);

        private bool _suggestionsGenerated = false;

        private bool _isGeneratingSuggestions;
        public bool IsGeneratingSuggestions
        {
            get => _isGeneratingSuggestions;
            set => SetProperty(ref _isGeneratingSuggestions, value);
        }

        private string _suggestionsStatus = "";
        public string SuggestionsStatus
        {
            get => _suggestionsStatus;
            set => SetProperty(ref _suggestionsStatus, value);
        }

        public ICommand GenerateSuggestionsCommand { get; private set; } = null!;

        /// <summary>
        /// Initialise les 10 cartes d'observation clinique avec leurs options génériques (fallback).
        /// Les suggestions IA seront générées dès l'entrée en Étape 2.
        /// </summary>
        private void InitializeClinicalObservations()
        {
            _clinicalObservations.Cards.Clear();
            _clinicalObservations.CreatedAt = DateTime.Now;
            _suggestionsGenerated = false;
            SuggestionsStatus = "";

            AddClinicalCard(ClinicalObservationBranch.Contact, "Contact/Rapport",
                new[] { "Bon", "Distant", "Fuyant", "Adhésif", "Instable" });

            AddClinicalCard(ClinicalObservationBranch.Langage, "Langage",
                new[] { "Adapté", "Riche", "Pauvre/Immaturité", "Inexistant" });

            AddClinicalCard(ClinicalObservationBranch.Comprehension, "Compréhension",
                new[] { "Adaptée", "Limitée", "Consignes simples uniquement" });

            AddClinicalCard(ClinicalObservationBranch.Psychomotricite, "Psychomotricité",
                new[] { "Harmonieuse", "Instabilité motrice", "Inhibition", "Maladresse" });

            AddClinicalCard(ClinicalObservationBranch.MimiquRegard, "Mimique & Regard",
                new[] { "Expressive", "Faciès figé", "Regard fuyant", "Pauvreté des mimiques" });

            AddClinicalCard(ClinicalObservationBranch.ProfilCognitif, "Profil Cognitif estimé",
                new[] { "Harmonieux", "Dysharmonieux", "Supérieur", "Retard suspecté" });

            AddClinicalCard(ClinicalObservationBranch.HumeurAnxiete, "Humeur / Anxiété",
                new[] { "Stable", "Triste", "Irritable", "Angoissé" });

            AddClinicalCard(ClinicalObservationBranch.ImaginaireJeu, "Imaginaire / Jeu",
                new[] { "Riche", "Pauvre", "Bizarre", "Stéréotypé" });

            AddClinicalCard(ClinicalObservationBranch.RapportCadre, "Rapport au cadre",
                new[] { "Respecté", "Opposition", "Désinhibé", "Passif" });

            AddClinicalCard(ClinicalObservationBranch.Vigilance, "Vigilance protection",
                new[] { "Pas de signe inquiétant", "Signes de négligence suspectés", "Suspicion maltraitance physique", "Suspicion maltraitance psychologique" });

            OnPropertyChanged(nameof(ClinicalObservations));
        }

        private void AddClinicalCard(ClinicalObservationBranch branch, string title, string[] options)
        {
            var card = new ClinicalObservationCard { Branch = branch, Title = title };
            card.SetOptions(options);
            _clinicalObservations.Cards.Add(card);
        }

        public void SelectObservationOption(ClinicalObservationCard card, string option)
        {
            // Compat : coche la première option correspondante dans OptionItems
            var item = card.OptionItems.FirstOrDefault(o => o.Label == option);
            if (item != null) item.IsSelected = !item.IsSelected;
            OnPropertyChanged(nameof(ClinicalObservations));
        }

        public void ToggleCardExpand(ClinicalObservationCard card)
        {
            card.IsExpanded = !card.IsExpanded;
            OnPropertyChanged(nameof(ClinicalObservations));
        }

        public async Task TerminateClinicalObservationsAsync()
        {
            if (_llmService == null)
            {
                // Fallback sans LLM : enchaîne les options brutes
                ObservationsNarrative = BuildFallbackNarrative(_clinicalObservations);
            }
            else
            {
                ObservationsStatusMessage = "⏳ Génération de la rédaction clinique...";
                var narrative = await GenerateClinicalNarrativeAsync(_clinicalObservations);
                ObservationsNarrative = string.IsNullOrWhiteSpace(narrative)
                    ? BuildFallbackNarrative(_clinicalObservations)
                    : narrative;
                ObservationsStatusMessage = "Vérifiez/modifiez puis cliquez Valider.";
            }

            _clinicalObservations.GeneratedClinicalNarrative = ObservationsNarrative;
            OnPropertyChanged(nameof(ClinicalObservations));

            // Bascule vers la zone de relecture/édition
            IsInClinicalMode = false;
            IsInObservationsReviewMode = true;
            _observationsNoteSaved = false;
            ScheduleDraftSave();
        }

        private static string BuildFallbackNarrative(ClinicalObservationsSession obs)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var card in obs.Cards)
            {
                if (!card.HasAnySelection) continue;
                sb.Append("• ").Append(card.Title).Append(" : ").Append(card.SelectedOptionsText);
                if (!string.IsNullOrWhiteSpace(card.FreeText))
                    sb.Append(" (").Append(card.FreeText.Trim()).Append(')');
                sb.AppendLine();
            }
            var result = sb.ToString().TrimEnd();
            return string.IsNullOrWhiteSpace(result)
                ? "Aucune observation clinique renseignée pour cette consultation."
                : result;
        }

        // ── Relecture/édition rédaction Observations ────────────────────────

        private bool _isInObservationsReviewMode;
        public bool IsInObservationsReviewMode
        {
            get => _isInObservationsReviewMode;
            set
            {
                if (SetProperty(ref _isInObservationsReviewMode, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                }
            }
        }

        private string _observationsNarrative = "";
        public string ObservationsNarrative
        {
            get => _observationsNarrative;
            set => SetProperty(ref _observationsNarrative, value);
        }

        private string _observationsStatusMessage = "";
        public string ObservationsStatusMessage
        {
            get => _observationsStatusMessage;
            set => SetProperty(ref _observationsStatusMessage, value);
        }

        private bool _observationsNoteSaved;

        public async Task SaveObservationsNoteAsync()
        {
            if (CurrentPatient == null ||
                string.IsNullOrWhiteSpace(ObservationsNarrative))
            {
                ObservationsStatusMessage = "❌ Patient ou contenu manquant.";
                return;
            }

            // Réécrit le fichier UNIQUE de la 1ère consultation (avec Interrogatoire + Observations).
            var (ok, err) = await WritePremiereConsultationFileAsync();

            if (ok)
            {
                _observationsNoteSaved = true;
                ObservationsStatusMessage = "✅ Observations ajoutées à la note de 1ère consultation.";
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested);
            }
            else
            {
                ObservationsStatusMessage = $"❌ Erreur sauvegarde : {err}";
            }
        }

        public void BackToObservationsCards()
        {
            IsInObservationsReviewMode = false;
            IsInClinicalMode = true;
        }

        /// <summary>
        /// Génère des suggestions d'observation contextuelles (LLM) à partir de l'interrogatoire.
        /// Remplace les options génériques par des items pertinents au tableau clinique.
        /// </summary>
        private async Task GenerateObservationSuggestionsAsync()
        {
            if (_llmService == null || string.IsNullOrWhiteSpace(NoteContent))
            {
                SuggestionsStatus = "";
                return;
            }

            IsGeneratingSuggestions = true;
            SuggestionsStatus = "⏳ Génération des suggestions en cours…";

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Tu es un clinicien en pédopsychiatrie. Lis l'interrogatoire ci-dessous pour comprendre le tableau clinique global (âge, motif, antécédents), puis propose pour chaque axe d'observation 3 à 4 qualificatifs adaptés au profil probable de cet enfant.");
                sb.AppendLine();
                sb.AppendLine("RÈGLES IMPÉRATIVES :");
                sb.AppendLine("✅ Chaque item = qualificatif qui décrit la QUALITÉ ou l'INTENSITÉ d'une observation clinique (ex: \"Bon contact\", \"Contact médiocre\", \"Retrait relationnel\", \"Contact adhésif\").");
                sb.AppendLine("✅ 3 à 4 items par axe, du plus favorable au moins favorable ou du plus fréquent au plus spécifique.");
                sb.AppendLine("❌ NE JAMAIS reprendre ou paraphraser un élément de l'interrogatoire (ex: \"Réaction à l'interrogatoire\" ou \"Établissement du lien\" sont INTERDITS).");
                sb.AppendLine("❌ NE PAS nommer une activité, une situation ou une observation faite à l'interrogatoire.");
                sb.AppendLine("❌ Pas de diagnostic, pas d'interprétation.");
                sb.AppendLine();
                sb.AppendLine("EXEMPLES DE FORMAT ATTENDU :");
                sb.AppendLine("Contact → [\"Bon contact\", \"Contact médiocre\", \"Retrait relationnel\", \"Contact adhésif\"]");
                sb.AppendLine("Langage → [\"Adapté à l'âge\", \"Langage pauvre\", \"Mutisme relatif\", \"Logorrhée\"]");
                sb.AppendLine("Psychomotricite → [\"Motricité harmonieuse\", \"Instabilité motrice\", \"Inhibition motrice\", \"Agitation constante\"]");
                sb.AppendLine();
                sb.AppendLine("⚠ RÈGLE SPÉCIALE — axe \"Vigilance\" :");
                sb.AppendLine("Cet axe ne concerne PAS l'état d'éveil ou d'attention de l'enfant.");
                sb.AppendLine("Il s'agit de la VIGILANCE DE PROTECTION DU CLINICIEN : y a-t-il des signes évocateurs de maltraitance physique, psychologique, négligence ou autre danger pour l'enfant ?");
                sb.AppendLine("Vigilance → TOUJOURS utiliser exactement : [\"Pas de signe inquiétant\", \"Signes de négligence suspectés\", \"Suspicion maltraitance physique\", \"Suspicion maltraitance psychologique\"]");
                sb.AppendLine();
                sb.AppendLine("INTERROGATOIRE (contexte clinique uniquement) :");
                sb.AppendLine(NoteContent.Trim());
                sb.AppendLine();
                sb.AppendLine("Réponds UNIQUEMENT en JSON valide, sans markdown ni commentaire :");
                sb.AppendLine("{\"Contact\":[],\"Langage\":[],\"Comprehension\":[],\"Psychomotricite\":[],\"MimiquRegard\":[],\"ProfilCognitif\":[],\"HumeurAnxiete\":[],\"ImaginaireJeu\":[],\"RapportCadre\":[],\"Vigilance\":[]}");

                var (ok, json, _) = await _llmService.ChatAsync(sb.ToString(), new(), maxTokens: 1200);
                if (!ok || string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("LLM non disponible");

                // Extrait le JSON brut (ignore texte éventuel avant/après)
                var start = json.IndexOf('{');
                var end   = json.LastIndexOf('}');
                if (start < 0 || end <= start) throw new FormatException("JSON introuvable");
                var cleanJson = json[start..(end + 1)];

                using var doc = System.Text.Json.JsonDocument.Parse(cleanJson);

                var branchMap = new Dictionary<string, Models.ClinicalObservationBranch>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Contact"]          = Models.ClinicalObservationBranch.Contact,
                    ["Langage"]          = Models.ClinicalObservationBranch.Langage,
                    ["Comprehension"]    = Models.ClinicalObservationBranch.Comprehension,
                    ["Psychomotricite"]  = Models.ClinicalObservationBranch.Psychomotricite,
                    ["MimiquRegard"]     = Models.ClinicalObservationBranch.MimiquRegard,
                    ["ProfilCognitif"]   = Models.ClinicalObservationBranch.ProfilCognitif,
                    ["HumeurAnxiete"]    = Models.ClinicalObservationBranch.HumeurAnxiete,
                    ["ImaginaireJeu"]    = Models.ClinicalObservationBranch.ImaginaireJeu,
                    ["RapportCadre"]     = Models.ClinicalObservationBranch.RapportCadre,
                    ["Vigilance"]        = Models.ClinicalObservationBranch.Vigilance,
                };

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!branchMap.TryGetValue(prop.Name, out var branch)) continue;
                    var card = ClinicalObservations.Cards.FirstOrDefault(c => c.Branch == branch);
                    if (card == null) continue;

                    var suggestions = prop.Value.EnumerateArray()
                        .Select(e => e.GetString() ?? "")
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Take(4)
                        .ToList();

                    if (suggestions.Count > 0)
                        card.SetOptions(suggestions);
                }

                _suggestionsGenerated = true;
                SuggestionsStatus = "✨ Suggestions IA — basées sur l'interrogatoire";
            }
            catch
            {
                SuggestionsStatus = "ℹ️ Options génériques (LLM indisponible ou interrogatoire vide)";
            }
            finally
            {
                IsGeneratingSuggestions = false;
            }
        }

        private async Task<string> GenerateClinicalNarrativeAsync(ClinicalObservationsSession obs)
        {
            if (_llmService == null) return "";
            if (!obs.Cards.Any(c => c.HasAnySelection)) return "";

            var prompt = new System.Text.StringBuilder();
            prompt.AppendLine("Rédige un paragraphe descriptif d'observations cliniques pédopsychiatriques à partir des éléments ci-dessous.");
            prompt.AppendLine();
            prompt.AppendLine("RÈGLES STRICTES — À RESPECTER ABSOLUMENT :");
            prompt.AppendLine("❌ AUCUN diagnostic, AUCUNE hypothèse diagnostique, AUCUNE étiquette pathologique.");
            prompt.AppendLine("❌ AUCUNE suggestion de prise en charge, d'examen complémentaire, de bilan, de traitement.");
            prompt.AppendLine("❌ AUCUNE recommandation, AUCUN \"justifiant\", AUCUN \"évoquant\", AUCUN \"pouvant suggérer\".");
            prompt.AppendLine("❌ NE PAS interpréter, NE PAS inférer, NE PAS conclure.");
            prompt.AppendLine("✅ UNIQUEMENT décrire factuellement ce qui a été observé pendant l'entretien.");
            prompt.AppendLine("✅ Style médical neutre, télégraphique, à l'imparfait/présent descriptif.");
            prompt.AppendLine("✅ Un seul paragraphe, 4-8 phrases maximum.");
            prompt.AppendLine();
            prompt.AppendLine("ÉLÉMENTS OBSERVÉS :");
            foreach (var card in obs.Cards)
            {
                if (!card.HasAnySelection) continue;
                prompt.Append("- ").Append(card.Title).Append(" : ").Append(card.SelectedOptionsText);
                if (!string.IsNullOrWhiteSpace(card.FreeText))
                    prompt.Append(" — ").Append(card.FreeText.Trim());
                prompt.AppendLine();
            }
            prompt.AppendLine();
            prompt.AppendLine("Rends UNIQUEMENT le paragraphe, sans titre, sans préambule, sans formule de clôture.");

            var (ok, result, _) = await _llmService.ChatAsync(prompt.ToString(), new(), maxTokens: 400);
            return ok ? result.Trim() : "";
        }

        #endregion

        #region Synthèse Initiale V0d

        private InitialSynthesisWeights _synthesisWeights = new();
        public InitialSynthesisWeights SynthesisWeights
        {
            get => _synthesisWeights;
            set => SetProperty(ref _synthesisWeights, value);
        }

        private bool _isSynthesisMode = false;
        public bool IsSynthesisMode
        {
            get => _isSynthesisMode;
            set
            {
                if (SetProperty(ref _isSynthesisMode, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                }
            }
        }

        private string _synthesisContent = "";
        public string SynthesisContent
        {
            get => _synthesisContent;
            set => SetProperty(ref _synthesisContent, value);
        }

        private bool _areWeightsLoading = false;
        public bool AreWeightsLoading
        {
            get => _areWeightsLoading;
            set => SetProperty(ref _areWeightsLoading, value);
        }

        private bool _isGeneratingSynthesis = false;
        public bool IsGeneratingSynthesis
        {
            get => _isGeneratingSynthesis;
            set => SetProperty(ref _isGeneratingSynthesis, value);
        }

        private string _synthesisStatusMessage = "";
        public string SynthesisStatusMessage
        {
            get => _synthesisStatusMessage;
            set => SetProperty(ref _synthesisStatusMessage, value);
        }

        // V0d : Documents importés
        public ObservableCollection<ImportedConsultationDocument> ImportedDocuments { get; } = new();

        private bool _isImportingDocument = false;
        public bool IsImportingDocument
        {
            get => _isImportingDocument;
            set => SetProperty(ref _isImportingDocument, value);
        }

        // Statut de l'action "Documents" globale (depuis la zone Med Suggestions)
        private string _medDocumentStatus = "";
        public string MedDocumentStatus
        {
            get => _medDocumentStatus;
            set => SetProperty(ref _medDocumentStatus, value);
        }

        private void SwitchToSynthesis()
        {
            ResetWorkspaceModes();
            IsSynthesisMode = true;
        }

        private async Task ProposeWeightsAsync()
        {
            AreWeightsLoading = true;
            SynthesisStatusMessage = "⏳ Analyse des données...";

            try
            {
                if (_llmService == null) return;

                // Construire le prompt pour proposer les poids
                var prompt = BuildWeightProposalPrompt();
                var (success, response, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 500);

                if (success)
                {
                    // Parser la réponse JSON pour extraire les poids proposés
                    var weights = ExtractWeightsFromResponse(response);

                    SynthesisWeights.InterrogatoireWeight = weights.ContainsKey("interrogatoire")
                        ? weights["interrogatoire"]
                        : 0.5;

                    SynthesisWeights.ObservationsWeight = weights.ContainsKey("observations")
                        ? weights["observations"]
                        : 0.8;

                    SynthesisWeights.DocumentWeights = weights
                        .Where(kv => !kv.Key.StartsWith("interrogatoire") && !kv.Key.StartsWith("observations"))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    SynthesisWeights.LLMJustification = ExtractJustificationFromResponse(response);

                    SynthesisStatusMessage = "✅ Poids proposés (ajustable via sliders)";
                }
                else
                {
                    SynthesisStatusMessage = "❌ Erreur: " + response;
                }
            }
            catch (Exception ex)
            {
                SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                AreWeightsLoading = false;
            }
        }

        private async Task GenerateSynthesisAsync()
        {
            IsGeneratingSynthesis = true;
            SynthesisStatusMessage = "⏳ Génération synthèse pondérée...";

            try
            {
                if (_llmService == null) return;

                // Construire le JSON structuré avec données + poids validés
                var synthesisJSON = BuildInitialSynthesisJSON(SynthesisWeights);

                var prompt = BuildSynthesisGenerationPrompt(synthesisJSON);

                var (success, synthesis, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 2000);

                if (success)
                {
                    SynthesisContent = synthesis;
                    SynthesisStatusMessage = "✅ Synthèse générée avec succès";
                }
                else
                {
                    SynthesisStatusMessage = "❌ Erreur génération: " + synthesis;
                }
            }
            catch (Exception ex)
            {
                SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                IsGeneratingSynthesis = false;
                ScheduleDraftSave();
            }
        }

        private Task SaveSynthesisAsync()
        {
            if (string.IsNullOrEmpty(SynthesisContent))
            {
                SynthesisStatusMessage = "❌ Aucune synthèse à sauvegarder";
                return Task.CompletedTask;
            }

            if (_currentPatient == null || string.IsNullOrEmpty(_currentPatient.DirectoryPath))
            {
                SynthesisStatusMessage = "❌ Patient non disponible";
                return Task.CompletedTask;
            }

            try
            {
                var now = DateTime.Now;
                var synthesisWithMetadata = $@"---
date_synthese: {now:yyyy-MM-ddTHH:mm:ss}
type: initial_consultation
weights:
  interrogatoire: {SynthesisWeights.InterrogatoireWeight:F1}
  observations: {SynthesisWeights.ObservationsWeight:F1}
  moyenne: {SynthesisWeights.AverageWeight:F1}
justification: {SynthesisWeights.LLMJustification ?? ""}
---

{SynthesisContent}";

                // Sauvegarder dans {patientDir}/synthese/synthese.md (lu par le dossier patient)
                var syntheseDir  = Path.Combine(_currentPatient.DirectoryPath, "synthese");
                Directory.CreateDirectory(syntheseDir);
                var synthesePath = Path.Combine(syntheseDir, "synthese.md");

                // Backup de l'ancienne synthèse si elle existe
                if (File.Exists(synthesePath))
                {
                    var backupPath = Path.Combine(syntheseDir, $"synthese_backup_{now:yyyyMMdd_HHmmss}.md");
                    try { File.Copy(synthesePath, backupPath, true); } catch { }
                }

                File.WriteAllText(synthesePath, synthesisWithMetadata, System.Text.Encoding.UTF8);

                SynthesisStatusMessage = $"✅ Synthèse sauvegardée";

                // Recharge la synthèse dans le dossier bleu pour qu'elle soit immédiatement visible
                LoadPatientSynthesisFromDisk();

                // Retour automatique au hub après un court délai (laisse le temps de voir la confirmation)
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(900);
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        ConsultationType = ConsultationType.Normal;
                        IsSynthesisMode = false;
                        IsEditingConsultation = false;
                        ResetWorkspaceModes();
                        SynthesisStatusMessage = "";

                        // Nettoyer les données temporaires de la consultation
                        NoteContent = "";
                        ManualNotes = "";
                        ObservationsNarrative = "";
                        TranscriptionInput = "";
                        InterrogatoireBlocks.Clear();
                        _premiereConsultationFilePath = null;
                        _noteSaved = false;
                        _observationsNoteSaved = false;
                        // Brouillon terminé → supprimer
                        _draftSaveCts?.Cancel();
                        _draftSvc.Delete(_currentPatient?.DirectoryPath);
                    });
                });
            }
            catch (Exception ex)
            {
                SynthesisStatusMessage = $"❌ Erreur sauvegarde: {ex.Message}";
            }

            return Task.CompletedTask;
        }

        // Import géré par ConsultationModeControl.xaml.cs (code-behind)
        // car nécessite OpenFileDialog / ScanDocumentDialog (UI)
        private Task ImportDocumentAsync() => Task.CompletedTask;

        // Helpers

        private string BuildWeightProposalPrompt()
        {
            var interrogatoireData = BuildInterrogatoireJSON();
            var observationsData = BuildObservationsJSON();
            var documentsSection = "";

            if (ImportedDocuments.Count > 0)
            {
                documentsSection = @"

DOCUMENTS IMPORTÉS (bilans, rapports, etc.):
" + string.Join("\n", ImportedDocuments.Select(d =>
    $@"- {d.FileName} (Catégorie: {d.Category})
  Synthèse: {d.DocumentSynthesis}"));
            }

            return $@"Tu es un clinicien expérimenté en pédopsychiatrie évaluant la qualité des données d'une 1ère consultation.

DONNÉES COLLECTÉES:

INTERROGATOIRE (anamnèse parentale):
{interrogatoireData}

OBSERVATIONS CLINIQUES DIRECTES (examen enfant):
{observationsData}
{documentsSection}

TÂCHE: Évalue la FIABILITÉ et PERTINENCE diagnostique de chaque source de données pour cette synthèse initiale.

Critères d'évaluation:
- COMPLÉTUDE: Toutes les zones ont-elles été explorées?
- COHÉRENCE: Les informations sont-elles cohérentes/compatibles?
- CLARTÉ: Le contenu est-il précis et non ambigu?
- RELEVANCE: Les informations sont-elles pertinentes au motif?
- DÉTAIL CLINIQUE: Suffisant pour une première évaluation?

Échelle de poids (0.1-1.0):
- 0.9-1.0: Très complet, cohérent, cliniquement riche
- 0.7-0.8: Bon coverage, fiable, quelques détails manquants
- 0.5-0.6: Moyen, partiellement complet
- 0.3-0.4: Limité, incomplet
- 0.1-0.2: Très partiel, peu fiable pour synthèse

Réponds en JSON valide:" + (ImportedDocuments.Count > 0 ? @"
{{
  ""weights"": {{
    ""interrogatoire"": 0.X,
    ""observations"": 0.X,
    ""documents"": {{
      // Pour chaque document: ""document_id"": 0.X
    }}
  }},
  ""justification"": ""Courte analyse de la fiabilité de chaque composant""
}}" : @"
{{
  ""weights"": {{
    ""interrogatoire"": 0.X,
    ""observations"": 0.X
  }},
  ""justification"": ""Courte analyse de la fiabilité de chaque composant""
}}");
        }

        private string BuildInterrogatoireJSON()
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                transcription = TranscriptionInput ?? "",
                blocks_count = InterrogatoireBlocks?.Count ?? 0,
                blocks_filled = InterrogatoireBlocks?.Count(b => b.ProgressPct > 0) ?? 0
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private string BuildObservationsJSON()
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                total_cards = _clinicalObservations.Cards.Count,
                filled_cards = _clinicalObservations.Cards.Count(c => c.SelectedOption != null),
                observations = _clinicalObservations.Cards.Select(c => new
                {
                    branch = c.Branch.ToString(),
                    title = c.Title,
                    selected = c.SelectedOption
                }).ToList()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private Dictionary<string, double> ExtractWeightsFromResponse(string response)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(response);
                var weights = new Dictionary<string, double>();

                if (json.RootElement.TryGetProperty("weights", out var weightsObj))
                {
                    foreach (var prop in weightsObj.EnumerateObject())
                    {
                        if (double.TryParse(prop.Value.ToString(), out var weight))
                        {
                            weights[prop.Name] = Math.Clamp(weight, 0.1, 1.0);
                        }
                    }
                }

                return weights;
            }
            catch
            {
                return new Dictionary<string, double>();
            }
        }

        private string ExtractJustificationFromResponse(string response)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(response);
                if (json.RootElement.TryGetProperty("justification", out var justif))
                {
                    return justif.GetString() ?? "";
                }
            }
            catch { }

            return "";
        }

        private string BuildInitialSynthesisJSON(InitialSynthesisWeights weights)
        {
            var data = new
            {
                interrogatoire = BuildInterrogatoireJSON(),
                observations = BuildObservationsJSON()
            };

            // Inclure les documents importés s'il y en a
            var documentsList = ImportedDocuments.Select(d => new
            {
                document_id = d.DocumentId,
                file_name = d.FileName,
                category = d.Category,
                synthesis = d.DocumentSynthesis
            }).ToList();

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                consultation_type = "premiere_consultation",
                weights = new
                {
                    interrogatoire = weights.InterrogatoireWeight,
                    observations = weights.ObservationsWeight,
                    documents = ImportedDocuments.ToDictionary(d => d.DocumentId, d => d.Weight),
                    moyenne = weights.AverageWeight
                },
                data = new
                {
                    interrogatoire = BuildInterrogatoireJSON(),
                    observations = BuildObservationsJSON(),
                    documents = documentsList
                }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private string BuildSynthesisGenerationPrompt(string synthesisJSON)
        {
            return $@"Tu es un pédopsychiatre rédigeant la synthèse d'une première consultation. Voici les données recueillies :

{synthesisJSON}

RÈGLES ABSOLUES :
❌ Aucun diagnostic, hypothèse diagnostique ou étiquette pathologique.
❌ Aucune recommandation thérapeutique, bilan ou projet de soin.
❌ Aucune mention de poids, fiabilité, source ou méthode de collecte.
❌ Aucune interprétation spéculative.
✅ Style médical direct, factuel, à l'imparfait ou au présent descriptif.
✅ Intègre naturellement les données parentales ET les observations cliniques sans les distinguer en sections séparées.

STRUCTURE OBLIGATOIRE (Markdown, sections courtes et épurées) :

# Synthèse Initiale – Première Consultation

## Contexte
[2-3 lignes : prénom/âge, motif de consultation, mode de venue]

## Profil clinique
[3-5 lignes : description de l'enfant tel qu'observé — contact, langage, psychomotricité, mimiques, rapport au cadre]

## État émotionnel et comportement
[3-5 lignes : humeur, anxiété, comportement rapporté à la maison et observé en séance]

## Développement et parcours
[3-5 lignes : éléments développementaux, scolarité, bilans antérieurs, prises en charge en cours]

Rédige uniquement le document. Pas de préambule, pas de conclusion, pas de commentaire sur les données.";
        }

        #endregion

        #region Restitution aux Parents V0e

        private RestitutionAuxParents _restitution = new();
        public RestitutionAuxParents Restitution
        {
            get => _restitution;
            set => SetProperty(ref _restitution, value);
        }

        // Index ComboBox (0=LesDeux,1=Mere,2=Pere,3=GrandParents,4=Educateur,5=Autre)
        public int RestitutionAccompagnantIndex
        {
            get => (int)_restitution.TypeAccompagnant;
            set
            {
                _restitution.TypeAccompagnant = (TypeAccompagnant)value;
                OnPropertyChanged(nameof(RestitutionAccompagnantIndex));
                OnPropertyChanged(nameof(Restitution));
            }
        }

        private bool _isSelectingRestitutionTypeMode = false;
        public bool IsSelectingRestitutionTypeMode
        {
            get => _isSelectingRestitutionTypeMode;
            set => SetProperty(ref _isSelectingRestitutionTypeMode, value);
        }

        private bool _isDossierRestitutionCliniqueMode = false;
        public bool IsDossierRestitutionCliniqueMode
        {
            get => _isDossierRestitutionCliniqueMode;
            set => SetProperty(ref _isDossierRestitutionCliniqueMode, value);
        }

        private ViewModels.Restitutions.RestitutionEditorViewModel? _restitutionEditor;
        public ViewModels.Restitutions.RestitutionEditorViewModel? RestitutionEditor
        {
            get => _restitutionEditor;
            set => SetProperty(ref _restitutionEditor, value);
        }

        private bool _isRestitutionMode = false;
        public bool IsRestitutionMode
        {
            get => _isRestitutionMode;
            set
            {
                if (SetProperty(ref _isRestitutionMode, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                    OnPropertyChanged(nameof(IsRestitutionFormMode));
                }
            }
        }

        private bool _isRestitutionReviewMode = false;
        public bool IsRestitutionReviewMode
        {
            get => _isRestitutionReviewMode;
            set
            {
                if (SetProperty(ref _isRestitutionReviewMode, value))
                    OnPropertyChanged(nameof(IsRestitutionFormMode));
            }
        }

        public bool IsRestitutionFormMode => IsRestitutionMode && !IsRestitutionReviewMode;

        // Champs éditables après génération LLM
        private string _reviewIntro = "";
        public string ReviewIntro { get => _reviewIntro; set => SetProperty(ref _reviewIntro, value); }

        private string _reviewMotif = "";
        public string ReviewMotif { get => _reviewMotif; set => SetProperty(ref _reviewMotif, value); }

        private string _reviewForces = "";
        public string ReviewForces { get => _reviewForces; set => SetProperty(ref _reviewForces, value); }

        private string _reviewDefis = "";
        public string ReviewDefis { get => _reviewDefis; set => SetProperty(ref _reviewDefis, value); }

        private bool _isGeneratingRestitution = false;
        public bool IsGeneratingRestitution
        {
            get => _isGeneratingRestitution;
            set => SetProperty(ref _isGeneratingRestitution, value);
        }

        private string _restitutionStatusMessage = "";
        public string RestitutionStatusMessage
        {
            get => _restitutionStatusMessage;
            set => SetProperty(ref _restitutionStatusMessage, value);
        }

        private void SwitchToRestitution()
        {
            if (IsEditingConsultation && HasUnsavedConsultation())
            {
                var r = System.Windows.MessageBox.Show(
                    "Une consultation est en cours et n'a pas été sauvegardée.\n\nAbandonner la consultation en cours pour créer la restitution ?",
                    "Consultation en cours",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (r != System.Windows.MessageBoxResult.Yes) return;
            }

            ConsultationType = ConsultationType.Normal;
            IsEditingConsultation = false;
            ResetWorkspaceModes();
            IsSelectingRestitutionTypeMode = true; // Affiche la grille de sélection
        }

        private void StartRestitution(string type)
        {
            IsSelectingRestitutionTypeMode = false;
            if (type == "PremiereConsultation")
            {
                IsRestitutionMode = true;
                if (string.IsNullOrWhiteSpace(_restitution.NomAccompagnant) && CurrentPatient != null)
                    _restitution.NomAccompagnant = "";
                RestitutionStatusMessage = "";
            }
            else if (type == "DossierClinique")
            {
                var dossier = new DossierRestitutionInitial
                {
                    PatientNomComplet = CurrentPatient?.NomComplet ?? "Inconnu"
                };
                var pathService    = new PathService();
                var syntheseSvc    = new MedCompanion.Services.Synthesis.SyntheseGlobaleService(pathService);
                var projetSvc      = new MedCompanion.Services.Therapeutique.ProjetTherapeutiqueService(pathService);
                var dossierReader  = new MedCompanion.Services.Restitutions.DossierReaderService(pathService, syntheseSvc, projetSvc, _evaluationPhaseService);
                var suggesterService = new RestitutionSuggesterService(
                    _llmService!,
                    dossierReader,
                    syntheseSvc,
                    projetSvc,
                    new MedCompanion.Services.PatientContextService(
                        _storageService ?? new MedCompanion.StorageService(pathService),
                        new MedCompanion.Services.PatientIndexService(pathService)
                    )
                );
                
                var previewService = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(pathService, _evaluationPhaseService);

                Func<int, Task<bool>> editSphereAsync = (sphereNum) =>
                {
                    if (_currentPatient == null) return Task.FromResult(false);

                    var phase = _evaluationPhaseService?.LoadActive(_currentPatient.DirectoryPath);
                    if (phase == null)
                    {
                        // Fallback : dernière phase clôturée (modification directe du fichier)
                        var allPhases = _evaluationPhaseService?.LoadAll(_currentPatient.DirectoryPath)
                                        ?? new System.Collections.Generic.List<MedCompanion.Models.Evaluations.EvaluationPhase>();
                        phase = allPhases
                            .Where(p => !p.IsActive)
                            .OrderByDescending(p => p.DateCloture ?? p.DateDerniereModif)
                            .FirstOrDefault();
                    }

                    if (phase == null)
                    {
                        System.Windows.MessageBox.Show(
                            "Aucune évaluation trouvée pour ce patient.",
                            "Sphère non disponible");
                        return Task.FromResult(false);
                    }

                    MedCompanion.Models.Evaluations.ChenilleSegment? segment = sphereNum switch
                    {
                        1 => phase.CartographieEnfant.Attachement,
                        2 => phase.CartographieEnfant.Emotions,
                        3 => phase.CartographieEnfant.Langage,
                        6 => phase.CartographieEnfant.Imaginaire,
                        7 => phase.CartographieEnfant.Pensee,
                        _ => null
                    };

                    if (segment == null)
                    {
                        System.Windows.MessageBox.Show(
                            $"La sphère {sphereNum} utilise un profil spécifique (Tempérament, Psychomotricité ou Attention).\nOuvrez l'onglet Évaluation pour la compléter.",
                            "Sphère non éditable ici");
                        return Task.FromResult(false);
                    }

                    int? age = _currentPatient.Age;
                    var segVm = new CartographieSegmentViewModel(segment, () => age);
                    var dlg = new MedCompanion.Dialogs.SphereEvaluationDialog(segVm)
                    {
                        Owner = System.Windows.Application.Current?.MainWindow
                    };
                    bool? result = dlg.ShowDialog();
                    if (result == true)
                    {
                        _evaluationPhaseService?.Save(phase);
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);
                };

                var editorVm = new ViewModels.Restitutions.RestitutionEditorViewModel(
                    dossier,
                    CurrentPatient?.NomComplet ?? "Inconnu",
                    new RestitutionService(pathService),
                    suggesterService,
                    dossierReader,
                    previewService,
                    editSphereAsync
                );

                // Ouverture dans une fenêtre indépendante redimensionnable. L'utilisateur peut
                // la maximiser ou la déplacer sur un second écran sans perdre la consultation.
                var win = new MedCompanion.Views.Restitutions.RestitutionEditorWindow(editorVm)
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                win.Show();

                RestitutionEditor = editorVm;
                editorVm.RequestClose += () => { RestitutionEditor = null; };
            }
        }

        // Étape 1 : LLM génère les champs → mode révision
        private async Task GenerateRestitutionAsync()
        {
            if (_llmService == null) return;

            IsGeneratingRestitution = true;
            RestitutionStatusMessage = "⏳ Génération IA en cours...";

            try
            {
                var prompt = BuildRestitutionPrompt();
                var (ok, jsonRaw, err) = await _llmService.ChatAsync(prompt, new(), maxTokens: 1500);

                if (!ok || string.IsNullOrWhiteSpace(jsonRaw))
                {
                    RestitutionStatusMessage = $"❌ Erreur LLM : {err}";
                    return;
                }

                // Extraction par balises ===SECTION=== : insensible aux caractères spéciaux
                // dans le texte généré (pas de JSON à parser → plus d'erreurs de guillemets).
                static string ExtractSection(string text, string startTag, string endTag)
                {
                    var s = text.IndexOf(startTag, StringComparison.Ordinal);
                    if (s < 0) return "";
                    s += startTag.Length;
                    var e = text.IndexOf(endTag, s, StringComparison.Ordinal);
                    if (e < 0) e = text.Length;
                    return text.Substring(s, e - s).Trim();
                }

                static string ExtractBullets(string section)
                {
                    if (string.IsNullOrWhiteSpace(section))
                        return "Aucun élément identifié lors de cette première rencontre.";
                    var items = section
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(l => l.TrimStart('-', '*', '•', ' ').Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();
                    return items.Count == 0
                        ? "Aucun élément identifié lors de cette première rencontre."
                        : string.Join("\n", items);
                }

                var raw = jsonRaw;

                // Remplir les champs éditables
                ReviewIntro   = ExtractSection(raw, "===INTRO===",   "===MOTIF===");
                ReviewMotif   = ExtractSection(raw, "===MOTIF===",   "===FORCES===");
                ReviewForces  = ExtractBullets(ExtractSection(raw, "===FORCES===", "===DEFIS==="));
                ReviewDefis   = ExtractBullets(ExtractSection(raw, "===DEFIS===",  "===FIN==="));

                // Fallback si le LLM n'a pas respecté les balises
                if (string.IsNullOrWhiteSpace(ReviewIntro) && string.IsNullOrWhiteSpace(ReviewMotif))
                {
                    RestitutionStatusMessage = "❌ Le LLM n'a pas respecté le format attendu. Réessaie.";
                    return;
                }

                // Basculer en mode révision
                IsRestitutionReviewMode = true;
                RestitutionStatusMessage = "✏️ Vérifiez et modifiez si besoin, puis confirmez pour générer le PDF.";

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested);
            }
            catch (Exception ex)
            {
                RestitutionStatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsGeneratingRestitution = false;
            }
        }

        // Étape 2 : Confirmer les champs édités → générer le PDF
        private async Task ConfirmRestitutionAsync()
        {
            IsGeneratingRestitution = true;
            RestitutionStatusMessage = "⏳ Génération du PDF...";

            try
            {
                // Template HTML
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var templatePath = Path.Combine(appDir, "Resources", "Consultation", "restitution_template.html");
                if (!File.Exists(templatePath))
                {
                    RestitutionStatusMessage = "❌ Template introuvable.";
                    return;
                }
                var template = File.ReadAllText(templatePath, System.Text.Encoding.UTF8);

                // Convertir les champs multi-lignes en <ul>
                string BuildListFromLines(string text)
                {
                    var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (lines.Length == 0)
                        return "<p><em>Aucun élément identifié lors de cette première rencontre.</em></p>";
                    return "<ul>" + string.Join("", lines.Select(l =>
                        $"<li>{System.Net.WebUtility.HtmlEncode(l)}</li>")) + "</ul>";
                }

                // Bilans fournis (C#, jamais LLM)
                var bilansFournis = ImportedDocuments.Count == 0
                    ? "<p><em>Aucun document fourni lors de cette consultation.</em></p>"
                    : "<ul>" + string.Join("", ImportedDocuments.Select(d =>
                        $"<li>{System.Net.WebUtility.HtmlEncode(d.FileName)}</li>")) + "</ul>";

                // Données fixes C#
                var doctorName    = AppSettings.Load().Medecin;
                var mentionLegale = _restitution.MentionLegale;
                var mentionBlock  = string.IsNullOrWhiteSpace(mentionLegale) ? "" :
                    $"<div class=\"legal\">⚖️ {mentionLegale}</div>";

                var html = template
                    .Replace("{{DOCTOR_NAME}}",    System.Net.WebUtility.HtmlEncode(doctorName))
                    .Replace("{{INTRO}}",          ReviewIntro)
                    .Replace("{{MOTIF}}",          ReviewMotif)
                    .Replace("{{BILANS_FOURNIS}}", bilansFournis)
                    .Replace("{{FORCES}}",         BuildListFromLines(ReviewForces))
                    .Replace("{{DEFIS}}",          BuildListFromLines(ReviewDefis))
                    .Replace("{{NOMBRE_SEANCES}}", _restitution.NombreSeances.ToString())
                    .Replace("{{MENTION_LEGALE}}", mentionBlock);

                if (_currentPatient == null || string.IsNullOrEmpty(_currentPatient.DirectoryPath))
                {
                    RestitutionStatusMessage = "❌ Impossible de sauvegarder : dossier patient introuvable.";
                    return;
                }

                var restitutionsDir = Path.Combine(_currentPatient.DirectoryPath, DateTime.Now.Year.ToString(), "restitutions");
                Directory.CreateDirectory(restitutionsDir);
                
                var stamp    = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var htmlPath = Path.Combine(restitutionsDir, $"restitution_PremierEntretien_v1_{stamp}.html");
                var pdfPath  = Path.Combine(restitutionsDir, $"restitution_PremierEntretien_v1_{stamp}.pdf");
                var mdPath   = Path.Combine(restitutionsDir, $"restitution_PremierEntretien_v1_{stamp}.md");
                File.WriteAllText(htmlPath, html, System.Text.Encoding.UTF8);

                _restitution.GeneratedHtmlPath = htmlPath;
                OnPropertyChanged(nameof(Restitution));

                var edgeSvc  = new MedCompanion.Services.EdgeHeadlessPdfService();
                var savedPdf = string.Empty;
                if (edgeSvc.IsAvailable)
                {
                    var pdfOk = await edgeSvc.ConvertAsync(htmlPath, pdfPath);
                    if (pdfOk)
                    {
                        savedPdf = pdfPath;
                        _restitution.GeneratedPdfPath = pdfPath;
                        OnPropertyChanged(nameof(Restitution));
                        RestitutionStatusMessage = "✅ PDF sauvegardé dans le dossier patient";
                    }
                    else
                    {
                        RestitutionStatusMessage = "⚠️ Conversion PDF échouée — HTML disponible";
                    }
                }
                else
                {
                    RestitutionStatusMessage = "⚠️ Microsoft Edge introuvable — HTML disponible";
                }

                // Manifest Markdown — écrit après le PDF pour inclure le chemin réel
                var now = DateTime.Now;
                var mdContent = $@"---
type: PremierEntretien
version: 1
statut: Validee
patient: ""{_currentPatient.NomComplet}""
date_creation: ""{now:O}""
date_validation: ""{now:O}""
pdf_path: ""{savedPdf.Replace("\\", "\\\\")}""
---
";
                File.WriteAllText(mdPath, mdContent, System.Text.Encoding.UTF8);

                // Rafraîchir le panel DOCS (dossier bleu) avec le nouveau PDF
                if (!string.IsNullOrEmpty(savedPdf))
                    LoadPatientDocumentsFromDisk();

                // Notifier MainWindow (enregistrement optionnel dans l'index Documents du panel IA)
                if (_currentPatient != null && !string.IsNullOrEmpty(savedPdf))
                    RestitutionPdfSavedToPatient?.Invoke(this, savedPdf);

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested);
            }
            catch (Exception ex)
            {
                RestitutionStatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsGeneratingRestitution = false;
            }
        }

        private string BuildRestitutionPrompt()
        {
            var patient     = CurrentPatient;
            var prenom      = patient?.Prenom ?? "";
            var patientName = $"{prenom} {patient?.Nom}".Trim();
            var dateStr     = DateTime.Now.ToString("dd/MM/yyyy");

            // Ton et salutation selon l'accompagnant
            var accomp = _restitution.NomAccompagnant?.Trim() ?? "";
            var tonInstruction = _restitution.TypeAccompagnant switch
            {
                TypeAccompagnant.LesDeuParents =>
                    $"Salutation : \"Chers parents,\" — ton chaleureux et bienveillant, adressez-vous directement aux parents.",
                TypeAccompagnant.MereSeule =>
                    $"Salutation : \"Chère maman,\" — ton chaleureux, adressez-vous directement à la mère.",
                TypeAccompagnant.PereSeul =>
                    $"Salutation : \"Cher papa,\" — ton chaleureux, adressez-vous directement au père.",
                TypeAccompagnant.GrandParents =>
                    $"Salutation : \"Chers grands-parents,\" — ton chaleureux mais légèrement formel. " +
                    (string.IsNullOrWhiteSpace(accomp) ? "" : $"Accompagnant présent : {accomp}."),
                TypeAccompagnant.Educateur =>
                    $"Salutation formelle : \"Vous avez accompagné {prenom} lors de cette consultation.\" " +
                    $"— vouvoiement strict, ton institutionnel et respectueux. " +
                    (string.IsNullOrWhiteSpace(accomp) ? "" : $"Accompagnant : {accomp}."),
                TypeAccompagnant.Autre =>
                    $"Salutation formelle : \"Vous avez accompagné {prenom} lors de cette consultation.\" " +
                    $"— vouvoiement strict, ton institutionnel. " +
                    (string.IsNullOrWhiteSpace(accomp) ? "" : $"Accompagnant : {accomp}."),
                _ =>
                    "Salutation : \"Chers parents,\" — ton chaleureux."
            };

            var donnees = new System.Text.StringBuilder();

            var interrogatoireRempli = InterrogatoireBlocks.Where(b => !string.IsNullOrWhiteSpace(b.FreeText)).ToList();
            if (interrogatoireRempli.Count > 0)
            {
                donnees.AppendLine("=== INTERROGATOIRE ===");
                foreach (var block in interrogatoireRempli)
                    donnees.AppendLine($"[{block.Title}] {block.FreeText}");
            }
            if (!string.IsNullOrWhiteSpace(_clinicalObservations.GeneratedClinicalNarrative))
            {
                donnees.AppendLine("=== OBSERVATIONS CLINIQUES ===");
                donnees.AppendLine(_clinicalObservations.GeneratedClinicalNarrative);
            }
            if (!string.IsNullOrWhiteSpace(_synthesisContent))
            {
                donnees.AppendLine("=== SYNTHÈSE ===");
                donnees.AppendLine(_synthesisContent);
            }
            foreach (var d in ImportedDocuments.Where(d => !string.IsNullOrWhiteSpace(d.DocumentSynthesis)))
            {
                donnees.AppendLine($"=== BILAN FOURNI : {d.FileName} ===");
                donnees.AppendLine(d.DocumentSynthesis);
            }
            if (!string.IsNullOrWhiteSpace(_restitution.NotesLibres))
            {
                donnees.AppendLine("=== NOTES CLINICIEN ===");
                donnees.AppendLine(_restitution.NotesLibres);
            }

            var hasDonnees = donnees.Length > 0;

            return $@"Tu es un pédopsychiatre. Rédige le document de restitution pour {patientName} (consultation du {dateStr}).

TON ET SALUTATION : {tonInstruction}

{(hasDonnees ? $"DONNÉES CLINIQUES :\n{donnees}" : "AUCUNE DONNÉE CLINIQUE DISPONIBLE.")}

RÈGLES ABSOLUES :
- Aucun diagnostic, étiquette pathologique ou terme médical incompréhensible.
- Si les données sont insuffisantes : écris exactement ""Aucun élément identifié lors de cette première rencontre."" — ne jamais inventer.
- FORCES et DEFIS : 2 à 3 éléments maximum, phrases courtes (débuter chaque élément par un tiret -).
- L'intro cite la date {dateStr} et respecte strictement le ton indiqué.

RETOURNE UNIQUEMENT ce texte structuré (copie exactement les balises ===) :
===INTRO===
[Salutation adaptée + 2 phrases — contexte du {dateStr} et objectif bienveillant]
===MOTIF===
[Résumé du motif en 2-3 lignes accessibles]
===FORCES===
- [Point fort 1]
- [Point fort 2]
===DEFIS===
- [Défi 1]
- [Défi 2]
===FIN===";
        }

        private void OpenRestitutionFile()
        {
            var path = _restitution.GeneratedPdfPath ?? _restitution.GeneratedHtmlPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                RestitutionStatusMessage = "❌ Aucun fichier généré.";
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                RestitutionStatusMessage = $"❌ Impossible d'ouvrir : {ex.Message}";
            }
        }

        #endregion

        #region Consultation de Suivi V0

        private ConsultationSuivi _suivi = new();

        // Dictionnaire de brouillons de Suivi par patient (en mémoire seulement, perdu à la fermeture de l'app)
        // Clé = NomComplet du patient. Permet de garder un brouillon par patient quand on navigue ailleurs.
        private readonly Dictionary<string, ConsultationSuivi> _suiviDraftsByPatient = new();

        /// <summary>
        /// Stash le Suivi en cours pour le patient passé en paramètre dans le dictionnaire de brouillons,
        /// SI il a du contenu. Sinon retire l'entrée du dictionnaire.
        /// Auto-pause Whisper si en cours d'enregistrement.
        /// </summary>
        private void StashCurrentSuiviDraftFor(string? patientKey)
        {
            if (string.IsNullOrEmpty(patientKey)) return;

            // Auto-pause si recording (fire-and-forget pour éviter tout risque de deadlock UI)
            if (IsRecording && _whisperService != null)
            {
                _ = _whisperService.StopAsync();
                IsRecording = false;
            }

            if (HasSuiviInProgress)
                _suiviDraftsByPatient[patientKey] = _suivi;
            else
                _suiviDraftsByPatient.Remove(patientKey);
        }

        /// <summary>
        /// Restaure le brouillon de Suivi du patient passé en paramètre, ou crée une instance vierge.
        /// </summary>
        private void RestoreSuiviDraftFor(string? patientKey)
        {
            if (!string.IsNullOrEmpty(patientKey) && _suiviDraftsByPatient.TryGetValue(patientKey, out var draft))
                Suivi = draft;
            else
                Suivi = new ConsultationSuivi();
        }

        public ConsultationSuivi Suivi
        {
            get => _suivi;
            set
            {
                value ??= new ConsultationSuivi();
                if (_suivi != null) _suivi.PropertyChanged -= OnSuiviInnerPropertyChanged;
#pragma warning disable CS8601
                if (SetProperty(ref _suivi, value!))
#pragma warning restore CS8601
                {
                    if (_suivi != null) _suivi.PropertyChanged += OnSuiviInnerPropertyChanged;
                    OnPropertyChanged(nameof(HasSuiviInProgress));
                }
            }
        }

        private void OnSuiviInnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Toute modification interne (case cochée, transcription, extraction) → réévaluer HasSuiviInProgress
            OnPropertyChanged(nameof(HasSuiviInProgress));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
        }

        private bool _isExtractingSuivi;
        public bool IsExtractingSuivi
        {
            get => _isExtractingSuivi;
            set => SetProperty(ref _isExtractingSuivi, value);
        }

        private string _suiviStatusMessage = "";
        public string SuiviStatusMessage
        {
            get => _suiviStatusMessage;
            set => SetProperty(ref _suiviStatusMessage, value);
        }

        /// <summary>
        /// Construit la note finale à partir des cases cochées + extraction IA.
        /// Si RAS : la note se limite à "RAS, va bien" (autres cases et transcription ignorées).
        /// </summary>
        private string BuildSuiviNote()
        {
            var dateStr = ConsultationDate.ToString("dd/MM/yyyy");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Consultation de suivi — {dateStr}");
            sb.AppendLine();

            if (Suivi.RAS)
            {
                sb.AppendLine("- RAS, va bien.");
                return sb.ToString().TrimEnd();
            }

            // Cases cochées en puces
            if (Suivi.Renouvellement)       sb.AppendLine("- Renouvellement du traitement.");
            if (Suivi.PasEffetsSecondaires) sb.AppendLine("- Pas d'effets secondaires signalés.");
            if (Suivi.AdhesionOk)           sb.AppendLine("- Adhésion thérapeutique satisfaisante.");
            if (Suivi.EvolutionScolaire)    sb.AppendLine("- Évolution scolaire favorable.");
            if (Suivi.SommeilCorrect)       sb.AppendLine("- Sommeil correct.");
            if (Suivi.ARevoir)              sb.AppendLine("- À revoir dans 1 mois.");

            // Extraction IA en bonus
            if (!string.IsNullOrWhiteSpace(Suivi.AIExtraction))
            {
                sb.AppendLine();
                sb.AppendLine(Suivi.AIExtraction.Trim());
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Extrait la transcription brute actuelle en puces cliniques compactes.
        /// Append le résultat à Suivi.AIExtraction (cumulatif sur plusieurs pauses).
        /// Vide Suivi.Transcription en cas de succès pour la prochaine dictée.
        /// </summary>
        private async Task ExtractSuiviAsync()
        {
            if (_llmService == null) return;
            if (string.IsNullOrWhiteSpace(Suivi.Transcription))
            {
                SuiviStatusMessage = "❌ Aucune transcription à analyser.";
                return;
            }

            IsExtractingSuivi = true;
            SuiviStatusMessage = "⏳ Extraction IA en cours...";

            try
            {
                var prompt = $@"Tu es pédopsychiatre. Voici un SEGMENT de transcription d'une consultation de suivi (souvent bavarde, beaucoup de digressions).

Ton rôle : extraire UNIQUEMENT les éléments cliniquement pertinents sous forme de PUCES ULTRA-COMPACTES (3-5 puces maximum, une ligne chacune).

GARDE :
- Évolution des symptômes cibles
- Effets secondaires (même évoqués en passant)
- Adhérence au traitement, oublis
- Évènements de vie significatifs (déménagement, deuil, conflit, scolarité)
- Humeur, sommeil, appétit, comportement
- Idées noires, automutilation, consommation

IGNORE :
- Digressions, anecdotes ordinaires, bavardage
- Activités banales (parc, jeux, vacances sans particularité)
- Considérations météo, hobbies sans lien clinique

Si rien de cliniquement pertinent dans ce segment : réponds exactement ""RAS"".

FORMAT — RÈGLE ABSOLUE :
- Uniquement des puces markdown avec ""- ""
- AUCUN titre, AUCUNE introduction, AUCUN préambule (pas de ""Voici…"", ""Dans ce segment…"", etc.)
- Commence DIRECTEMENT par ""- "" sur la première ligne

SEGMENT À ANALYSER :
{Suivi.Transcription}

PUCES :";

                var (ok, result, err) = await _llmService.ChatAsync(prompt, new(), maxTokens: 400);

                if (!ok || string.IsNullOrWhiteSpace(result))
                {
                    SuiviStatusMessage = $"❌ Erreur LLM : {err} (transcription conservée)";
                    return;
                }

                var newBullets = result.Trim();

                // Filtrer "RAS" simple (segment sans contenu pertinent)
                if (string.Equals(newBullets, "RAS", StringComparison.OrdinalIgnoreCase))
                {
                    Suivi.Transcription = "";
                    SuiviStatusMessage = "✅ Segment sans élément clinique (RAS).";
                    return;
                }

                // Append au cumul existant (séparateur ligne vide entre segments)
                if (string.IsNullOrWhiteSpace(Suivi.AIExtraction))
                    Suivi.AIExtraction = newBullets;
                else
                    Suivi.AIExtraction = Suivi.AIExtraction.TrimEnd() + "\n" + newBullets;

                // Vider la transcription brute — la valeur est maintenant dans les puces
                Suivi.Transcription = "";
                SuiviStatusMessage = "✅ Segment extrait, puces cumulées.";
            }
            catch (Exception ex)
            {
                SuiviStatusMessage = $"❌ Erreur : {ex.Message} (transcription conservée)";
            }
            finally
            {
                IsExtractingSuivi = false;
            }
        }

        private async Task SaveSuiviNoteAsync()
        {
            if (_currentPatient == null || string.IsNullOrEmpty(_currentPatient.DirectoryPath))
            {
                SuiviStatusMessage = "❌ Patient non disponible.";
                return;
            }

            try
            {
                var notesDir = Path.Combine(_currentPatient.DirectoryPath, ConsultationDate.Year.ToString(), "notes");
                Directory.CreateDirectory(notesDir);

                var stamp    = ConsultationDate.ToString("yyyy-MM-dd_HHmm");
                var filePath = Path.Combine(notesDir, $"{stamp}_suivi.md");

                var noteContent = $@"---
patient: ""{_currentPatient.NomComplet}""
date: ""{ConsultationDate:yyyy-MM-ddTHH:mm}""
type: ""consultation-suivi""
source: ""MedCompanion""
---

{BuildSuiviNote()}
";

                File.WriteAllText(filePath, noteContent, System.Text.Encoding.UTF8);
                SuiviStatusMessage = $"✅ Note sauvegardée : {Path.GetFileName(filePath)}";

                // Rafraîchir le dossier patient pour faire apparaître la nouvelle note immédiatement
                await RefreshConsultationNotesAsync();

                // Sortir du mode édition et retourner au hub
                IsEditingConsultation = false;
                ConsultationType = ConsultationType.Normal;
                Suivi.Reset();  // nettoyage après finalisation
                OnPropertyChanged(nameof(HasSuiviInProgress));
                RaiseNoteSavedToPatient();
                TriggerUrgenceAnalysis(filePath, noteContent, "consultation-suivi");
            }
            catch (Exception ex)
            {
                SuiviStatusMessage = $"❌ Erreur sauvegarde : {ex.Message}";
            }
        }

        /// <summary>
        /// True dès qu'une consultation de suivi a du contenu non sauvegardé
        /// (case cochée, transcription brute en cours, ou puces déjà extraites).
        /// Utilisé pour afficher une carte "Reprendre" sur le hub.
        /// </summary>
        public bool HasSuiviInProgress =>
            Suivi.RAS || Suivi.Renouvellement || Suivi.PasEffetsSecondaires ||
            Suivi.AdhesionOk || Suivi.EvolutionScolaire || Suivi.SommeilCorrect ||
            Suivi.ARevoir ||
            !string.IsNullOrWhiteSpace(Suivi.Transcription) ||
            !string.IsNullOrWhiteSpace(Suivi.AIExtraction);

        /// <summary>
        /// Reprend la consultation de suivi en cours sans réinitialiser son contenu.
        /// </summary>
        private void ResumeSuivi()
        {
            ResetWorkspaceModes();
            ConsultationType = ConsultationType.Suivi;
            IsEditingConsultation = true;
            SuiviStatusMessage = "▶ Reprise de la consultation en cours.";
        }

        /// <summary>
        /// Abandonne le brouillon de suivi en cours (perd les puces et cases).
        /// </summary>
        private void DiscardSuivi()
        {
            var r = System.Windows.MessageBox.Show(
                "Abandonner la consultation de suivi en cours ?\n\nToutes les puces extraites et les cases cochées seront perdues.",
                "Abandonner",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (r != System.Windows.MessageBoxResult.Yes) return;

            Suivi.Reset();
            SuiviStatusMessage = "";
            OnPropertyChanged(nameof(HasSuiviInProgress));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
        }

        #endregion

        #region Commands

        public ICommand SwitchToFocusTravailCommand { get; }
        public ICommand SwitchToConsultationCommand { get; }
        public ICommand SwitchToDossierCommand { get; }

        public ICommand SetMedSilencieuxCommand { get; }
        public ICommand SetMedSuggestionsCommand { get; }
        public ICommand SetMedChecklistCommand { get; }
        public ICommand ToggleMedVoiceCommand { get; }
        public ICommand ToggleMedExpandedCommand { get; }

        public ICommand SelectDossierTabCommand { get; }

        public ICommand SaveNoteCommand { get; }

        public ICommand ExtractInterrogatoireCommand { get; }
        public ICommand SaveInterrogatoireNoteCommand { get; }
        public ICommand AddFinalNoteSectionCommand { get; }   // param : ConsultationBlockViewModel
        public ICommand ReformulateAllCommand { get; }        // reformule toutes les sections
        public ICommand BackToSaisieCommand { get; }
        public ICommand SwitchWhisperModelCommand { get; private set; }
        public ICommand StartRecordingCommand { get; }
        public ICommand StopRecordingCommand { get; }

        // Hub de consultations (frise + bouton +)
        public ICommand NewConsultationCommand { get; }       // param : "premiere" | "suivi"
        public ICommand OpenCardCommand { get; }              // param : ConsultationCardViewModel
        public ICommand DeleteConsultationCardCommand { get; } // param : ConsultationCardViewModel
        public ICommand ClosePastConsultationCommand { get; }
        public ICommand DeleteSelectedPastConsultationCommand { get; }
        public ICommand ViewPastPremiereStepCommand { get; }
        public ICommand ClosePastPremiereConsultationCommand { get; }
        public ICommand EditPastPremiereCommand { get; private set; } = null!;
        public ICommand SavePastPremiereCommand { get; private set; } = null!;
        public ICommand CancelPastPremiereEditCommand { get; private set; } = null!;
        public ICommand OpenEvaluationCardCommand { get; }    // param : EvaluationCardViewModel
        public ICommand DeleteEvaluationCardCommand { get; }  // param : EvaluationCardViewModel
        public ICommand OpenSyntheseGlobaleCardCommand { get; private set; } = null!;  // param : SyntheseGlobaleCardViewModel
        public ICommand OpenProjetTherapeutiqueCardCommand { get; private set; } = null!;  // param : ProjetTherapeutiqueCardViewModel
        public ICommand DeleteSyntheseGlobaleCardCommand { get; } // param : SyntheseGlobaleCardViewModel
        public ICommand DeleteProjetTherapeutiqueCardCommand { get; } // param : ProjetTherapeutiqueCardViewModel

        // V0b : Commande pour confirmer l'âge manuellement
        public ICommand ConfirmAgeCommand { get; }

        // V0b : Commande pour saisir le motif manuellement
        public ICommand SetMotifManuallyCommand { get; }

        // V0c : Commande pour basculer la visibilité d'un bloc
        public ICommand ToggleBlockVisibilityCommand { get; }

        // V0c : Commandes Observations Cliniques
        public ICommand SwitchToInterrogatoireCommand { get; }
        public ICommand SwitchToClinicalCommand { get; }
        public ICommand OpenFormulaireParentsCommand { get; }
        public ICommand GenererFormulaireCommand     { get; }
        public ICommand ImportFormulaireImageCommand { get; }
        public ICommand CameraFormulaireCommand      { get; }
        public ICommand CloseFormulaireCommand       { get; }
        public ICommand SelectObservationCommand { get; }
        public ICommand ToggleCardExpandCommand { get; }
        public ICommand TerminateClinicalObservationsCommand { get; }
        public ICommand SaveObservationsNoteCommand { get; }
        public ICommand BackToObservationsCardsCommand { get; }

        // V0d : Commandes Synthèse Initiale
        public ICommand SwitchToSynthesisCommand { get; }
        public ICommand ProposeWeightsCommand { get; }
        public ICommand GenerateSynthesisCommand { get; }
        public ICommand SaveSynthesisCommand { get; }

        // V0d : Commandes Documents Importés
        public ICommand ImportDocumentCommand { get; }
        public ICommand RemoveDocumentCommand { get; }

        // V0e : Commandes Restitution
        public ICommand StartRestitutionCommand { get; }
        public ICommand SwitchToRestitutionCommand { get; }
        public ICommand GenerateRestitutionCommand { get; }
        public ICommand ConfirmRestitutionCommand { get; }
        public ICommand BackToRestitutionFormCommand { get; }
        public ICommand OpenRestitutionCommand { get; }

        public ICommand ExtractSuiviCommand { get; }
        public ICommand SaveSuiviCommand { get; }
        public ICommand ResumeSuiviCommand { get; }
        public ICommand DiscardSuiviCommand { get; }

        #endregion

        #region Constructor

        public ConsultationModeViewModel()
        {
            // Charger le modèle Whisper persisté
            var savedSettings = AppSettings.Load();
            _selectedWhisperModel = savedSettings.WhisperModel == "LargeV3"
                ? WhisperModelSize.LargeV3
                : WhisperModelSize.Medium;

            // V0b : initialiser le suggester avec le resolver
            _blockSuggester = new ContextualBlockSuggester(_blockSetResolver);
            _motifDetector.MotifDetected += OnMotifDetected;
            // Par défaut, ouvrir sur la couverture
            _activeDossierTab = DossierTab.Couverture;

            RestitutionsHub = new RestitutionsHubViewModel(new RestitutionService(new PathService()));
            RestitutionsHub.RequestCreateNew += () => {
                if (SwitchToRestitutionCommand?.CanExecute(null) == true)
                    SwitchToRestitutionCommand.Execute(null);
            };

            // Commands Layout
            SwitchToFocusTravailCommand = new RelayCommand(_ => CurrentState = ConsultationViewState.FocusTravail);
            SwitchToConsultationCommand = new RelayCommand(_ => CurrentState = ConsultationViewState.Consultation);
            SwitchToDossierCommand = new RelayCommand(_ => CurrentState = ConsultationViewState.FocusDossier);

            // Commands Med
            SetMedSilencieuxCommand = new RelayCommand(_ => MedMode = MedConsultationMode.Silencieux);
            SetMedSuggestionsCommand = new RelayCommand(_ => MedMode = MedConsultationMode.Suggestions);
            SetMedChecklistCommand = new RelayCommand(_ => MedMode = MedConsultationMode.Checklist);
            ToggleMedVoiceCommand   = new RelayCommand(_ => IsMedVoiceOn = !IsMedVoiceOn);
            ToggleMedExpandedCommand = new RelayCommand(_ => IsMedExpanded = !IsMedExpanded);

            // Command Dossier Tab
            SelectDossierTabCommand = new RelayCommand(param =>
            {
                if (param is DossierTab tab)
                    ActiveDossierTab = tab;
                else if (param is string tabName && Enum.TryParse<DossierTab>(tabName, out var parsedTab))
                    ActiveDossierTab = parsedTab;
            });

            // Command Save Note
            SaveNoteCommand = new RelayCommand(async _ => await SaveNoteAsync(), _ => HasPatient && HasNoteContent);

            // Commands interrogatoire
            ExtractInterrogatoireCommand   = new RelayCommand(async _ => await ExtractInterrogatoireAsync(), _ => CanExtract);
            SaveInterrogatoireNoteCommand  = new RelayCommand(async _ => await SaveInterrogatoireNoteAsync(), _ => IsInFinalNoteMode && HasPatient && HasNoteContent && !_noteSaved);
            AddFinalNoteSectionCommand     = new RelayCommand(p => AddFinalNoteSection(p as ConsultationBlockViewModel), p => p is ConsultationBlockViewModel);
            ReformulateAllCommand          = new RelayCommand(async _ => await ReformulateAllAsync(),
                                                              _ => !IsReformulatingAll && FinalNoteBlocks.Any(b => !string.IsNullOrWhiteSpace(b.FreeText)));
            BackToSaisieCommand = new RelayCommand(_ =>
            {
                // Retour en saisie sans effacer les blocs (le contenu est précieux)
                NoteContent = "";
                _noteSaved = false;
                InterrogatoireState = InterrogatoireState.Saisie;
            });

            SwitchWhisperModelCommand = new RelayCommand(
                async _ =>
                {
                    if (_whisperService == null) return;
                    var newModel = _selectedWhisperModel == WhisperModelSize.Medium
                        ? WhisperModelSize.LargeV3
                        : WhisperModelSize.Medium;
                    await _whisperService.UnloadModelAsync();
                    SelectedWhisperModel = newModel;
                    var settings = AppSettings.Load();
                    settings.WhisperModel = newModel == WhisperModelSize.LargeV3 ? "LargeV3" : "Medium";
                    settings.Save();
                    ExtractionStatus = $"Modèle Whisper basculé : {WhisperModelLabel} (chargement au prochain démarrage de dictée)";
                },
                _ => !IsRecording && _whisperService != null);

            StartRecordingCommand = new RelayCommand(
                async _ =>
                {
                    if (_whisperService == null) return;
                    _whisperService.Mode                 = UseBatchMode ? RecordingMode.Batch : RecordingMode.Streaming;
                    _whisperService.BatchDurationSeconds = BatchDurationSeconds;
                    var modelManager = new WhisperModelManager { ModelSize = _selectedWhisperModel };
                    await _whisperService.StartAsync(modelManager);
                    IsRecording = true;
                },
                _ => (IsInSaisieMode || IsSuiviMode) && !IsRecording && _whisperService != null);

            StopRecordingCommand = new RelayCommand(
                async _ =>
                {
                    if (_whisperService == null) return;
                    await _whisperService.StopAsync();
                    IsRecording = false;

                    // En mode Suivi : la "pause" déclenche automatiquement l'extraction IA
                    // → les puces sont cumulées et la transcription brute est vidée
                    if (IsSuiviMode && !string.IsNullOrWhiteSpace(Suivi.Transcription))
                    {
                        await ExtractSuiviAsync();
                    }
                },
                _ => IsRecording);

            // Hub : créer une nouvelle consultation (param "premiere" | "suivi")
            NewConsultationCommand = new RelayCommand(param =>
            {
                var type = param?.ToString() ?? "premiere";

                // Confirmation si une consultation est en cours non sauvegardée
                if (IsEditingConsultation && HasUnsavedConsultation())
                {
                    var r = System.Windows.MessageBox.Show(
                        "Une consultation est en cours et n'a pas été sauvegardée.\n\nAbandonner la consultation en cours ?",
                        "Consultation en cours",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    if (r != System.Windows.MessageBoxResult.Yes) return;
                }

                StartNewConsultation(type);
            });

            // Hub : ouvrir une carte passée (lecture)
            OpenCardCommand = new RelayCommand(param =>
            {
                if (param is ConsultationCardViewModel card)
                    OpenPastConsultation(card);
            });

            DeleteConsultationCardCommand = new RelayCommand(param =>
            {
                if (param is ConsultationCardViewModel card)
                    DeleteConsultationCard(card);
            });

            ClosePastConsultationCommand = new RelayCommand(_ =>
            {
                ResetWorkspaceModes();
            });

            DeleteSelectedPastConsultationCommand = new RelayCommand(_ =>
            {
                DeleteSelectedPastConsultation();
            });

            ViewPastPremiereStepCommand = new RelayCommand(param =>
            {
                if (param is string s && int.TryParse(s, out var step))
                {
                    PastPremiereStep = step;
                }
                else if (param is int stepInt)
                {
                    PastPremiereStep = stepInt;
                }
            });

            ClosePastPremiereConsultationCommand = new RelayCommand(_ =>
            {
                IsReadingPastPremiereConsultationMode = false;
                IsEditingPastPremiere = false;
                ResetWorkspaceModes();
            });

            EditPastPremiereCommand = new RelayCommand(_ =>
            {
                IsEditingPastPremiere = true;
            });

            SavePastPremiereCommand = new RelayCommand(async _ => await SavePastPremiereAsync());

            CancelPastPremiereEditCommand = new RelayCommand(_ =>
            {
                // Recharger depuis le fichier pour annuler les modifications
                if (!string.IsNullOrEmpty(_pastPremiereFilePath) && File.Exists(_pastPremiereFilePath))
                {
                    var content = File.ReadAllText(_pastPremiereFilePath, System.Text.Encoding.UTF8);
                    var yamlHeader = ExtractYamlHeader(content) ?? "";
                    var (_, body) = SplitFrontmatter(content);
                    LoadPastPremiereConsultationData(yamlHeader, body, _pastPremiereFilePath);
                }
                IsEditingPastPremiere = false;
            });

            // Hub : ouvrir une card évaluation (active → Resume, clôturée → ReadOnly + tab SYNTHESE)
            // Implémentation complète à l'étape 2.
            OpenEvaluationCardCommand = new RelayCommand(param =>
            {
                if (param is EvaluationCardViewModel ecard)
                    OpenEvaluationCard(ecard);
            });

            // Hub : supprimer une card évaluation (utile pour nettoyer les tests)
            DeleteEvaluationCardCommand = new RelayCommand(param =>
            {
                if (param is EvaluationCardViewModel ecard)
                    DeleteEvaluationCard(ecard);
            });

            // Hub : ouvrir une carte de Synthèse Globale (brouillon = édition, validée = lecture seule)
            OpenSyntheseGlobaleCardCommand = new RelayCommand(param =>
            {
                if (param is SyntheseGlobaleCardViewModel sc)
                    OpenSyntheseGlobaleCard(sc);
            });

            // Hub : ouvrir une carte de Projet Thérapeutique
            OpenProjetTherapeutiqueCardCommand = new RelayCommand(param =>
            {
                if (param is ProjetTherapeutiqueCardViewModel pc)
                    OpenProjetTherapeutiqueCard(pc);
            });

            // Hub : supprimer une carte de Synthèse Globale
            DeleteSyntheseGlobaleCardCommand = new RelayCommand(param =>
            {
                if (param is SyntheseGlobaleCardViewModel sc)
                    DeleteSyntheseGlobaleCard(sc);
            });

            // Hub : supprimer une carte de Projet Thérapeutique
            DeleteProjetTherapeutiqueCardCommand = new RelayCommand(param =>
            {
                if (param is ProjetTherapeutiqueCardViewModel pc)
                    DeleteProjetTherapeutiqueCard(pc);
            });

            // V0b : Commande confirmation âge
            ConfirmAgeCommand = new RelayCommand(param =>
            {
                if (param is int age)
                    ConfirmedAge = age;
                else if (param is string ageStr && int.TryParse(ageStr, out var parsedAge))
                    ConfirmedAge = parsedAge;
            }, _ => IsInterrogatoireMode);

            // V0b : Commande motif manuel
            SetMotifManuallyCommand = new RelayCommand(param =>
            {
                if (param is string motif && !string.IsNullOrWhiteSpace(motif))
                    _motifDetector.SetMotifManually(motif);
            }, _ => IsInterrogatoireMode && !HasDetectedMotif);

            // V0c : Commande basculer visibilité bloc
            ToggleBlockVisibilityCommand = new RelayCommand(param =>
            {
                if (param is ConsultationBlockViewModel vm)
                {
                    vm.ToggleHidden();
                    UpdateBlockCollections();
                }
            }, _ => IsInterrogatoireMode);

            // V0c : Commandes Observations Cliniques
            SwitchToInterrogatoireCommand = new RelayCommand(_ =>
            {
                ScheduleDraftSave();
                ResetWorkspaceModes();
            }, _ => IsInterrogatoireMode);

            SwitchToClinicalCommand = new RelayCommand(_ =>
            {
                if (_clinicalObservations.Cards.Count == 0)
                    InitializeClinicalObservations();
                ScheduleDraftSave();
                ResetWorkspaceModes();
                IsInClinicalMode = true;
            }, _ => IsInterrogatoireMode);

            OpenFormulaireParentsCommand = new RelayCommand(_ =>
            {
                ScheduleDraftSave();
                ResetWorkspaceModes();
                FormulaireStatusMessage = "";
                IsFormulaireMode = true;
            }, _ => IsInterrogatoireMode && HasPatient);

            GenererFormulaireCommand = new RelayCommand(
                async _ => await GenerateFormulaireParentsAsync(),
                _ => IsFormulaireMode && !IsFormulaireOcrProcessing);

            ImportFormulaireImageCommand = new RelayCommand(_ =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title  = "Sélectionner la photo du formulaire",
                    Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                };
                if (dlg.ShowDialog() == true)
                    _ = ScanFormulaireAsync(dlg.FileName);
            }, _ => IsFormulaireMode && !IsFormulaireOcrProcessing);

            CameraFormulaireCommand = new RelayCommand(_ =>
            {
                try
                {
                    var cameraRoll = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Camera Roll");
                    var beforeCapture = DateTime.Now.AddSeconds(-5);
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true });
                    System.Windows.MessageBox.Show(
                        "Photographiez le formulaire rempli par les parents.\n\n" +
                        "1. Prenez la photo.\n2. Fermez l'application Caméra.\n3. Cliquez OK.",
                        "Scanner le formulaire",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    if (Directory.Exists(cameraRoll))
                    {
                        var newest = new System.IO.DirectoryInfo(cameraRoll)
                            .GetFiles("*.*")
                            .Where(f => f.Extension.ToLower() is ".jpg" or ".jpeg" or ".png")
                            .OrderByDescending(f => f.CreationTime)
                            .FirstOrDefault();
                        if (newest != null && newest.CreationTime >= beforeCapture)
                            _ = ScanFormulaireAsync(newest.FullName);
                        else
                            System.Windows.MessageBox.Show(
                                "Aucune nouvelle photo détectée dans la Pellicule.\nAssurez-vous d'avoir pris la photo.",
                                "Photo non trouvée",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    FormulaireStatusMessage = $"❌ Caméra indisponible : {ex.Message}";
                }
            }, _ => IsFormulaireMode && !IsFormulaireOcrProcessing);

            CloseFormulaireCommand = new RelayCommand(_ =>
            {
                IsFormulaireMode = false;
                FormulaireOcrResult = "";
                FormulaireStatusMessage = "";
            }, _ => IsFormulaireMode);

            SelectObservationCommand = new RelayCommand(param =>
            {
                // Format attendu du paramètre depuis le XAML : object[] avec [card, option]
                // Ou directement un tuple ValueTuple<ClinicalObservationCard, string>
                if (param is object[] arr && arr.Length >= 2)
                {
                    if (arr[0] is ClinicalObservationCard card && arr[1] is string option)
                        SelectObservationOption(card, option);
                }
                else if (param is ValueTuple<ClinicalObservationCard, string> tuple)
                {
                    SelectObservationOption(tuple.Item1, tuple.Item2);
                }
            }, _ => IsInClinicalMode);

            ToggleCardExpandCommand = new RelayCommand(param =>
            {
                if (param is ClinicalObservationCard card)
                    ToggleCardExpand(card);
            }, _ => IsInClinicalMode);

            TerminateClinicalObservationsCommand = new RelayCommand(async _ =>
            {
                await TerminateClinicalObservationsAsync();
            }, _ => IsInClinicalMode);

            GenerateSuggestionsCommand = new RelayCommand(
                async _ => await GenerateObservationSuggestionsAsync(),
                _ => IsInClinicalMode && !IsGeneratingSuggestions && !string.IsNullOrWhiteSpace(NoteContent));

            SaveObservationsNoteCommand = new RelayCommand(
                async _ => await SaveObservationsNoteAsync(),
                _ => IsInObservationsReviewMode && HasPatient
                     && !string.IsNullOrWhiteSpace(ObservationsNarrative) && !_observationsNoteSaved);

            BackToObservationsCardsCommand = new RelayCommand(
                _ => BackToObservationsCards(),
                _ => IsInObservationsReviewMode);

            // Commands Synthèse Initiale V0d
            // TODO: Phase D — retourner à: IsInterrogatoireMode && IsStructureFrozen
            // Pour tests: juste IsInterrogatoireMode
            SwitchToSynthesisCommand = new RelayCommand(_ =>
            {
                ScheduleDraftSave();
                SwitchToSynthesis();
            }, _ => IsInterrogatoireMode);

            ProposeWeightsCommand = new RelayCommand(async _ =>
            {
                await ProposeWeightsAsync();
            }, _ => IsSynthesisMode && !AreWeightsLoading);

            GenerateSynthesisCommand = new RelayCommand(async _ =>
            {
                await GenerateSynthesisAsync();
            }, _ => IsSynthesisMode && !IsGeneratingSynthesis);

            SaveSynthesisCommand = new RelayCommand(async _ =>
            {
                await SaveSynthesisAsync();
            }, _ => IsSynthesisMode && !string.IsNullOrEmpty(SynthesisContent));

            // V0d : Commands Documents Importés
            ImportDocumentCommand = new RelayCommand(async _ =>
            {
                await ImportDocumentAsync();
            }, _ => IsSynthesisMode && !IsImportingDocument);

            RemoveDocumentCommand = new RelayCommand(param =>
            {
                if (param is ImportedConsultationDocument doc)
                {
                    ImportedDocuments.Remove(doc);
                }
            });

            // V0e : Commands Restitution
            SwitchToRestitutionCommand = new RelayCommand(
                _ => SwitchToRestitution(),
                _ => CurrentPatient != null);

            StartRestitutionCommand = new RelayCommand(
                param => StartRestitution(param as string ?? ""),
                param => !string.IsNullOrEmpty(param as string));

            GenerateRestitutionCommand = new RelayCommand(
                async _ => await GenerateRestitutionAsync(),
                _ => IsRestitutionFormMode && !IsGeneratingRestitution);

            ConfirmRestitutionCommand = new RelayCommand(
                async _ => await ConfirmRestitutionAsync(),
                _ => IsRestitutionReviewMode && !IsGeneratingRestitution);

            BackToRestitutionFormCommand = new RelayCommand(
                _ => { IsRestitutionReviewMode = false; RestitutionStatusMessage = ""; },
                _ => IsRestitutionReviewMode);

            OpenRestitutionCommand = new RelayCommand(
                _ => OpenRestitutionFile(),
                _ => IsRestitutionMode &&
                     (!string.IsNullOrEmpty(_restitution.GeneratedPdfPath) || !string.IsNullOrEmpty(_restitution.GeneratedHtmlPath)));

            // Commands Consultation de Suivi
            ExtractSuiviCommand = new RelayCommand(
                async _ => await ExtractSuiviAsync(),
                _ => IsSuiviMode && !IsExtractingSuivi && !Suivi.RAS && !string.IsNullOrWhiteSpace(Suivi.Transcription));

            SaveSuiviCommand = new RelayCommand(
                async _ => await SaveSuiviNoteAsync(),
                _ => IsSuiviMode && (Suivi.RAS || Suivi.Renouvellement || Suivi.PasEffetsSecondaires ||
                                     Suivi.AdhesionOk || Suivi.EvolutionScolaire || Suivi.SommeilCorrect ||
                                     Suivi.ARevoir || !string.IsNullOrWhiteSpace(Suivi.AIExtraction)));

            ResumeSuiviCommand  = new RelayCommand(_ => ResumeSuivi(),  _ => HasSuiviInProgress && !IsEditingConsultation);

            // Édition de la synthèse globale (dossier bleu)
            StartEditSynthesisCommand  = new RelayCommand(_ => StartEditSynthesis(),  _ => !IsSynthesisEditing);
            CancelEditSynthesisCommand = new RelayCommand(_ => CancelEditSynthesis(), _ => IsSynthesisEditing);
            SaveEditSynthesisCommand   = new RelayCommand(_ => SaveEditSynthesis(),   _ => IsSynthesisEditing);
            OpenDocumentDetailCommand        = new RelayCommand(OpenDocumentDetail);
            OpenPdfFileCommand               = new RelayCommand(OpenPdfFile);
            DeletePatientDocumentCommand     = new RelayCommand(DeletePatientDocument);
            SaisirFormulaireCommand          = new RelayCommand(SaisirFormulaire);

            // Drawer "Patients récents" (bord gauche)
            ToggleRecentDrawerCommand  = new RelayCommand(_ => ToggleRecentDrawer());
            CloseRecentDrawerCommand   = new RelayCommand(_ => IsRecentDrawerOpen = false);
            SelectRecentPatientCommand = new RelayCommand(SelectRecentPatient);
            DiscardSuiviCommand = new RelayCommand(_ => DiscardSuivi(), _ => HasSuiviInProgress);

            // S'abonner aux changements internes du Suivi pour réévaluer HasSuiviInProgress
            _suivi.PropertyChanged += OnSuiviInnerPropertyChanged;

            // Commands pagination dossier
            InitPaginationCommands();

            // Charger des données placeholder pour tester l'UI
            LoadPlaceholderData();
        }

        #endregion

        #region Methods

        public void SetDossierDataService(DossierDataService service)
        {
            _dossierDataService = service;
        }

        private DossierDataService _dossierDataService = new DossierDataService(new PathService());

        public RestitutionsHubViewModel RestitutionsHub { get; private set; }

        public ObservableCollection<ConsultationNoteViewModel> ConsultationNotes { get; } = new();

        // Frise chronologique (hub de consultations)
        public ObservableCollection<ConsultationCardViewModel> ConsultationCards { get; } = new();

        // Ligne fixe : étapes séquentielles du parcours patient (3-11 ans)
        public ObservableCollection<FriseStageViewModel> FriseStages { get; } = new();

        // Frise chronologique (cards d'évaluation — active + clôturées, en parallèle des consultations)
        public ObservableCollection<EvaluationCardViewModel> EvaluationCards { get; } = new();

        // Frise chronologique (cards de Synthèse Globale — brouillon courant + versions validées)
        public ObservableCollection<SyntheseGlobaleCardViewModel> SyntheseGlobaleCards { get; } = new();

        // Frise chronologique (cards de Projet Thérapeutique)
        public ObservableCollection<ProjetTherapeutiqueCardViewModel> ProjetTherapeutiqueCards { get; } = new();

        // Blocs d'affichage Projet Thérapeutique (versions validées) — onglet PROJET du dossier bleu.
        // Trié du plus récent au plus ancien.
        public ObservableCollection<ProjetTherapeutiqueBilanCardViewModel> ProjetTherapeutiqueBlocks { get; } = new();
        public bool HasProjetTherapeutiqueBlocks => ProjetTherapeutiqueBlocks.Count > 0;

        // Blocs de Synthèse Globale (versions validées) affichés EN PREMIER dans l'onglet
        // SYNTHESE du dossier bleu, AVANT les blocs Bilan Final des évaluations.
        // Trié du plus récent au plus ancien (version la plus récente d'abord).
        public ObservableCollection<SyntheseGlobaleBilanCardViewModel> SyntheseGlobaleBlocks { get; } = new();
        public bool HasSyntheseGlobaleBlocks => SyntheseGlobaleBlocks.Count > 0;

        // V0.3 — Tracker incrémental : indique si une mise à jour de la Synthèse Globale
        // est recommandée (poids accumulé ≥ 1.0 depuis la dernière synthèse validée).
        private bool _syntheseUpdateRecommandee;
        public bool SyntheseUpdateRecommandee
        {
            get => _syntheseUpdateRecommandee;
            private set
            {
                if (_syntheseUpdateRecommandee != value)
                {
                    _syntheseUpdateRecommandee = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SyntheseUpdateBadge));
                }
            }
        }
        /// <summary>Texte du tooltip / badge sur le bouton + indiquant le nombre d'éléments en attente.</summary>
        public string SyntheseUpdateBadge => _syntheseUpdateBadgeText;
        private string _syntheseUpdateBadgeText = "";

        // Blocs de synthèse diagnostique (issus des évaluations clôturées) affichés
        // dans l'onglet SYNTHESE du dossier bleu, SOUS la synthèse rédigée manuellement.
        // Trié du plus récent au plus ancien.
        public ObservableCollection<DiagnosticSyntheseCardViewModel> DiagnosticSyntheseBlocks { get; } = new();
        public bool HasDiagnosticSyntheseBlocks => DiagnosticSyntheseBlocks.Count > 0;

        // Cartographies (étape 4) issues des évaluations clôturées, affichées dans l'onglet
        // BILANS du dossier bleu. La cartographie de l'enfant EST un bilan d'évaluation.
        // Trié du plus récent au plus ancien.
        public ObservableCollection<CartographieBilanCardViewModel> CartographieBilans { get; } = new();
        public bool HasCartographieBilans => CartographieBilans.Count > 0;

        // Cartographies de l'environnement (étape 5) issues des évaluations clôturées.
        public ObservableCollection<CartographieEnvironnementBilanCardViewModel> CartographieEnvironnementBilans { get; } = new();
        public bool HasCartographieEnvironnementBilans => CartographieEnvironnementBilans.Count > 0;

        public bool HasNoConsultationNotes => ConsultationNotes.Count == 0;
        public bool HasConsultationNotes   => ConsultationNotes.Count > 0;

        private async Task RefreshConsultationNotesAsync()
        {
            if (CurrentPatient == null) return;
            var patientSnapshot = CurrentPatient;
            var pages = await _dossierDataService.LoadConsultationsAsync(patientSnapshot.NomComplet);
            if (CurrentPatient != patientSnapshot) return; // patient changé pendant l'attente IO
            ConsultationNotes.Clear();
            foreach (var p in pages)
            {
                var noteVm = new ConsultationNoteViewModel(p.Title, p.FilePath ?? "", p.Content ?? "");
                noteVm.DeleteRequested += vm =>
                {
                    ConsultationNotes.Remove(vm);
                    OnPropertyChanged(nameof(HasNoConsultationNotes));
                    OnPropertyChanged(nameof(HasConsultationNotes));
                    _singlePageIndex = Math.Max(0, Math.Min(_singlePageIndex, ConsultationNotes.Count - 1));
                    _spreadIndex     = Math.Max(0, Math.Min(_spreadIndex,     SpreadCount - 1));
                    NotifyPageChange();
                    NotifySpreadChange();
                };
                ConsultationNotes.Add(noteVm);
            }
            OnPropertyChanged(nameof(HasNoConsultationNotes));
            OnPropertyChanged(nameof(HasConsultationNotes));
            ResetPagination();

            LoadConsultationCards();
            LoadEvaluationCards();
            LoadSyntheseGlobaleCards();
            LoadProjetTherapeutiqueCards();
            RefreshSyntheseUpdateBadge();
        }

        /// <summary>
        /// Alimente la frise chronologique à partir des notes chargées,
        /// triées de la plus ancienne à la plus récente.
        /// </summary>
        private void LoadConsultationCards()
        {
            ConsultationCards.Clear();
            var cards = ConsultationNotes
                .Select(n => new ConsultationCardViewModel(n.Title, n.FilePath))
                .OrderBy(c => c.Date)
                .ToList();
            foreach (var c in cards)
                ConsultationCards.Add(c);

            OnPropertyChanged(nameof(HasNoConsultationCards));
            OnPropertyChanged(nameof(SuiviCards));
            OnPropertyChanged(nameof(HasNoSuiviCards));
            RefreshFriseStages();
        }

        /// <summary>
        /// Reconstruit la ligne fixe du parcours patient (3-11 ans) à partir de l'état actuel
        /// des collections (consultations, évaluations, synthèses, projets, documents).
        /// Appelé après chaque LoadXxx() pour maintenir la frise à jour.
        /// </summary>
        private void RefreshFriseStages()
        {
            FriseStages.Clear();

            // ── Étape 1 : 1ère consultation ────────────────────────────────────────────
            var premiereCard = ConsultationCards.FirstOrDefault(c => c.Type == "1ère consultation");
            bool premiereCompleted = premiereCard != null;

            // Clôturée → lecture seule  |  disponible → nouvelle consultation
            var premiereCmd = premiereCompleted && premiereCard != null
                ? new Commands.RelayCommand(_ => OpenPastConsultation(premiereCard))
                : (System.Windows.Input.ICommand)NewConsultationCommand;

            FriseStages.Add(new FriseStageViewModel
            {
                Key    = "premiere",
                Label  = "1ère consultation",
                Icon   = "🩺",
                Status = premiereCompleted ? FriseStageStatus.Completed : FriseStageStatus.Available,
                Date   = premiereCard?.Date,
                ActivateCommand = premiereCmd
            });

            // Pour l'instant seul le parcours 3-11 ans est défini
            bool age3to11 = _confirmedAge.HasValue && _confirmedAge >= 3 && _confirmedAge <= 11;
            if (!premiereCompleted || !age3to11)
            {
                FriseStages[0].ShowArrow = false;
                OnPropertyChanged(nameof(FriseStages));
                return;
            }

            // ── Étape 2 : Évaluation ────────────────────────────────────────────────────
            bool evalCompleted  = EvaluationCards.Any(c => c.IsClosed);
            bool evalInProgress = EvaluationCards.Any(c => c.IsActive);
            var  evalDate       = evalCompleted
                ? EvaluationCards.Where(c => c.IsClosed).OrderByDescending(c => c.DateCloture).FirstOrDefault()?.DateCloture
                : EvaluationCards.FirstOrDefault(c => c.IsActive)?.Date;

            // Évaluation : Completed/InProgress → ouvre la carte existante ; Available → nouvelle
            var closedEvalCard  = EvaluationCards.Where(c => c.IsClosed).OrderByDescending(c => c.DateCloture).FirstOrDefault();
            var activeEvalCard  = EvaluationCards.FirstOrDefault(c => c.IsActive);
            System.Windows.Input.ICommand evalCmd =
                evalCompleted  && closedEvalCard != null ? new Commands.RelayCommand(_ => OpenEvaluationCard(closedEvalCard)) :
                evalInProgress && activeEvalCard  != null ? new Commands.RelayCommand(_ => OpenEvaluationCard(activeEvalCard))  :
                new Commands.RelayCommand(_ => NewConsultationCommand.Execute("evaluation"));

            FriseStages.Add(new FriseStageViewModel
            {
                Key    = "evaluation",
                Label  = "Évaluation",
                Icon   = "📋",
                Status = evalCompleted  ? FriseStageStatus.Completed :
                         evalInProgress ? FriseStageStatus.InProgress : FriseStageStatus.Available,
                Date   = evalDate,
                ActivateCommand = evalCmd
            });

            // ── Étape 3 : Synthèse ──────────────────────────────────────────────────────
            bool synthCompleted  = SyntheseGlobaleCards.Any(c => c.IsValidee);
            bool synthInProgress = SyntheseGlobaleCards.Any(c => c.IsActive);
            var  synthDate       = synthCompleted
                ? SyntheseGlobaleCards.Where(c => c.IsValidee).OrderByDescending(c => c.Date).FirstOrDefault()?.Date
                : SyntheseGlobaleCards.FirstOrDefault(c => c.IsActive)?.Date;

            var validSynthCard  = SyntheseGlobaleCards.Where(c => c.IsValidee).OrderByDescending(c => c.Date).FirstOrDefault();
            var activeSynthCard = SyntheseGlobaleCards.FirstOrDefault(c => c.IsActive);
            System.Windows.Input.ICommand synthCmd =
                synthCompleted  && validSynthCard  != null ? new Commands.RelayCommand(_ => OpenSyntheseGlobaleCard(validSynthCard))  :
                synthInProgress && activeSynthCard != null ? new Commands.RelayCommand(_ => OpenSyntheseGlobaleCard(activeSynthCard)) :
                new Commands.RelayCommand(_ => NewConsultationCommand.Execute("synthese_globale"), _ => evalCompleted);

            FriseStages.Add(new FriseStageViewModel
            {
                Key    = "synthese",
                Label  = "Synthèse",
                Icon   = "🧭",
                Status = !evalCompleted  ? FriseStageStatus.Locked :
                         synthCompleted  ? FriseStageStatus.Completed :
                         synthInProgress ? FriseStageStatus.InProgress : FriseStageStatus.Available,
                Date   = synthDate,
                ActivateCommand = synthCmd
            });

            // ── Étape 4 : Projet thérapeutique ──────────────────────────────────────────
            bool projetCompleted  = ProjetTherapeutiqueCards.Any(c => c.IsValidee);
            bool projetInProgress = ProjetTherapeutiqueCards.Any(c => c.IsActive);
            var  projetDate       = projetCompleted
                ? ProjetTherapeutiqueCards.Where(c => c.IsValidee).OrderByDescending(c => c.DateValidation).FirstOrDefault()?.DateValidation
                : ProjetTherapeutiqueCards.FirstOrDefault(c => c.IsActive)?.Date;

            var validProjetCard  = ProjetTherapeutiqueCards.Where(c => c.IsValidee).OrderByDescending(c => c.DateValidation).FirstOrDefault();
            var activeProjetCard = ProjetTherapeutiqueCards.FirstOrDefault(c => c.IsActive);
            System.Windows.Input.ICommand projetCmd =
                projetCompleted  && validProjetCard  != null ? new Commands.RelayCommand(_ => OpenProjetTherapeutiqueCard(validProjetCard))  :
                projetInProgress && activeProjetCard != null ? new Commands.RelayCommand(_ => OpenProjetTherapeutiqueCard(activeProjetCard)) :
                new Commands.RelayCommand(_ => NewConsultationCommand.Execute("projet_therapeutique"), _ => synthCompleted);

            FriseStages.Add(new FriseStageViewModel
            {
                Key    = "projet",
                Label  = "Projet thérapeutique",
                Icon   = "🎯",
                Status = !synthCompleted  ? FriseStageStatus.Locked :
                         projetCompleted  ? FriseStageStatus.Completed :
                         projetInProgress ? FriseStageStatus.InProgress : FriseStageStatus.Available,
                Date   = projetDate,
                ActivateCommand = projetCmd
            });

            // ── Étape 5 : Restitution ────────────────────────────────────────────────────
            bool restitutionCompleted = false;
            if (CurrentPatient != null && !string.IsNullOrEmpty(CurrentPatient.DirectoryPath))
            {
                try
                {
                    foreach (var yearDir in System.IO.Directory.GetDirectories(CurrentPatient.DirectoryPath))
                    {
                        var rDir = System.IO.Path.Combine(yearDir, "restitutions");
                        if (System.IO.Directory.Exists(rDir) && System.IO.Directory.GetFiles(rDir, "*.pdf").Length > 0)
                        { restitutionCompleted = true; break; }
                    }
                }
                catch { }
            }

            FriseStages.Add(new FriseStageViewModel
            {
                Key    = "restitution",
                Label  = "Restitution",
                Icon   = "📝",
                Status = !projetCompleted     ? FriseStageStatus.Locked :
                         restitutionCompleted ? FriseStageStatus.Completed : FriseStageStatus.Available,
                ActivateCommand = new Commands.RelayCommand(_ => SelectDossierTabCommand.Execute("Documents"),
                                                            _ => projetCompleted)
            });

            // ── Étape 6 : Bilan semestriel ───────────────────────────────────────────────
            FriseStages.Add(new FriseStageViewModel
            {
                Key    = "bilan_s",
                Label  = "Bilan semestriel",
                Icon   = "📅",
                Status = restitutionCompleted ? FriseStageStatus.Available : FriseStageStatus.Locked,
                ActivateCommand = new Commands.RelayCommand(_ => { }, _ => false)
            });

            // ── Étape 7 : Bilan annuel ────────────────────────────────────────────────────
            FriseStages.Add(new FriseStageViewModel
            {
                Key       = "bilan_a",
                Label     = "Bilan annuel",
                Icon      = "🗓",
                Status    = FriseStageStatus.Locked,
                ShowArrow = false,
                ActivateCommand = new Commands.RelayCommand(_ => { }, _ => false)
            });

            OnPropertyChanged(nameof(FriseStages));
            OnPropertyChanged(nameof(HasFriseStages));
        }

        /// <summary>
        /// Alimente la frise des évaluations (actives + clôturées) ET les blocs de synthèse
        /// diagnostique du dossier SYNTHESE, à partir du dossier patient.
        /// Cards triées par DateDebut (ancienne → récente). Blocs de synthèse triés du plus
        /// récent au plus ancien (les dernières conclusions d'abord).
        /// </summary>
        private void LoadEvaluationCards()
        {
            EvaluationCards.Clear();
            DiagnosticSyntheseBlocks.Clear();
            CartographieBilans.Clear();
            CartographieEnvironnementBilans.Clear();
            if (_evaluationPhaseService == null || CurrentPatient == null
                || string.IsNullOrEmpty(CurrentPatient.DirectoryPath)) return;

            var phases = _evaluationPhaseService.LoadAll(CurrentPatient.DirectoryPath);
            foreach (var p in phases)
                EvaluationCards.Add(new EvaluationCardViewModel(p));

            // Synthèses : uniquement les évaluations clôturées avec au moins un diagnostic
            // retenu OU une certitude renseignée OU un élément en faveur OU un écarté.
            // (Pas de bruit pour les évaluations abandonnées sans synthèse.)
            var blocks = phases
                .Where(p => !p.IsActive)
                .Where(p => p.BilanFinal.DiagnosticsRetenus.Any(s => !string.IsNullOrWhiteSpace(s?.Value))
                         || p.BilanFinal.ElementsEnFaveur.Any(s => !string.IsNullOrWhiteSpace(s?.Value))
                         || p.BilanFinal.DiagnosticsEcartes.Any(e => !string.IsNullOrWhiteSpace(e?.Label))
                         || p.BilanFinal.Certitude != NiveauCertitude.NonRenseigne)
                .OrderByDescending(p => p.DateCloture ?? p.DateDerniereModif)
                .Select(p => new DiagnosticSyntheseCardViewModel(p))
                .ToList();
            foreach (var b in blocks)
                DiagnosticSyntheseBlocks.Add(b);

            // Cartographies : évaluations clôturées avec l'étape 4 validée OU au moins du contenu
            // (score > 0 sur un segment ou tempérament renseigné). Affichées dans l'onglet BILANS.
            var cartoBlocks = phases
                .Where(p => !p.IsActive)
                .Where(p => p.CartographieEnfant.IsValidated
                         || p.CartographieEnfant.Attachement.Score > 0
                         || p.CartographieEnfant.Psychomotricite.IsRenseigne
                         || p.CartographieEnfant.Langage.Score > 0
                         || p.CartographieEnfant.Emotions.Score > 0
                         || p.CartographieEnfant.Imaginaire.Score > 0
                         || p.CartographieEnfant.Pensee.Score > 0
                         || p.CartographieEnfant.Temperament.IsRenseigne
                         || p.CartographieEnfant.Attention.IsRenseigne)
                .OrderByDescending(p => p.DateCloture ?? p.DateDerniereModif)
                .Select(p => new CartographieBilanCardViewModel(p))
                .ToList();
            foreach (var b in cartoBlocks)
                CartographieBilans.Add(b);

            // Cartographies de l'environnement (étape 5) : évaluations clôturées avec étape 5
            // validée OU au moins une nervure avec score > 0.
            var envBlocks = phases
                .Where(p => !p.IsActive)
                .Where(p => p.CartographieEnvironnement.IsValidated || HasAnyEnvironnementScore(p.CartographieEnvironnement))
                .OrderByDescending(p => p.DateCloture ?? p.DateDerniereModif)
                .Select(p => new CartographieEnvironnementBilanCardViewModel(p))
                .ToList();
            foreach (var b in envBlocks)
                CartographieEnvironnementBilans.Add(b);

            OnPropertyChanged(nameof(HasNoTimelineCards));
            OnPropertyChanged(nameof(HasDiagnosticSyntheseBlocks));
            OnPropertyChanged(nameof(HasCartographieBilans));
            OnPropertyChanged(nameof(HasCartographieEnvironnementBilans));
            RefreshFriseStages();
        }

        /// <summary>
        /// Alimente la frise des Synthèses Globales (brouillon courant + versions validées)
        /// ET les blocs de l'onglet SYNTHESE du dossier bleu (versions validées seulement).
        /// Cards triées par date de rédaction (ancienne → récente).
        /// Blocs triés du plus récent au plus ancien (version la plus récente en haut).
        /// </summary>
        private void LoadSyntheseGlobaleCards()
        {
            SyntheseGlobaleCards.Clear();
            SyntheseGlobaleBlocks.Clear();

            if (_syntheseGlobaleService == null || CurrentPatient == null
                || string.IsNullOrEmpty(CurrentPatient.NomComplet)) return;

            var versions = _syntheseGlobaleService.ListVersions(CurrentPatient.NomComplet);

            // Frise : toutes les versions, ancienne → récente
            foreach (var v in versions.OrderBy(x => x.DateRedaction))
                SyntheseGlobaleCards.Add(new SyntheseGlobaleCardViewModel(v));

            // Blocs SYNTHESE : versions validées, plus récente d'abord
            foreach (var v in versions.Where(x => x.IsValidee).OrderByDescending(x => x.Version))
            {
                var full = _syntheseGlobaleService.Load(v.FilePath);
                if (full != null)
                    SyntheseGlobaleBlocks.Add(new SyntheseGlobaleBilanCardViewModel(full));
            }

            OnPropertyChanged(nameof(HasSyntheseGlobaleBlocks));
            OnPropertyChanged(nameof(HasNoTimelineCards));
            RefreshFriseStages();
        }

        private static bool HasAnyEnvironnementScore(MedCompanion.Models.Evaluations.CartographieEnvironnement carto)
        {
            if (FeuilleHasAnyScore(carto.Famille))           return true;
            if (FeuilleHasAnyScore(carto.EcolePairs))        return true;
            if (FeuilleHasAnyScore(carto.EcransMedias))      return true;
            if (FeuilleHasAnyScore(carto.ValeursSocietales)) return true;
            if (FeuilleHasAnyScore(carto.CadreEducatif))     return true;
            return false;
        }

        private static bool FeuilleHasAnyScore(MedCompanion.Models.Evaluations.FeuilleEnvironnement f)
        {
            if (f.NervureCentrale.Score > 0) return true;
            foreach (var s in f.NervuresSecondaires) if (s.Score > 0) return true;
            return false;
        }

        public bool HasNoConsultationCards => ConsultationCards.Count == 0;
        public bool HasNoTimelineCards     => ConsultationCards.Count == 0 && EvaluationCards.Count == 0 && SyntheseGlobaleCards.Count == 0 && ProjetTherapeutiqueCards.Count == 0;

        // Ligne libre : notes de suivi uniquement (exclut la 1ère consultation)
        public IEnumerable<ConsultationCardViewModel> SuiviCards =>
            ConsultationCards.Where(c => c.Type != "1ère consultation");
        public bool HasNoSuiviCards  => !SuiviCards.Any();
        // Vrai dès qu'il y a au moins 1 jalon dans la ligne fixe (pour afficher le connecteur)
        public bool HasFriseStages   => FriseStages.Count > 0;

        #endregion

        #region Pagination CONSULT

        private int _singlePageIndex = 0;
        private int _spreadIndex     = 0;

        private int SpreadCount =>
            ConsultationNotes.Count == 0 ? 0 : (int)Math.Ceiling(ConsultationNotes.Count / 2.0);

        public ConsultationNoteViewModel? SinglePageNote =>
            ConsultationNotes.Count > 0 ? ConsultationNotes[Math.Clamp(_singlePageIndex, 0, ConsultationNotes.Count - 1)] : null;

        public ConsultationNoteViewModel? LeftPageNote  =>
            ConsultationNotes.ElementAtOrDefault(_spreadIndex * 2);

        public ConsultationNoteViewModel? RightPageNote =>
            ConsultationNotes.ElementAtOrDefault(_spreadIndex * 2 + 1);

        public bool   HasPrevSingle  => _singlePageIndex > 0;
        public bool   HasNextSingle  => _singlePageIndex < ConsultationNotes.Count - 1;
        public string SinglePageText => ConsultationNotes.Count == 0 ? "–" : $"{_singlePageIndex + 1}/{ConsultationNotes.Count}";

        public bool   HasPrevSpread  => _spreadIndex > 0;
        public bool   HasNextSpread  => _spreadIndex < SpreadCount - 1;
        public string SpreadText
        {
            get
            {
                if (ConsultationNotes.Count == 0) return "–";
                int l = _spreadIndex * 2 + 1;
                int r = Math.Min(l + 1, ConsultationNotes.Count);
                return r == l ? $"{l}/{ConsultationNotes.Count}" : $"{l}–{r}/{ConsultationNotes.Count}";
            }
        }

        public ICommand NextSinglePageCommand  { get; private set; } = null!;
        public ICommand PrevSinglePageCommand  { get; private set; } = null!;
        public ICommand NextSpreadCommand      { get; private set; } = null!;
        public ICommand PrevSpreadCommand      { get; private set; } = null!;
        public ICommand IncreaseFontSizeCommand { get; private set; } = null!;
        public ICommand DecreaseFontSizeCommand { get; private set; } = null!;

        private double _noteFontSize = 12;
        public double NoteFontSize
        {
            get => _noteFontSize;
            set => SetProperty(ref _noteFontSize, Math.Clamp(value, 10, 28));
        }

        private void InitPaginationCommands()
        {
            NextSinglePageCommand   = new RelayCommand(_ => { _singlePageIndex++; NotifyPageChange(); },   _ => HasNextSingle);
            PrevSinglePageCommand   = new RelayCommand(_ => { _singlePageIndex--; NotifyPageChange(); },   _ => HasPrevSingle);
            NextSpreadCommand       = new RelayCommand(_ => { _spreadIndex++;     NotifySpreadChange(); }, _ => HasNextSpread);
            PrevSpreadCommand       = new RelayCommand(_ => { _spreadIndex--;     NotifySpreadChange(); }, _ => HasPrevSpread);
            IncreaseFontSizeCommand = new RelayCommand(_ => NoteFontSize += 2, _ => NoteFontSize < 28);
            DecreaseFontSizeCommand = new RelayCommand(_ => NoteFontSize -= 2, _ => NoteFontSize > 10);
        }

        private void NotifyPageChange()
        {
            OnPropertyChanged(nameof(SinglePageNote));
            OnPropertyChanged(nameof(SinglePageText));
            OnPropertyChanged(nameof(HasPrevSingle));
            OnPropertyChanged(nameof(HasNextSingle));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
        }

        public bool HasRightPageNote => RightPageNote != null;

        private void NotifySpreadChange()
        {
            OnPropertyChanged(nameof(LeftPageNote));
            OnPropertyChanged(nameof(RightPageNote));
            OnPropertyChanged(nameof(HasRightPageNote));
            OnPropertyChanged(nameof(SpreadText));
            OnPropertyChanged(nameof(HasPrevSpread));
            OnPropertyChanged(nameof(HasNextSpread));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
        }

        private void ResetPagination()
        {
            _singlePageIndex = 0;
            _spreadIndex     = 0;
            NotifyPageChange();
            NotifySpreadChange();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Charge le patient pour la consultation
        /// </summary>
        public void LoadPatient(PatientIndexEntry patient)
        {
            if (patient is null) return;
            // Annuler toute sauvegarde de brouillon en cours pour le patient précédent
            _draftSaveCts?.Cancel();
            // Stash le brouillon de Suivi du patient PRÉCÉDENT avant le swap
            StashCurrentSuiviDraftFor(_currentPatient?.NomComplet);

            CurrentPatient = patient;
            _ = RestitutionsHub.LoadForPatientAsync(patient);
            ConsultationDate = DateTime.Now;

            // Restaurer le brouillon de Suivi du NOUVEAU patient (ou créer une instance vierge)
            RestoreSuiviDraftFor(patient?.NomComplet);

            // Réinitialiser l'interrogatoire (blocs, état, textes)
            ConsultationType = ConsultationType.Normal;   // remet les flags IsInterrogatoireMode etc.
            IsEditingConsultation = false;                // retour à la vue frise
            InterrogatoireBlocks.Clear();
            _interrogatoireState = InterrogatoireState.Saisie;
            _noteSaved = false;
            _observationsNoteSaved = false;
            _premiereConsultationFilePath = null;
            TranscriptionInput = "";
            ManualNotes = "";
            NoteContent = "";
            ObservationsNarrative = "";
            ExtractionStatus = "";

            // Reset état Synthèse Initiale pour éviter la persistance entre patients
            IsSynthesisMode = false;
            IsSyntheseGlobaleMode = false;
            IsProjetTherapeutiqueMode = false;
            SynthesisContent = "";
            SynthesisStatusMessage = "";
            ObservationsStatusMessage = "";

            // Reset zone Documents Med pour éviter d'afficher un import du patient précédent
            MedDocumentStatus = "";
            ImportedDocuments.Clear();

            // V0b : réinitialiser l'état adaptatif
            _confirmedAge = null;
            _ageFromInterrogatoire = null;
            OnPropertyChanged(nameof(ConfirmedAge));
            OnPropertyChanged(nameof(HasConfirmedAge));
            _motifDetector.Reset();
            DetectedMotif = "";
            _isAgeConfirmed = false;
            OnPropertyChanged(nameof(HasConfirmedAge));
            BlockSuggestions.Clear();
            OnPropertyChanged(nameof(HasBlockSuggestions));
            IsStructureFrozen = false;

            // Pré-calculer l'âge si DOB disponible (servira de valeur par défaut),
            // mais on EXIGE confirmation via l'interrogatoire — parfois la DOB enregistrée
            // est fausse ou pas du tout mentionnée. Bloc "age" dédié à cette confirmation.
#pragma warning disable CS8602
            if (!string.IsNullOrEmpty(patient.Dob) &&
                DateTime.TryParse(patient.Dob, out var dob))
#pragma warning restore CS8602
            {
                var age = DateTime.Now.Year - dob.Year;
                if (DateTime.Now.DayOfYear < dob.DayOfYear) age--;
                _confirmedAge = age;
                OnPropertyChanged(nameof(ConfirmedAge));
                OnPropertyChanged(nameof(HasConfirmedAge));
            }

            // V0e : réinitialiser la restitution
            IsRestitutionMode = false;
            _restitution = new RestitutionAuxParents();
            OnPropertyChanged(nameof(Restitution));
            RestitutionStatusMessage = "";

            // V0c : réinitialiser les observations cliniques
            IsInClinicalMode = false;
            IsInObservationsReviewMode = false;
            ObservationsNarrative = "";
            ObservationsStatusMessage = "";
            _observationsNoteSaved = false;
            _clinicalObservations.Cards.Clear();
            _clinicalObservations.GeneratedClinicalNarrative = null;

            // Vider immédiatement pour ne pas afficher les notes du patient précédent
            ConsultationNotes.Clear();
            OnPropertyChanged(nameof(HasNoConsultationNotes));
            OnPropertyChanged(nameof(HasConsultationNotes));
            ResetPagination();

            ActiveDossierTab = DossierTab.Couverture;
            LoadSuggestionsForPatient(patient);
            _ = RefreshConsultationNotesAsync();
            LoadPatientSynthesisFromDisk();
            LoadPatientBilansFromDisk();
            LoadPatientDocumentsFromDisk();

            // Reset du mode évaluation au changement de patient (sinon l'UI précédente reste affichée)
            IsEvaluationPhaseMode = false;

            // Recharger l'état de la phase d'évaluation pour ce patient
            EvaluationPhase?.SetCurrentPatient(patient);
        }

        // ── Synthèse globale du patient (dossier bleu, onglet SYNTHESE) ───────

        private string _patientSynthesisText = "";
        /// <summary>
        /// Contenu Markdown de {patient}/synthese/synthese.md (sans le YAML header).
        /// Chargé à chaque LoadPatient et affiché dans le dossier bleu.
        /// </summary>
        public string PatientSynthesisText
        {
            get => _patientSynthesisText;
            set
            {
                if (SetProperty(ref _patientSynthesisText, value))
                {
                    PatientSynthesisDocument = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(value ?? "");
                    OnPropertyChanged(nameof(HasPatientSynthesis));
                    OnPropertyChanged(nameof(HasNoPatientSynthesis));
                }
            }
        }

        private FlowDocument _patientSynthesisDocument = new FlowDocument();
        public FlowDocument PatientSynthesisDocument
        {
            get => _patientSynthesisDocument;
            set => SetProperty(ref _patientSynthesisDocument, value);
        }

        public bool HasPatientSynthesis   => !string.IsNullOrWhiteSpace(_patientSynthesisText);
        public bool HasNoPatientSynthesis => !HasPatientSynthesis;

        // ── Édition de la SYNTHESE ──────────────────────────────────────────
        private bool _isSynthesisEditing;
        public bool IsSynthesisEditing
        {
            get => _isSynthesisEditing;
            set
            {
                if (SetProperty(ref _isSynthesisEditing, value))
                    OnPropertyChanged(nameof(IsSynthesisNotEditing));
            }
        }
        public bool IsSynthesisNotEditing => !_isSynthesisEditing;

        private string _patientSynthesisEditText = "";
        public string PatientSynthesisEditText
        {
            get => _patientSynthesisEditText;
            set => SetProperty(ref _patientSynthesisEditText, value);
        }

        private void StartEditSynthesis()
        {
            // Copie le contenu actuel dans le buffer d'édition (sans YAML)
            PatientSynthesisEditText = _patientSynthesisText;
            IsSynthesisEditing = true;
        }

        private void CancelEditSynthesis()
        {
            IsSynthesisEditing = false;
            PatientSynthesisEditText = "";
        }

        private void SaveEditSynthesis()
        {
            if (_currentPatient == null || string.IsNullOrEmpty(_currentPatient.DirectoryPath)) return;

            try
            {
                var syntheseDir  = Path.Combine(_currentPatient.DirectoryPath, "synthese");
                Directory.CreateDirectory(syntheseDir);
                var synthesePath = Path.Combine(syntheseDir, "synthese.md");

                // Backup avant écrasement
                if (File.Exists(synthesePath))
                {
                    var backup = Path.Combine(syntheseDir, $"synthese_backup_{DateTime.Now:yyyyMMdd_HHmmss}.md");
                    try { File.Copy(synthesePath, backup, true); } catch { }
                }

                // Préserver le YAML header existant si présent, sinon en créer un
                string yamlHeader;
                if (File.Exists(synthesePath))
                {
                    var existing = File.ReadAllText(synthesePath, System.Text.Encoding.UTF8);
                    if (existing.TrimStart().StartsWith("---"))
                    {
                        var first  = existing.IndexOf("---", StringComparison.Ordinal);
                        var second = existing.IndexOf("---", first + 3, StringComparison.Ordinal);
                        yamlHeader = second > 0
                            ? existing.Substring(0, second + 3) + "\n\n"
                            : $"---\ndate_synthese: {DateTime.Now:yyyy-MM-ddTHH:mm:ss}\ntype: edited_manual\n---\n\n";
                    }
                    else
                    {
                        yamlHeader = $"---\ndate_synthese: {DateTime.Now:yyyy-MM-ddTHH:mm:ss}\ntype: edited_manual\n---\n\n";
                    }
                }
                else
                {
                    yamlHeader = $"---\ndate_synthese: {DateTime.Now:yyyy-MM-ddTHH:mm:ss}\ntype: edited_manual\n---\n\n";
                }

                File.WriteAllText(synthesePath, yamlHeader + _patientSynthesisEditText, System.Text.Encoding.UTF8);

                // Recharge la synthèse depuis le disque (regénère aussi le FlowDocument)
                LoadPatientSynthesisFromDisk();
                IsSynthesisEditing = false;
                PatientSynthesisEditText = "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveEditSynthesis] Erreur : {ex.Message}");
            }
        }

        public ICommand StartEditSynthesisCommand  { get; }
        public ICommand CancelEditSynthesisCommand { get; }
        public ICommand SaveEditSynthesisCommand   { get; }

        public ICommand OpenDocumentDetailCommand       { get; private set; } = null!;
        public ICommand OpenPdfFileCommand              { get; private set; } = null!;
        public ICommand DeletePatientDocumentCommand    { get; private set; } = null!;
        public ICommand SaisirFormulaireCommand         { get; private set; } = null!;

        private void OpenDocumentDetail(object? param)
        {
            if (param is not PatientDocumentItem item) return;
            try
            {
                var dlg = new MedCompanion.Dialogs.DocumentDetailDialog(item)
                {
                    Owner = System.Windows.Application.Current?.MainWindow
                };
                dlg.ShowDialog();
                // Si modifié → la dialog a déjà sauvegardé, on rafraîchit la liste
                LoadPatientBilansFromDisk();
                LoadPatientDocumentsFromDisk();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[OpenDocumentDetail] {ex.Message}"); }
        }

        // ── Drawer "Patients récents" (bord gauche, style Doctolib) ──────────

        public ObservableCollection<RecentPatientItem> RecentPatientsForDrawer { get; } = new();

        private bool _isRecentDrawerOpen;
        public bool IsRecentDrawerOpen
        {
            get => _isRecentDrawerOpen;
            set => SetProperty(ref _isRecentDrawerOpen, value);
        }

        public ICommand ToggleRecentDrawerCommand  { get; private set; } = null!;
        public ICommand CloseRecentDrawerCommand   { get; private set; } = null!;
        public ICommand SelectRecentPatientCommand { get; private set; } = null!;

        private void ToggleRecentDrawer()
        {
            if (!IsRecentDrawerOpen)
                RefreshRecentPatientsDrawer();
            IsRecentDrawerOpen = !IsRecentDrawerOpen;
        }

        /// <summary>
        /// Recharge la liste depuis PatientIndexService.GetRecentPatients() (max 20, ordre MRU).
        /// </summary>
        public void RefreshRecentPatientsDrawer()
        {
            RecentPatientsForDrawer.Clear();
            if (_patientIndex == null) return;

            foreach (var p in _patientIndex.GetRecentPatients())
            {
                var last = _patientIndex.GetLastConsultationDate(p.Id);
                RecentPatientsForDrawer.Add(new RecentPatientItem
                {
                    Patient            = p,
                    NomComplet         = p.NomComplet,
                    Initials           = RecentPatientItem.InitialsFor(p.Nom, p.Prenom),
                    AgeDisplay         = p.Age.HasValue ? $"{p.Age} ans" : "",
                    LastConsultDisplay = last.HasValue ? last.Value.ToString("dd/MM/yyyy") : "Jamais",
                    AvatarBrush        = RecentPatientItem.AvatarBrushForName(p.NomComplet)
                });
            }
        }

        private void SelectRecentPatient(object? param)
        {
            if (param is not RecentPatientItem item) return;
            IsRecentDrawerOpen = false;

            // Router via MainWindow pour que TOUT se mette à jour (en-tête patient du haut +
            // tous les panneaux), pas seulement le dossier consultation. MainWindow.LoadPatientAsync
            // appelle déjà LoadPatient + AddRecentPatient. Fallback direct si personne n'écoute.
            if (PatientSwitchRequested != null)
            {
                PatientSwitchRequested.Invoke(this, item.Patient);
            }
            else
            {
                LoadPatient(item.Patient);
                _patientIndex?.AddRecentPatient(item.Patient.Id);  // MRU : remet ce patient en tête
            }
        }

        private void OpenPdfFile(object? param)
        {
            if (param is not PatientDocumentItem item) return;
            if (string.IsNullOrWhiteSpace(item.FilePath))
            {
                System.Windows.MessageBox.Show(
                    "Fichier introuvable :\n" + item.FilePath,
                    "Document",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var pathToOpen = item.FilePath;

            // Cas restitution : le fichier suivi est le .md source. Le bouton « PDF » doit ouvrir le
            // PDF exporté — pas le markdown brut, qui s'ouvre dans LibreOffice et s'affiche en YAML
            // illisible. Si le PDF n'a pas encore été généré (brouillon), on guide l'utilisateur.
            if (pathToOpen.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var pdfPath = Path.ChangeExtension(pathToOpen, ".pdf");
                if (File.Exists(pdfPath))
                {
                    pathToOpen = pdfPath;
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Le PDF de ce dossier de restitution n'a pas encore été généré.\n\n" +
                        "Ouvrez le dossier dans l'éditeur de restitution puis exportez-le en PDF " +
                        "(bouton « 💾 Sauvegarder PDF »).",
                        "PDF non disponible",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            if (!File.Exists(pathToOpen))
            {
                System.Windows.MessageBox.Show(
                    "Fichier introuvable :\n" + pathToOpen,
                    "Document",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = pathToOpen,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Impossible d'ouvrir le fichier :\n" + ex.Message,
                    "Document",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void DeletePatientDocument(object? param)
        {
            if (param is not PatientDocumentItem item) return;

            var result = System.Windows.MessageBox.Show(
                $"Supprimer ce document ?\n\n{item.FileName}",
                "Confirmer la suppression",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                // Pour les restitutions : supprimer le PDF + MD + HTML associés (même base de nom)
                var basePath = Path.Combine(
                    Path.GetDirectoryName(item.FilePath) ?? "",
                    Path.GetFileNameWithoutExtension(item.FilePath));
                foreach (var ext in new[] { ".pdf", ".md", ".html" })
                {
                    var linked = basePath + ext;
                    if (File.Exists(linked))
                        try { File.Delete(linked); } catch { /* meilleur effort */ }
                }
                // Fichier principal s'il n'est pas couvert ci-dessus
                if (File.Exists(item.FilePath))
                    try { File.Delete(item.FilePath); } catch { }

                LoadPatientDocumentsFromDisk();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Erreur lors de la suppression :\n" + ex.Message,
                    "Erreur",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void SaisirFormulaire(object? param)
        {
            if (param is not PatientDocumentItem item) return;
            var dialog = new MedCompanion.Dialogs.FormulaireSaisieDialog(
                item.FilePath,
                _currentPatient?.DirectoryPath);
            dialog.ShowDialog();
        }

        // ── BILANS du dossier bleu (cartes cliquables, une par bilan) ──
        public ObservableCollection<PatientDocumentItem> PatientBilans { get; } = new();
        public bool HasPatientBilans   => PatientBilans.Count > 0;
        public bool HasNoPatientBilans => !HasPatientBilans;

        public void LoadPatientBilansFromDisk()
        {
            PatientBilans.Clear();
            if (_currentPatient == null || string.IsNullOrEmpty(_currentPatient.DirectoryPath))
            {
                OnPropertyChanged(nameof(HasPatientBilans));
                OnPropertyChanged(nameof(HasNoPatientBilans));
                return;
            }

            try
            {
                var year = DateTime.Now.Year;
                var documentsRoot = Path.Combine(_currentPatient.DirectoryPath, year.ToString(), "documents");
                var bilansDir     = Path.Combine(documentsRoot, "bilans");
                var synthesesDir  = Path.Combine(documentsRoot, "syntheses_documents");

                if (!Directory.Exists(bilansDir))
                {
                    OnPropertyChanged(nameof(HasPatientBilans));
                    OnPropertyChanged(nameof(HasNoPatientBilans));
                    return;
                }

                var bilanFiles = Directory.GetFiles(bilansDir).OrderByDescending(f => File.GetCreationTime(f)).ToList();
                foreach (var bilanPath in bilanFiles)
                {
                    var item = BuildDocumentItem(bilanPath, "bilans", synthesesDir);
                    PatientBilans.Add(item);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadBilans] {ex.Message}"); }

            OnPropertyChanged(nameof(HasPatientBilans));
            OnPropertyChanged(nameof(HasNoPatientBilans));
        }

        /// <summary>
        /// Construit un PatientDocumentItem en lisant le fichier original + sa synthèse IA associée.
        /// </summary>
        private PatientDocumentItem BuildDocumentItem(string filePath, string category, string synthesesDir)
        {
            var item = new PatientDocumentItem
            {
                FilePath  = filePath,
                FileName  = Path.GetFileName(filePath),
                Category  = category,
                DateAdded = File.GetCreationTime(filePath)
            };

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            if (Directory.Exists(synthesesDir))
            {
                var matchingSynth = Directory.GetFiles(synthesesDir, $"{baseName}_synthese_*.md")
                                             .OrderByDescending(f => File.GetCreationTime(f))
                                             .FirstOrDefault();
                if (matchingSynth != null)
                {
                    item.SynthesisFilePath = matchingSynth;
                    try
                    {
                        var content = File.ReadAllText(matchingSynth, System.Text.Encoding.UTF8);
                        // Retirer YAML front matter pour la lecture
                        if (content.TrimStart().StartsWith("---"))
                        {
                            var first  = content.IndexOf("---", StringComparison.Ordinal);
                            var second = content.IndexOf("---", first + 3, StringComparison.Ordinal);
                            if (second > 0) content = content.Substring(second + 3).TrimStart();
                        }
                        item.SynthesisContent = content;
                    }
                    catch { }
                }
            }
            return item;
        }

        // ── DOCS du dossier bleu (cartes cliquables, hors bilans) ──
        public ObservableCollection<PatientDocumentItem> PatientDocumentsList { get; } = new();
        public bool HasPatientDocuments   => PatientDocumentsList.Count > 0;
        public bool HasNoPatientDocuments => !HasPatientDocuments;

        public void LoadPatientDocumentsFromDisk()
        {
            PatientDocumentsList.Clear();
            if (_currentPatient == null || string.IsNullOrEmpty(_currentPatient.DirectoryPath))
            {
                OnPropertyChanged(nameof(HasPatientDocuments));
                OnPropertyChanged(nameof(HasNoPatientDocuments));
                return;
            }

            try
            {
                var year          = DateTime.Now.Year;
                var documentsRoot = Path.Combine(_currentPatient.DirectoryPath, year.ToString(), "documents");
                var synthesesDir  = Path.Combine(documentsRoot, "syntheses_documents");

                if (!Directory.Exists(documentsRoot))
                {
                    OnPropertyChanged(nameof(HasPatientDocuments));
                    OnPropertyChanged(nameof(HasNoPatientDocuments));
                    return;
                }

                // Toutes les catégories sauf "bilans" (onglet dédié) et les sous-dossiers techniques
                var excluded = new[] { "bilans", "syntheses_documents", "syntheses" };
                var categoryDirs = Directory.GetDirectories(documentsRoot)
                    .Where(d => !excluded.Contains(Path.GetFileName(d)?.ToLowerInvariant() ?? ""))
                    .OrderBy(d => d)
                    .ToList();

                foreach (var catDir in categoryDirs)
                {
                    var catName = Path.GetFileName(catDir);
                    var files = Directory.GetFiles(catDir).OrderByDescending(f => File.GetCreationTime(f)).ToList();
                    foreach (var docPath in files)
                    {
                        var item = BuildDocumentItem(docPath, catName, synthesesDir);
                        PatientDocumentsList.Add(item);
                    }
                }

                // Restitutions du patient (1er entretien, dossier initial…)
                var restitutionsDir = Path.Combine(_currentPatient.DirectoryPath, year.ToString(), "restitutions");
                if (Directory.Exists(restitutionsDir))
                {
                    var restFiles = Directory.GetFiles(restitutionsDir, "*.md", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => File.GetCreationTime(f))
                        .ToList();
                    foreach (var docPath in restFiles)
                    {
                        var pdfPath = Path.ChangeExtension(docPath, ".pdf");
                        var finalPath = File.Exists(pdfPath) ? pdfPath : docPath;
                        var item = BuildDocumentItem(finalPath, "Restitutions", synthesesDir);
                        PatientDocumentsList.Add(item);
                    }
                }

            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadDocuments] {ex.Message}"); }

            OnPropertyChanged(nameof(HasPatientDocuments));
            OnPropertyChanged(nameof(HasNoPatientDocuments));
            RefreshFriseStages();   // met à jour l'état "Restitution" sur la ligne fixe
        }

        private void LoadPatientSynthesisFromDisk()
        {
            if (_currentPatient == null || string.IsNullOrEmpty(_currentPatient.DirectoryPath))
            {
                PatientSynthesisText = "";
                return;
            }

            var path = Path.Combine(_currentPatient.DirectoryPath, "synthese", "synthese.md");
            if (!File.Exists(path))
            {
                PatientSynthesisText = "";
                return;
            }

            try
            {
                var content = File.ReadAllText(path, System.Text.Encoding.UTF8);

                // Retirer le YAML front matter "--- ... ---" s'il existe
                if (content.TrimStart().StartsWith("---"))
                {
                    var firstDashes = content.IndexOf("---", StringComparison.Ordinal);
                    var secondDashes = content.IndexOf("---", firstDashes + 3, StringComparison.Ordinal);
                    if (secondDashes > 0)
                        content = content.Substring(secondDashes + 3).TrimStart();
                }

                PatientSynthesisText = content;
            }
            catch
            {
                PatientSynthesisText = "";
            }
        }

        /// <summary>
        /// Charge les suggestions de Med pour le patient
        /// </summary>
        private void LoadSuggestionsForPatient(PatientIndexEntry patient)
        {
            Suggestions.Clear();

            // Placeholder - sera connecté au service Med plus tard
            Suggestions.Add(new MedSuggestion
            {
                Icon = "📝",
                Title = "Points à évoquer",
                Content = "Suivi scolaire, RDV bilan prévu",
                Category = "PointAEvoquer"
            });
        }

        /// <summary>
        /// Sauvegarde la note de consultation
        /// </summary>
        private System.Threading.Tasks.Task SaveNoteAsync()
        {
            if (CurrentPatient == null || string.IsNullOrWhiteSpace(NoteContent))
                return System.Threading.Tasks.Task.CompletedTask;

            // TODO: Connecter au service de stockage
            System.Diagnostics.Debug.WriteLine($"[ConsultationMode] Sauvegarde note pour {PatientDisplayName}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Données placeholder pour tester l'UI
        /// </summary>
        private void LoadPlaceholderData()
        {
            // Suggestions de test
            Suggestions.Add(new MedSuggestion
            {
                Icon = "💊",
                Title = "Interactions détectées",
                Content = "Aucune interaction connue",
                Category = "Interaction"
            });

            Suggestions.Add(new MedSuggestion
            {
                Icon = "📝",
                Title = "Points à évoquer",
                Content = "Suivi scolaire, sommeil",
                Category = "PointAEvoquer"
            });

            Suggestions.Add(new MedSuggestion
            {
                Icon = "🎯",
                Title = "Rappel",
                Content = "Dernier RDV: fatigue mentionnée",
                Category = "Rappel"
            });

            // Checklist de test
            ChecklistItems.Add(new ChecklistItem { Text = "Évolution depuis dernier RDV", Source = "auto" });
            ChecklistItems.Add(new ChecklistItem { Text = "Tolérance traitement", Source = "auto" });
            ChecklistItems.Add(new ChecklistItem { Text = "Situation scolaire", Source = "auto" });
            ChecklistItems.Add(new ChecklistItem { Text = "Qualité du sommeil", Source = "auto" });
        }

        #endregion
    }

    public class QualityIssueViewModel : INotifyPropertyChanged
    {
#pragma warning disable CS0067
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

        private readonly ConsultationModeViewModel _parent;
        public QualityIssue Issue { get; }

        public string Original   => Issue.Original;
        public string Suggestion => Issue.Suggestion;
        public string Reason     => Issue.Reason;
        public string BlockTitle => Issue.BlockTitle;

        public string TypeIcon => Issue.Type switch
        {
            QualityIssueType.Medication => "💊",
            QualityIssueType.Coherence  => "⚠",
            _                           => "❓"
        };

        public ICommand AcceptCommand  { get; }
        public ICommand DismissCommand { get; }

        public QualityIssueViewModel(QualityIssue issue, ConsultationModeViewModel parent)
        {
            Issue   = issue;
            _parent = parent;

            AcceptCommand = new RelayCommand(_ =>
            {
                _parent.ApplyQualityIssue(Issue);
                _parent.DismissQualityIssue(this);
            });

            DismissCommand = new RelayCommand(_ => _parent.DismissQualityIssue(this));
        }
    }

    public class PastInterrogatoireBlock
    {
        public string Title { get; set; } = "";
        public string FreeText { get; set; } = "";
    }

    public class PastClinicalCard
    {
        public string Title { get; set; } = "";
        public List<string> SelectedOptions { get; set; } = new();
        public string SelectedOption => SelectedOptions.FirstOrDefault() ?? ""; // compat lecture
        public string FreeText { get; set; } = "";
        public bool HasSelection => SelectedOptions.Count > 0;
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.ViewModels.Restitutions
{
    /// <summary>Ce qu'un champ PT contient — et donc comment il s'édite.</summary>
    public enum PtFieldKind
    {
        /// <summary>Un texte libre.</summary>
        Text,
        /// <summary>Une liste de phrases.</summary>
        List,
        /// <summary>Une liste d'ACTIONS : quoi, qui, quand, à quel degré.</summary>
        Actions,
        /// <summary>L'INDICATION d'une section : est-ce indiqué, à quel degré, par qui, pourquoi.</summary>
        Indication
    }

    // ── Définition d'un champ PT (donnée statique par type de bloc) ───────────
    /// <param name="PorteurParDefaut">
    /// Valeurs prises par une action qu'on vient d'ajouter. Elles ne sont pas neutres : le
    /// médecin ajoute une action pendant la consultation, il doit pouvoir le faire en quelques
    /// secondes et ne corriger que ce qui diffère. Un bilan part donc sur « professionnel à
    /// trouver », un suivi médical sur « le médecin ».
    /// </param>
    public record PtFieldDef(
        string      JsonPath,
        string      Label,
        PtFieldKind Kind,
        string      PorteurParDefaut  = "",
        string      EcheanceParDefaut = "",
        string      DegreParDefaut    = "")
    {
        public bool IsList    => Kind == PtFieldKind.List;
        public bool IsActions => Kind == PtFieldKind.Actions;

        /// <summary>Bilans : la question clinique que l'examen résout.</summary>
        public bool AvecPourTrancher { get; init; }

        /// <summary>Rééducations, interventions, ressources : ce que l'action vise.</summary>
        public bool AvecObjectif { get; init; }

        /// <summary>
        /// Faux pour ce qui s'installe au lieu de se programmer — ressources du quotidien,
        /// stratégies éducatives. Les hiérarchiser comme des soins serait un contresens.
        /// </summary>
        public bool AvecEcheanceEtDegre { get; init; } = true;

        /// <summary>
        /// Dispositifs scolaires : à demander, déjà en place, ou à renouveler. Un dispositif
        /// existant ne se redemande pas — on s'appuie dessus, et ce n'est pas le même texte.
        /// </summary>
        public bool AvecStatut { get; init; }
    }

    /// <summary>
    /// Une action éditable du projet thérapeutique. Les listes de valeurs sont fermées et
    /// exposées ici pour que l'éditeur propose exactement le vocabulaire que le prompt impose —
    /// sans quoi un dossier saisi à la main deviendrait incomparable à un dossier généré.
    /// </summary>
    public class PtActionVm : INotifyPropertyChanged
    {
        public static readonly string[] PorteursPossibles =
            { "le médecin", "les parents", "l'école", "professionnel en place", "professionnel à trouver" };

        public static readonly string[] EcheancesPossibles =
            { "sans attendre", "sous 1 mois", "ce trimestre", "cette année scolaire", "à 6 mois", "à 1 an", "à 2 ans" };

        public static readonly string[] DegresPossibles =
            { "indispensable", "recommandé", "utile si possible", "à réévaluer plus tard" };

        public System.Collections.Generic.IReadOnlyList<string> Porteurs  => PorteursPossibles;
        public System.Collections.Generic.IReadOnlyList<string> Echeances => EcheancesPossibles;
        public System.Collections.Generic.IReadOnlyList<string> Degres    => DegresPossibles;

        private string _quoi = "";
        public string Quoi
        {
            get => _quoi;
            set { if (_quoi == value) return; _quoi = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _porteur = "";
        public string Porteur
        {
            get => _porteur;
            set { if (_porteur == value) return; _porteur = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _echeance = "";
        public string Echeance
        {
            get => _echeance;
            set { if (_echeance == value) return; _echeance = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _degre = "";
        public string Degre
        {
            get => _degre;
            set { if (_degre == value) return; _degre = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _pourTrancher = "";
        public string PourTrancher
        {
            get => _pourTrancher;
            set { if (_pourTrancher == value) return; _pourTrancher = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _objectif = "";
        public string Objectif
        {
            get => _objectif;
            set { if (_objectif == value) return; _objectif = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public static readonly string[] StatutsPossibles = { "à demander", "déjà en place", "à renouveler" };
        public System.Collections.Generic.IReadOnlyList<string> Statuts => StatutsPossibles;

        private string _statut = "";
        public string Statut
        {
            get => _statut;
            set { if (_statut == value) return; _statut = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        /// <summary>Vrai pour les dispositifs scolaires uniquement.</summary>
        public bool AfficherStatut { get; init; }

        /// <summary>Vrai pour les bilans : « ce que ça sert à trancher » ne concerne qu'eux.</summary>
        public bool AfficherPourTrancher { get; init; }

        /// <summary>Vrai pour les rééducations et les ressources de vie : ce que l'action vise.</summary>
        public bool AfficherObjectif { get; init; }

        /// <summary>
        /// Faux pour les ressources du quotidien : une habitude s'installe, elle ne se programme
        /// pas comme un rendez-vous, et la hiérarchiser comme un soin serait un contresens.
        /// </summary>
        public bool AfficherEcheanceEtDegre { get; init; } = true;

        public Action? Flush      { get; set; }
        public Action? RemoveSelf { get; set; }

        public ICommand RemoveCommand { get; }

        public PtActionVm()
        {
            RemoveCommand = new RelayCommand(_ => RemoveSelf?.Invoke());
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// L'indication d'une section de projet : ce qui décide si l'accompagnement a lieu, à quel
    /// degré et par qui. Son échelle de degrés est plus large que celle des actions — elle
    /// comprend « non indiqué à ce stade », qui n'a de sens que pour une section entière.
    /// </summary>
    public class PtIndicationVm : INotifyPropertyChanged
    {
        public static readonly string[] DegresPossibles =
            { "indispensable", "recommandé", "utile si possible", "à réévaluer plus tard", "non indiqué à ce stade" };

        public System.Collections.Generic.IReadOnlyList<string> Degres   => DegresPossibles;
        public System.Collections.Generic.IReadOnlyList<string> Porteurs => PtActionVm.PorteursPossibles;

        private string _degre = "";
        public string Degre
        {
            get => _degre;
            set
            {
                if (_degre == value) return;
                _degre = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(EstEcartee));
                Flush?.Invoke();
            }
        }

        /// <summary>
        /// Vrai quand la section n'est pas engagée maintenant — « non indiqué à ce stade » ou
        /// « à réévaluer plus tard ». Le motif et le critère de réévaluation disent alors tout ;
        /// aligner des objectifs sous une indication qu'on vient d'écarter serait se contredire
        /// dans la même page. C'est déjà la lecture que fait le rendu du dossier.
        /// </summary>
        public bool EstEcartee =>
            _degre.IndexOf("non indiqu", StringComparison.OrdinalIgnoreCase) >= 0 ||
            _degre.IndexOf("réévaluer",  StringComparison.OrdinalIgnoreCase) >= 0 ||
            _degre.IndexOf("reevaluer",  StringComparison.OrdinalIgnoreCase) >= 0;

        private string _porteur = "";
        public string Porteur
        {
            get => _porteur;
            set { if (_porteur == value) return; _porteur = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _motif = "";
        public string Motif
        {
            get => _motif;
            set { if (_motif == value) return; _motif = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _critereReevaluation = "";
        public string CritereReevaluation
        {
            get => _critereReevaluation;
            set { if (_critereReevaluation == value) return; _critereReevaluation = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public Action? Flush { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Section éditable d'un bloc PT (vm vivant, lié à PtFieldDef) ──────────
    public class PtFieldViewModel : INotifyPropertyChanged
    {
        public string      JsonPath { get; }
        public string      Title    { get; }
        public PtFieldKind Kind     { get; }

        public bool IsList       => Kind == PtFieldKind.List;
        public bool IsActions    => Kind == PtFieldKind.Actions;
        public bool IsText       => Kind == PtFieldKind.Text;
        public bool IsIndication => Kind == PtFieldKind.Indication;

        public PtIndicationVm Indication { get; } = new();

        /// <summary>
        /// Replié parce que l'indication de la section a été écartée. Rien n'est effacé : le
        /// contenu déjà saisi reste en mémoire et reparaît si le médecin change d'avis ou clique
        /// sur « afficher quand même ». Piloté par le bloc, jamais par le champ lui-même.
        /// </summary>
        private bool _masqueParIndication;
        public bool MasqueParIndication
        {
            get => _masqueParIndication;
            set { if (_masqueParIndication == value) return; _masqueParIndication = value; OnPropertyChanged(); }
        }

        /// <summary>Valeurs d'une action fraîchement ajoutée — cf. PtFieldDef.</summary>
        public string PorteurParDefaut  { get; init; } = "";
        public string EcheanceParDefaut { get; init; } = "";
        public string DegreParDefaut    { get; init; } = "";

        /// <summary>Les bilans affichent « pour trancher », pas les autres actions.</summary>
        public bool ActionsAvecPourTrancher { get; init; }

        /// <summary>Rééducations et ressources de vie affichent « objectif visé ».</summary>
        public bool ActionsAvecObjectif { get; init; }

        /// <summary>Faux pour les ressources du quotidien : ni échéance ni degré.</summary>
        public bool ActionsAvecEcheanceEtDegre { get; init; } = true;

        /// <summary>Dispositifs scolaires : statut à demander / déjà en place / à renouveler.</summary>
        public bool ActionsAvecStatut { get; init; }

        public ObservableCollection<PtActionVm> Actions { get; } = new();
        public ICommand AddActionCommand { get; }

        private string _content = "";
        public string Content
        {
            get => _content;
            set { if (_content == value) return; _content = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public ObservableCollection<EditableString> Items { get; } = new();

        private bool _isReformulePanelVisible;
        public bool IsReformulePanelVisible
        {
            get => _isReformulePanelVisible;
            set { if (_isReformulePanelVisible == value) return; _isReformulePanelVisible = value; OnPropertyChanged(); }
        }

        private string _userInstruction = "";
        public string UserInstruction
        {
            get => _userInstruction;
            set { if (_userInstruction == value) return; _userInstruction = value ?? ""; OnPropertyChanged(); }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating == value) return;
                _isGenerating = value;
                OnPropertyChanged();
                (RegenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public Action? Flush { get; set; }

        public ICommand ToggleReformulePanelCommand { get; }
        public ICommand CancelReformulePanelCommand { get; }
        public ICommand RegenerateCommand           { get; private set; }
        public ICommand AddItemCommand              { get; }
        public ICommand RemoveItemCommand           { get; }

        public PtFieldViewModel(string jsonPath, string title, PtFieldKind kind)
        {
            JsonPath = jsonPath;
            Title    = title;
            Kind     = kind;

            Indication.Flush = () => Flush?.Invoke();

            AddActionCommand = new RelayCommand(_ =>
            {
                var a = NouvelleAction();
                a.Quoi     = "";
                a.Porteur  = PorteurParDefaut;
                a.Echeance = EcheanceParDefaut;
                a.Degre    = DegreParDefaut;
                Actions.Add(a);
                Flush?.Invoke();
            });

            ToggleReformulePanelCommand = new RelayCommand(_ => IsReformulePanelVisible = !IsReformulePanelVisible);
            CancelReformulePanelCommand = new RelayCommand(_ => { IsReformulePanelVisible = false; UserInstruction = ""; });
            RegenerateCommand = new RelayCommand(_ => { }, _ => false);

            AddItemCommand = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                Items.Add(item);
                Flush?.Invoke();
            });

            RemoveItemCommand = new RelayCommand(param =>
            {
                if (param is EditableString es) { Items.Remove(es); Flush?.Invoke(); }
            });
        }

        /// <summary>
        /// Crée une action déjà branchée sur ce champ — appelée aussi bien par le bouton
        /// « + Ajouter » que par la relecture du JSON, pour que les deux chemins produisent
        /// exactement le même objet.
        /// </summary>
        public PtActionVm NouvelleAction()
        {
            var a = new PtActionVm
            {
                AfficherPourTrancher    = ActionsAvecPourTrancher,
                AfficherObjectif        = ActionsAvecObjectif,
                AfficherEcheanceEtDegre = ActionsAvecEcheanceEtDegre,
                AfficherStatut          = ActionsAvecStatut,
            };
            a.Flush      = () => Flush?.Invoke();
            a.RemoveSelf = () => { Actions.Remove(a); Flush?.Invoke(); };
            return a;
        }

        public void InitRegenerateCommand(Func<PtFieldViewModel, Task> reformulateFieldAsync)
        {
            RegenerateCommand = new RelayCommand(
                async _ => await reformulateFieldAsync(this),
                _ => !IsGenerating);
            OnPropertyChanged(nameof(RegenerateCommand));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    // ── Diagnostics retenus (synthese_diag_s2) ───────────────────────────────

    public class DiagRetenuVm : INotifyPropertyChanged
    {
        private string _label = "";
        public string Label
        {
            get => _label;
            set { if (_label == value) return; _label = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _certitude = "Modérée";
        public string Certitude
        {
            get => _certitude;
            set { if (_certitude == value) return; _certitude = value ?? "Modérée"; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public static readonly string[] CertitudeOptions = { "Hypothèse", "Modérée", "Élevée", "Très élevée" };

        public ObservableCollection<EditableString> Elements { get; } = new();

        public Action? Flush      { get; set; }
        public Action? RemoveSelf { get; set; }

        public ICommand AddElementCommand    { get; }
        public ICommand RemoveElementCommand { get; }
        public ICommand RemoveSelfCommand    { get; }

        public DiagRetenuVm()
        {
            AddElementCommand = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                Elements.Add(item);
                Flush?.Invoke();
            });

            RemoveElementCommand = new RelayCommand(param =>
            {
                if (param is EditableString es) { Elements.Remove(es); Flush?.Invoke(); }
            });

            RemoveSelfCommand = new RelayCommand(_ => RemoveSelf?.Invoke());
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Diagnostics écartés (synthese_diag_s3) ───────────────────────────────

    public class DiagEcarteVm : INotifyPropertyChanged
    {
        private string _label = "";
        public string Label
        {
            get => _label;
            set { if (_label == value) return; _label = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _conclusion = "";
        public string Conclusion
        {
            get => _conclusion;
            set { if (_conclusion == value) return; _conclusion = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public ObservableCollection<EditableString> Arguments { get; } = new();

        public Action? Flush      { get; set; }
        public Action? RemoveSelf { get; set; }

        public ICommand AddArgumentCommand    { get; }
        public ICommand RemoveArgumentCommand { get; }
        public ICommand RemoveSelfCommand     { get; }

        public DiagEcarteVm()
        {
            AddArgumentCommand = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                Arguments.Add(item);
                Flush?.Invoke();
            });

            RemoveArgumentCommand = new RelayCommand(param =>
            {
                if (param is EditableString es) { Arguments.Remove(es); Flush?.Invoke(); }
            });

            RemoveSelfCommand = new RelayCommand(_ => RemoveSelf?.Invoke());
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Intégration cartographies (synthese_diag_s4) ─────────────────────────

    public class S4IntegrationVm : INotifyPropertyChanged
    {
        public ObservableCollection<EditableString> Forces      { get; } = new();
        public ObservableCollection<EditableString> Fragilites  { get; } = new();
        public ObservableCollection<EditableString> Protecteurs { get; } = new();
        public ObservableCollection<EditableString> Aggravants  { get; } = new();

        public Action? Flush { get; set; }

        public ICommand AddForceCommand        { get; }
        public ICommand RemoveForceCommand     { get; }
        public ICommand AddFragiliteCommand    { get; }
        public ICommand RemoveFragiliteCommand { get; }
        public ICommand AddProtecteurCommand   { get; }
        public ICommand RemoveProtecteurCommand { get; }
        public ICommand AddAggravantCommand    { get; }
        public ICommand RemoveAggravantCommand { get; }

        public S4IntegrationVm()
        {
            (AddForceCommand,      RemoveForceCommand)      = MakeListCommands(Forces);
            (AddFragiliteCommand,  RemoveFragiliteCommand)  = MakeListCommands(Fragilites);
            (AddProtecteurCommand, RemoveProtecteurCommand) = MakeListCommands(Protecteurs);
            (AddAggravantCommand,  RemoveAggravantCommand)  = MakeListCommands(Aggravants);
        }

        private (ICommand add, ICommand remove) MakeListCommands(ObservableCollection<EditableString> list)
        {
            var add = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                list.Add(item);
                Flush?.Invoke();
            });
            var remove = new RelayCommand(param =>
            {
                if (param is EditableString es) { list.Remove(es); Flush?.Invoke(); }
            });
            return (add, remove);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

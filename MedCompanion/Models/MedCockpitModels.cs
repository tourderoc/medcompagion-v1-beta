using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MedCompanion.Models
{
    /// <summary>
    /// États d'écoute et d'activité du compagnon Med.
    /// Garantit la règle absolue de priorité : pendant la dictée de consultation (Whisper),
    /// l'écoute d'ambiance de Med est immédiatement mise en pause.
    /// </summary>
    public enum MedEcouteEtat
    {
        /// <summary>Med en veille / silencieux</summary>
        Veille,

        /// <summary>Med écoute discrètement les échanges de la consultation (écoute d'ambiance passive)</summary>
        EcouteAmbiance,

        /// <summary>Dictée active par le praticien : Whisper a 100% de la priorité, Med est coupé</summary>
        DicteeEnCours,

        /// <summary>Med réfléchit ou prépare une intervention / suggestion</summary>
        Reflexion
    }

    /// <summary>
    /// Rôles incarnés par le compagnon Med
    /// </summary>
    public enum MedRole
    {
        /// <summary>💼 Secrétaire : accueil, présence patient, préparation du dossier, vérification RDV</summary>
        Secretaire,

        /// <summary>🩺 Interne : synthèse du dossier, rappels cliniques, alertes, points à explorer</summary>
        Interne,

        /// <summary>👨‍⚕️ Confrère : aide diagnostique, avis thérapeutique, posologies, prise de recul</summary>
        Confrere,

        /// <summary>✨ Med Général : assistant système / copilote</summary>
        Med,

        /// <summary>👤 Praticien : questions ou réponses du médecin</summary>
        Praticien
    }

    /// <summary>
    /// Action rapide cliquable proposée par Med (boutons Oui/Non, Présent/Absent, Voir note, etc.)
    /// </summary>
    public class MedCockpitAction : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _id = Guid.NewGuid().ToString("N");
        public string Id
        {
            get => _id;
            set { _id = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Id))); }
        }

        private string _label = "";
        public string Label
        {
            get => _label;
            set { _label = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
        }

        private string _actionKey = "";
        public string ActionKey
        {
            get => _actionKey;
            set { _actionKey = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionKey))); }
        }

        private object? _payload;
        public object? Payload
        {
            get => _payload;
            set { _payload = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Payload))); }
        }

        private string _styleVariant = "Primary";
        /// <summary>
        /// Variante visuelle : "Success" (vert), "Danger" (rouge), "Primary" (bleu), "Warning" (orange), "Secondary" (gris)
        /// </summary>
        public string StyleVariant
        {
            get => _styleVariant;
            set { _styleVariant = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StyleVariant))); }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
        }
    }

    /// <summary>
    /// Message dans le fil de discussion interactif de la zone centrale Med
    /// </summary>
    public class MedCockpitMessage : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private MedRole _role = MedRole.Med;
        public MedRole Role
        {
            get => _role;
            set
            {
                _role = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoleDisplayName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoleIcon)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoleBadgeColor)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoleBackground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFromUser)));
            }
        }

        public string RoleDisplayName => Role switch
        {
            MedRole.Secretaire => "Secrétaire",
            MedRole.Interne => "Interne",
            MedRole.Confrere => "Confrère",
            MedRole.Praticien => "Vous",
            _ => "Med"
        };

        public string RoleIcon => Role switch
        {
            MedRole.Secretaire => "💼",
            MedRole.Interne => "🩺",
            MedRole.Confrere => "👨‍⚕️",
            MedRole.Praticien => "👤",
            _ => "✨"
        };

        public string RoleBadgeColor => Role switch
        {
            MedRole.Secretaire => "#0D47A1",
            MedRole.Interne => "#6A1B9A",
            MedRole.Confrere => "#00695C",
            MedRole.Praticien => "#37474F",
            _ => "#1565C0"
        };

        public string RoleBackground => Role switch
        {
            MedRole.Secretaire => "#F4F8FD",
            MedRole.Interne => "#FAF5FC",
            MedRole.Confrere => "#F2FAF7",
            MedRole.Praticien => "#F8F9FA",
            _ => "#F5F7FB"
        };

        private string _content = "";
        public string Content
        {
            get => _content;
            set { _content = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content))); }
        }

        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string TimestampDisplay => Timestamp.ToString("HH:mm");

        public bool IsFromUser => Role == MedRole.Praticien;

        public ObservableCollection<MedCockpitAction> Actions { get; set; } = new();

        public bool HasActions => Actions != null && Actions.Count > 0;

        public void NotifyActionsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActions)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Actions)));
        }
    }
}

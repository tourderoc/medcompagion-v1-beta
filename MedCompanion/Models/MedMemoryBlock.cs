using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Bloc mémoire de Med - visible, contrôlable, modifiable par l'utilisateur
    /// Stocke des informations persistantes entre les sessions
    /// </summary>
    public class MedMemoryBlock : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Identifiant unique du bloc (ex: "work_frame", "personality", "notes")
        /// </summary>
        public string BlockId { get; set; } = string.Empty;

        /// <summary>
        /// Titre affiché dans l'UI
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Emoji/icône du bloc
        /// </summary>
        public string Icon { get; set; } = string.Empty;

        /// <summary>
        /// Description courte du bloc (sous-titre)
        /// </summary>
        public string Description { get; set; } = string.Empty;

        private string _content = string.Empty;
        /// <summary>
        /// Contenu textuel du bloc (éditable par l'utilisateur)
        /// </summary>
        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasContent));
                    OnPropertyChanged(nameof(Preview));
                }
            }
        }

        /// <summary>
        /// Indique si le bloc a du contenu
        /// </summary>
        [JsonIgnore]
        public bool HasContent => !string.IsNullOrWhiteSpace(Content);

        /// <summary>
        /// Aperçu du contenu (limité à 100 caractères)
        /// </summary>
        [JsonIgnore]
        public string Preview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Content)) return "(vide)";
                var clean = Content.Replace("\r\n", " ").Replace("\n", " ").Trim();
                return clean.Length > 100 ? clean.Substring(0, 100) + "..." : clean;
            }
        }

        private bool _isExpanded = false;
        /// <summary>
        /// État d'expansion dans l'UI
        /// </summary>
        [JsonIgnore]
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isEditing = false;
        /// <summary>
        /// Indique si le bloc est en mode édition
        /// </summary>
        [JsonIgnore]
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Date de dernière modification
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Nécessite un LLM local (pour le bloc "Vie privée")
        /// </summary>
        public bool RequiresLocalLLM { get; set; } = false;

        /// <summary>
        /// Ordre d'affichage
        /// </summary>
        public int DisplayOrder { get; set; } = 0;
    }

    /// <summary>
    /// Collection des blocs mémoire de Med
    /// </summary>
    public class MedMemory
    {
        /// <summary>
        /// Version du format
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Liste des blocs mémoire
        /// </summary>
        public List<MedMemoryBlock> Blocks { get; set; } = new();

        /// <summary>
        /// Date de dernière modification globale
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Récupère un bloc par son ID
        /// </summary>
        public MedMemoryBlock? GetBlock(string blockId)
        {
            return Blocks.FirstOrDefault(b => b.BlockId == blockId);
        }

        /// <summary>
        /// Crée la mémoire par défaut avec les 3 blocs V0
        /// </summary>
        public static MedMemory CreateDefault()
        {
            return new MedMemory
            {
                Blocks = new List<MedMemoryBlock>
                {
                    new MedMemoryBlock
                    {
                        BlockId = "work_frame",
                        Title = "Mon cadre de travail",
                        Icon = "📋",
                        Description = "Comment je travaille avec Med",
                        DisplayOrder = 1,
                        Content = ""
                    },
                    new MedMemoryBlock
                    {
                        BlockId = "personality",
                        Title = "Personnalité de Med",
                        Icon = "🎭",
                        Description = "Ton, style, préférences de communication",
                        DisplayOrder = 2,
                        Content = ""
                    },
                    new MedMemoryBlock
                    {
                        BlockId = "notes",
                        Title = "Mes notes",
                        Icon = "📝",
                        Description = "Notes libres, pensées, rappels",
                        DisplayOrder = 3,
                        Content = ""
                    }
                }
            };
        }

        /// <summary>
        /// Génère un contexte à injecter dans le prompt de Med
        /// </summary>
        public string ToContextString()
        {
            var parts = new List<string>();

            foreach (var block in Blocks.OrderBy(b => b.DisplayOrder))
            {
                if (!string.IsNullOrWhiteSpace(block.Content))
                {
                    parts.Add($"[{block.Title}]\n{block.Content}");
                }
            }

            if (parts.Count == 0) return string.Empty;

            return "--- Mémoire de Med ---\n" + string.Join("\n\n", parts) + "\n--- Fin mémoire ---";
        }
    }
}

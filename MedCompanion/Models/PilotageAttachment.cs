using System;
using System.ComponentModel;
using System.IO;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente un document à joindre à un email dans le mode Pilotage
    /// </summary>
    public class PilotageAttachment : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        /// <summary>
        /// Chemin complet du fichier
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Nom du fichier (sans chemin)
        /// </summary>
        public string FileName => Path.GetFileName(FilePath);

        /// <summary>
        /// Taille du fichier formatée
        /// </summary>
        public string FileSize
        {
            get
            {
                try
                {
                    if (!File.Exists(FilePath)) return "—";
                    var info = new FileInfo(FilePath);
                    var bytes = info.Length;
                    if (bytes < 1024) return $"{bytes} o";
                    if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} Ko";
                    return $"{bytes / (1024.0 * 1024.0):F1} Mo";
                }
                catch
                {
                    return "—";
                }
            }
        }

        /// <summary>
        /// ID du patient associé (format: NOM_Prenom)
        /// </summary>
        public string PatientId { get; set; } = string.Empty;

        /// <summary>
        /// Type de document (attestation, courrier, ordonnance, autre)
        /// </summary>
        public string DocumentType { get; set; } = "autre";

        /// <summary>
        /// Date d'ajout à la file d'attente
        /// </summary>
        public DateTime AddedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Indique si le document est sélectionné pour l'envoi
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

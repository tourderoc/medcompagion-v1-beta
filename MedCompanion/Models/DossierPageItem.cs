using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente une page (feuille) dans le dossier papier
    /// Chaque section contient plusieurs pages chronologiques
    /// </summary>
    public class DossierPageItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _title = "";
        /// <summary>
        /// Titre de la page (ex: "Note du 15/03/2025", "Synthèse annuelle 2025")
        /// </summary>
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private DateTime _date;
        /// <summary>
        /// Date du document pour le tri chronologique
        /// </summary>
        public DateTime Date
        {
            get => _date;
            set { _date = value; OnPropertyChanged(); OnPropertyChanged(nameof(DateFormatted)); }
        }

        /// <summary>
        /// Date formatée pour l'affichage (ex: "15 mars 2025")
        /// </summary>
        public string DateFormatted => Date.ToString("dd MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

        private string _filePath = "";
        /// <summary>
        /// Chemin complet vers le fichier source
        /// </summary>
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        private string _content = "";
        /// <summary>
        /// Contenu complet de la page (markdown ou texte)
        /// </summary>
        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(); }
        }

        private string _previewText = "";
        /// <summary>
        /// Aperçu du contenu (~150 premiers caractères)
        /// </summary>
        public string PreviewText
        {
            get => _previewText;
            set { _previewText = value; OnPropertyChanged(); }
        }

        private DossierTab _section;
        /// <summary>
        /// Section (intercalaire) à laquelle appartient cette page
        /// </summary>
        public DossierTab Section
        {
            get => _section;
            set { _section = value; OnPropertyChanged(); }
        }

        private string _documentType = "";
        /// <summary>
        /// Type de document (note, courrier, ordonnance, attestation, etc.)
        /// </summary>
        public string DocumentType
        {
            get => _documentType;
            set { _documentType = value; OnPropertyChanged(); }
        }

        private bool _isLoaded = false;
        /// <summary>
        /// Indique si le contenu complet a été chargé (lazy loading)
        /// </summary>
        public bool IsLoaded
        {
            get => _isLoaded;
            set { _isLoaded = value; OnPropertyChanged(); }
        }
    }
}

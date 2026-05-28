using System;
using System.ComponentModel;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente un document patient (bilan, courrier, attestation, etc.) affichable dans une carte
    /// du dossier bleu en mode Consultation. Encapsule le fichier original ET sa synthèse IA.
    /// </summary>
    public class PatientDocumentItem : INotifyPropertyChanged
    {
        public string FilePath          { get; set; } = "";       // chemin du document original (.pdf/.jpg...)
        public string FileName          { get; set; } = "";       // nom du fichier (affiché sur la carte)
        public string Category          { get; set; } = "";       // bilans / courriers / attestations / ...
        public DateTime DateAdded       { get; set; }              // date de création du fichier

        // Synthèse IA générée pour ce document
        public string SynthesisFilePath { get; set; } = "";        // chemin du _synthese_*.md
        private string _synthesisContent = "";
        public string SynthesisContent
        {
            get => _synthesisContent;
            set
            {
                if (_synthesisContent != value)
                {
                    _synthesisContent = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SynthesisContent)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewText)));
                }
            }
        }

        /// <summary>
        /// Aperçu (premiers ~120 caractères de la synthèse, sans markdown).
        /// </summary>
        public string PreviewText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_synthesisContent)) return "(Pas de synthèse générée)";
                // Retire les marqueurs markdown les plus visibles pour l'aperçu
                var clean = System.Text.RegularExpressions.Regex.Replace(_synthesisContent, @"[#*_`>\-]+", "").Trim();
                return clean.Length > 120 ? clean.Substring(0, 120) + "…" : clean;
            }
        }

        public string DateFormatted => DateAdded.ToString("dd/MM/yyyy");

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

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

        /// <summary>
        /// Ce document est un formulaire à saisir champ par champ — seul cas où le crayon de saisie
        /// a un sens. Sur un bilan ou une restitution, il ouvrait une fenêtre de saisie de
        /// formulaire parents qui n'avait rien à lire.
        ///
        /// Déduit du dossier et du nom plutôt que de l'index : cette liste est construite en
        /// balayant le disque, sans passer par documents-index.json. Le test sur le nom couvre au
        /// passage les formulaires importés AVANT la catégorie dédiée, qui dorment dans « autres ».
        /// </summary>
        public bool IsFormulaire =>
            IsQuestionnaireCartographie ||
            Category.Equals("Formulaires", StringComparison.OrdinalIgnoreCase) ||
            FileName.Contains("formulaire", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Feuille du questionnaire parent de la Cartographie de l'enfant, rendue remplie puis
        /// scannée. Le crayon lui ouvre le DÉPOUILLEMENT — trente cases à cocher — et non la
        /// saisie champ par champ du formulaire de complétion, qui n'aurait rien à y lire.
        /// </summary>
        public bool IsQuestionnaireCartographie =>
            FileName.Contains("formulaire_carto", StringComparison.OrdinalIgnoreCase) ||
            FileName.Contains("cartographie", StringComparison.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

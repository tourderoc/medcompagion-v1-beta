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
            IsQuestionnaireEnvironnement ||
            Category.Equals("Formulaires", StringComparison.OrdinalIgnoreCase) ||
            FileName.Contains("formulaire", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Identifiant du formulaire, lu dans le nom du fichier importé —
        /// <c>aaaa-mm-jj_formulaire_&lt;id&gt;_rempli.ext</c>.
        ///
        /// Il est lu ENTRE SES SÉPARATEURS, et c'est tout l'intérêt : un simple
        /// <c>Contains("formulaire_carto")</c> reconnaissait aussi <c>formulaire_cartoenv</c>, si
        /// bien que la feuille de l'environnement ouvrait le dépouillement de la feuille de
        /// l'enfant — trente cases d'axes qui n'existent pas sur la page affichée à côté.
        /// </summary>
        private string? FormulaireIdDeduit
        {
            get
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    FileName, @"_formulaire_([a-z0-9]+)_rempli",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
            }
        }

        /// <summary>
        /// Feuille du questionnaire parent de la Cartographie de l'ENFANT, remplie puis scannée.
        /// Le crayon lui ouvre le dépouillement des trente cases par axe.
        ///
        /// Le repli sur le nom couvre les feuilles importées avant que l'identifiant soit posé ;
        /// il exclut explicitement l'environnement, dont le libellé contient lui aussi
        /// « cartographie ».
        /// </summary>
        public bool IsQuestionnaireCartographie =>
            FormulaireIdDeduit == "carto"
            || (FormulaireIdDeduit == null
                && FileName.Contains("cartographie", StringComparison.OrdinalIgnoreCase)
                && !FileName.Contains("environnement", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Feuille du questionnaire parent de la Cartographie de l'ENVIRONNEMENT. Le crayon lui
        /// ouvre son propre dépouillement — vingt-deux cases, réparties en blocs de tailles
        /// inégales.
        /// </summary>
        public bool IsQuestionnaireEnvironnement =>
            FormulaireIdDeduit == "cartoenv"
            || (FormulaireIdDeduit == null
                && FileName.Contains("environnement", StringComparison.OrdinalIgnoreCase));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

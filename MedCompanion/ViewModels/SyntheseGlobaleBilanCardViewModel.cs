using System;
using System.Collections.Generic;
using MedCompanion.Models.Synthesis;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Bloc d'affichage d'une Synthèse Globale VALIDÉE, destiné à l'onglet SYNTHESE du
    /// dossier bleu. Présenté EN PREMIER (avant les blocs de Bilan Final des évaluations),
    /// car c'est le document de référence du dossier.
    ///
    /// Lecture seule : pour modifier, créer une nouvelle version via le bouton +.
    /// </summary>
    public class SyntheseGlobaleBilanCardViewModel
    {
        public string   FilePath       { get; }
        public int      Version        { get; }
        public DateTime DateValidation { get; }
        public string   DateText       => DateValidation.ToString("dd/MM/yyyy");
        public string   TitreCard      => $"🧭 Synthèse Globale v{Version} — validée {DateText}";

        public List<SyntheseSectionDisplay> Sections { get; }

        public SyntheseGlobaleBilanCardViewModel(SyntheseGlobale s)
        {
            FilePath       = s.FilePath;
            Version        = s.Version;
            DateValidation = s.DateValidation ?? s.DateRedaction;

            Sections = new List<SyntheseSectionDisplay>();
            foreach (var section in s.Sections)
            {
                if (string.IsNullOrWhiteSpace(section.Contenu)) continue;
                Sections.Add(new SyntheseSectionDisplay
                {
                    Titre   = section.Titre,
                    Contenu = section.Contenu.Trim()
                });
            }
        }
    }

    public class SyntheseSectionDisplay
    {
        public string Titre   { get; set; } = "";
        public string Contenu { get; set; } = "";
    }
}

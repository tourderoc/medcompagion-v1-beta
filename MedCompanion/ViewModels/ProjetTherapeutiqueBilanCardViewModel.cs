using System;
using System.Collections.Generic;
using System.Linq;
using MedCompanion.Models.Therapeutique;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Bloc d'affichage d'un Projet Thérapeutique VALIDÉ pour l'onglet PROJET du
    /// dossier bleu. Lecture seule.
    /// </summary>
    public class ProjetTherapeutiqueBilanCardViewModel
    {
        public string   FilePath       { get; }
        public int      Version        { get; }
        public DateTime DateValidation { get; }
        public string   DateText       => DateValidation.ToString("dd/MM/yyyy");
        public string   TitreCard      => $"🎯 Projet Thérapeutique v{Version} — validé {DateText}";

        public string ObjectifsPrioritaires { get; }
        public string RessourcesASoutenir   { get; }
        public string ReevaluationChecklist { get; }
        public string CoConstructionFamille { get; }

        public DateTime? DateReevaluationPrevue { get; }
        public string?   DateReevaluationText
            => DateReevaluationPrevue?.ToString("dd/MM/yyyy");
        public bool IsReevaluationPassee
            => DateReevaluationPrevue.HasValue && DateReevaluationPrevue.Value.Date < DateTime.Now.Date;

        public List<ProjetSectionActionsDisplay> Sections { get; }

        public int ProgressionPct { get; }
        public int NbActionsAVenir  { get; }
        public int NbActionsEnCours { get; }
        public int NbActionsFaites  { get; }
        public int NbActionsAbandon { get; }

        public bool HasObjectifs            => !string.IsNullOrWhiteSpace(ObjectifsPrioritaires);
        public bool HasRessources           => !string.IsNullOrWhiteSpace(RessourcesASoutenir);
        public bool HasReevaluation         => !string.IsNullOrWhiteSpace(ReevaluationChecklist) || DateReevaluationPrevue.HasValue;
        public bool HasCoConstruction       => !string.IsNullOrWhiteSpace(CoConstructionFamille);

        public ProjetTherapeutiqueBilanCardViewModel(ProjetTherapeutique p)
        {
            FilePath       = p.FilePath;
            Version        = p.Version;
            DateValidation = p.DateValidation ?? p.DateRedaction;

            ObjectifsPrioritaires = (p.ObjectifsPrioritaires ?? "").Trim();
            RessourcesASoutenir   = (p.RessourcesASoutenir   ?? "").Trim();
            ReevaluationChecklist = (p.ReevaluationChecklist ?? "").Trim();
            CoConstructionFamille = (p.CoConstructionFamille ?? "").Trim();
            DateReevaluationPrevue = p.DateReevaluationPrevue;

            Sections = new List<ProjetSectionActionsDisplay>
            {
                BuildSection("💊 Prise en charge médicale",          p.ActionsMedicales),
                BuildSection("🧠 Prise en charge psychologique",     p.ActionsPsychologiques),
                BuildSection("🐛 Accompagnement développemental",    p.ActionsDeveloppementales),
                BuildSection("🌿 Actions sur l'environnement",       p.ActionsEnvironnementales),
            }.Where(s => s.Actions.Count > 0).ToList();

            ProgressionPct   = p.ProgressionPct;
            NbActionsAVenir  = p.NbActionsAVenir;
            NbActionsEnCours = p.NbActionsEnCours;
            NbActionsFaites  = p.NbActionsFaites;
            NbActionsAbandon = p.NbActionsAbandon;
        }

        private static ProjetSectionActionsDisplay BuildSection(string titre, IEnumerable<ProjetAction> actions)
            => new ProjetSectionActionsDisplay
            {
                Titre   = titre,
                Actions = actions
                    .Where(a => !string.IsNullOrWhiteSpace(a.Libelle))
                    .Select(a => new ProjetActionDisplay
                    {
                        Libelle             = a.Libelle,
                        Description         = a.Description,
                        IndicateurReussite  = a.IndicateurReussite,
                        LienSyntheseSection = a.LienSyntheseSection,
                        StatutIcon          = a.StatutIcon,
                        StatutLabel         = a.StatutLabel,
                        StatutColor         = a.StatutColor,
                        HasDescription      = !string.IsNullOrWhiteSpace(a.Description),
                        HasIndicateur       = !string.IsNullOrWhiteSpace(a.IndicateurReussite),
                    }).ToList()
            };
    }

    public class ProjetSectionActionsDisplay
    {
        public string Titre { get; set; } = "";
        public List<ProjetActionDisplay> Actions { get; set; } = new();
        public bool HasActions => Actions.Count > 0;
    }

    public class ProjetActionDisplay
    {
        public string Libelle             { get; set; } = "";
        public string Description         { get; set; } = "";
        public string IndicateurReussite  { get; set; } = "";
        public string LienSyntheseSection { get; set; } = "";
        public string StatutIcon          { get; set; } = "";
        public string StatutLabel         { get; set; } = "";
        public string StatutColor         { get; set; } = "";
        public bool   HasDescription      { get; set; }
        public bool   HasIndicateur       { get; set; }
    }
}

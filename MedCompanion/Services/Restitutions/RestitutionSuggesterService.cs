using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services.Synthesis;
using MedCompanion.Services.Therapeutique;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Restitutions
{
    public class RestitutionSuggesterService
    {
        private readonly ILLMService _llmService;
        private readonly SyntheseGlobaleService _syntheseService;
        private readonly ProjetTherapeutiqueService _projetService;
        private readonly PatientContextService _contextService;

        public RestitutionSuggesterService(ILLMService llmService, SyntheseGlobaleService syntheseService, ProjetTherapeutiqueService projetService, PatientContextService contextService)
        {
            _llmService = llmService;
            _syntheseService = syntheseService;
            _projetService = projetService;
            _contextService = contextService;
        }

        public async Task<(string Suggestion, string SourceContext)> PrefillBlocAsync(string patientNomComplet, RestitutionBloc bloc)
        {
            // 1. Récupérer le contexte
            string context = GetContextForBloc(patientNomComplet, bloc.Key);
            if (string.IsNullOrWhiteSpace(context))
            {
                return ("(Aucun contenu source disponible pour générer cette section.)", "Aucune source trouvée.");
            }

            // 2. Préparer le prompt
            var systemPrompt = GetSystemPromptForBloc(bloc);
            var messages = new List<(string role, string content)>
            {
                ("user", $"Voici le contenu médical source du patient :\n\n{context}\n\nÀ partir de ces informations, rédige le contenu de la section '{bloc.Titre}'.")
            };

            // 3. Appel au LLM
            var result = await _llmService.ChatAsync(systemPrompt, messages, 1500);

            if (result.success)
            {
                return (result.result, context); // on retourne la suggestion + la source pour l'UI
            }
            else
            {
                return ($"(Erreur lors de la génération : {result.error})", context);
            }
        }

        private string GetContextForBloc(string patientNomComplet, string key)
        {
            var sb = new StringBuilder();
            
            // On essaie de récupérer les sources principales
            var syntheseMetadata = _syntheseService.GetDerniereValidee(patientNomComplet);
            var synthese = syntheseMetadata != null 
                ? _syntheseService.Load(syntheseMetadata.FilePath) 
                : null;
                
            var projetMetadata = _projetService.GetDerniereValidee(patientNomComplet);
            var projet = projetMetadata != null
                ? _projetService.Load(projetMetadata.FilePath)
                : null;

            switch (key)
            {
                case "couverture":
                case "patient_contexte":
                    if (synthese != null)
                    {
                        sb.AppendLine(synthese.Sections.FirstOrDefault(s => s.Titre == "I. Profil Patient & Antécédents")?.Contenu);
                    }
                    else
                    {
                        var bundle = _contextService?.GetCompleteContext(patientNomComplet);
                        if (!string.IsNullOrEmpty(bundle?.ClinicalContext)) sb.AppendLine(bundle.ClinicalContext);
                    }
                    break;
                case "synthese_diag":
                case "bilan_final":
                    if (synthese != null)
                    {
                        sb.AppendLine(synthese.Sections.FirstOrDefault(s => s.Titre == "II. Synthèse Évolutive")?.Contenu);
                        sb.AppendLine(synthese.Sections.FirstOrDefault(s => s.Titre == "III. Bilan Actuel (Domaines)")?.Contenu);
                    }
                    break;
                case "synthese_globale":
                    if (synthese != null)
                    {
                        sb.AppendLine(synthese.Sections.FirstOrDefault(s => s.Titre == "IV. Synthèse Globale & Modèle de Compréhension")?.Contenu);
                    }
                    break;
                case "projet_therapeutique":
                    if (projet != null)
                    {
                        sb.AppendLine($"Objectifs prioritaires : {projet.ObjectifsPrioritaires}");
                        sb.AppendLine("Actions Médicales :");
                        foreach (var a in projet.ActionsMedicales) sb.AppendLine($"- {a.Libelle}: {a.Description}");
                        sb.AppendLine("Actions Psychologiques :");
                        foreach (var a in projet.ActionsPsychologiques) sb.AppendLine($"- {a.Libelle}: {a.Description}");
                        sb.AppendLine("Actions Développementales :");
                        foreach (var a in projet.ActionsDeveloppementales) sb.AppendLine($"- {a.Libelle}: {a.Description}");
                        sb.AppendLine("Actions Environnementales :");
                        foreach (var a in projet.ActionsEnvironnementales) sb.AppendLine($"- {a.Libelle}: {a.Description}");
                    }
                    break;
                case "restitution_1page":
                case "conclusion":
                    if (synthese != null)
                        sb.AppendLine(synthese.Sections.FirstOrDefault(s => s.Titre == "IV. Synthèse Globale & Modèle de Compréhension")?.Contenu);
                    if (projet != null)
                        sb.AppendLine($"Projet: {projet.ObjectifsPrioritaires}");
                    break;
                default:
                    if (synthese != null)
                    {
                        foreach(var s in synthese.Sections) sb.AppendLine(s.Contenu);
                    }
                    break;
            }

            return sb.ToString().Trim();
        }

        private string GetSystemPromptForBloc(RestitutionBloc bloc)
        {
            var basePrompt = @"Tu es un assistant médical spécialisé en pédopsychiatrie. Ton rôle est de rédiger un bloc spécifique du dossier de restitution du patient à partir des notes cliniques.
Consignes globales :
- Ne génère QUE le texte de la section demandée, sans salutations ni méta-commentaires.
- Rédige en utilisant un ton professionnel et adapté à la pédopsychiatrie.
- N'invente jamais d'informations médicales. Si une donnée manque, omet-la simplement.
- Utilise un formatage Markdown (listes, puces, gras) clair et structuré.";

            if (bloc.VoixCible == "clinique")
            {
                basePrompt += "\n- Ton cible : CLINIQUE (Jargon médical rigoureux, très synthétique, structuré). Destiné aux autres professionnels de santé.";
            }
            else if (bloc.VoixCible == "livre")
            {
                basePrompt += "\n- Ton cible : PARENTS (Empathique, vulgarisation médicale, accessible, déculpabilisant). Destiné à être lu et compris par les parents.";
            }
            else
            {
                basePrompt += "\n- Ton cible : MIXTE (Semi-vulgarisé, clair mais précis). Destiné à la fois aux professionnels de santé et aux parents.";
            }

            return basePrompt;
        }
    }
}

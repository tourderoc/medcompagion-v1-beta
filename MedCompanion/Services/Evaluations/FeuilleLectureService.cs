using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Étape 5 — Génère une lecture clinique courte (3-5 lignes) d'une feuille
    /// dimensionnelle. Destinataire : le pédopsy en consultation → ton clinique
    /// sobre, pas de métaphores poétiques (voix livre déférée Restitution Parents).
    /// </summary>
    public class FeuilleLectureService
    {
        private const int LlmTimeoutSeconds = 60;
        private const int MaxTokens         = 800;

        private readonly ILLMService _llm;
        public FeuilleLectureService(ILLMService llm) { _llm = llm; }

        public async Task<(bool ok, string? lecture, string? error)> ReadFeuilleAsync(
            FeuilleEnvironnement feuille,
            int? age,
            string? motif,
            CancellationToken ct = default)
        {
            if (feuille == null) return (false, null, "Feuille nulle.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(feuille, age, motif);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var lecture = Clean(raw);
                if (string.IsNullOrWhiteSpace(lecture))
                    return (false, null, "Lecture vide après nettoyage.");

                return (true, lecture, null);
            }
            catch (OperationCanceledException)
            {
                return (false, null, "Délai LLM dépassé.");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private static string BuildPrompt(FeuilleEnvironnement feuille, int? age, string? motif)
        {
            var ageInfo   = age.HasValue ? $"{age.Value} ans" : "âge non précisé";
            var motifInfo = string.IsNullOrWhiteSpace(motif) ? "(non précisé)" : motif.Trim();

            var couleurFeuille = EnvironnementScoringService.CalculerFeuille(feuille);
            var couleurCent    = EnvironnementScoringService.CalculerNervure(feuille.NervureCentrale);

            var sb = new StringBuilder();
            sb.AppendLine($"Tu es un assistant clinique destiné à un pédopsychiatre qui lit la cartographie environnementale d'un enfant qu'il suit.");
            sb.AppendLine();
            sb.AppendLine($"Feuille : {feuille.Label} — {feuille.SousTitre}");
            sb.AppendLine($"Patient : {ageInfo}, motif : {motifInfo}");
            sb.AppendLine();
            sb.AppendLine($"Nervure centrale ({feuille.NervureCentrale.Label}) : {feuille.NervureCentrale.Score}/{feuille.NervureCentrale.MaxScore} → {couleurCent}");
            foreach (var item in feuille.NervureCentrale.Items)
                sb.AppendLine($"  - [{(item.IsChecked ? "Oui" : "Non")}] {item.Affirmation}");
            sb.AppendLine();

            sb.AppendLine("Nervures secondaires :");
            foreach (var sec in feuille.NervuresSecondaires)
            {
                var couleurSec = EnvironnementScoringService.CalculerNervure(sec);
                sb.AppendLine($"- {sec.Label} : {sec.Score}/{sec.MaxScore} → {couleurSec}");
                foreach (var item in sec.Items)
                    sb.AppendLine($"    - [{(item.IsChecked ? "Oui" : "Non")}] {item.Affirmation}");
            }
            sb.AppendLine();
            sb.AppendLine($"Couleur globale de la feuille : {couleurFeuille}");
            sb.AppendLine();
            sb.AppendLine("CONSIGNE :");
            sb.AppendLine("- 3 à 5 lignes maximum, ton clinique direct (pas de métaphores poétiques).");
            sb.AppendLine("- Décris l'équilibre/déséquilibre entre nervure centrale et secondaires.");
            sb.AppendLine("- Pointe UN élément clinique à creuser en consultation.");
            sb.AppendLine("- INTERDIT : suggestions d'action, conseils aux parents, plan thérapeutique");
            sb.AppendLine("  (réservé Projet Thérapeutique).");
            sb.AppendLine("- Si la feuille est verte uniforme : nomme la solidité, n'invente pas un problème.");
            sb.AppendLine("- Réponds uniquement par la lecture, sans titre, sans préambule, sans signature.");

            return sb.ToString();
        }

        private static string Clean(string raw)
        {
            var t = raw.Trim();
            // Enlève d'éventuels balises markdown de bloc
            if (t.StartsWith("```")) { var i = t.IndexOf('\n'); if (i > 0) t = t.Substring(i + 1); }
            if (t.EndsWith("```"))   t = t.Substring(0, t.LastIndexOf("```", StringComparison.Ordinal)).TrimEnd();
            // Enlève un éventuel "Lecture :" / "Réponse :" en début
            foreach (var prefix in new[] { "Lecture :", "Lecture:", "Réponse :", "Réponse:" })
                if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    t = t.Substring(prefix.Length).TrimStart();
            return t.Trim();
        }
    }
}

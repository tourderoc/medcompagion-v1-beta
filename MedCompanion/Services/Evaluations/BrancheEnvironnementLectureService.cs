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
    /// Étape 5 — Génère une lecture clinique globale (8-12 lignes) de la branche
    /// environnement, en croisant les 5 feuilles. Destinataire : le pédopsy en
    /// consultation → ton clinique sobre, pas de métaphores poétiques (voix livre
    /// déférée Restitution Parents).
    /// </summary>
    public class BrancheEnvironnementLectureService
    {
        private const int LlmTimeoutSeconds = 90;
        private const int MaxTokens         = 1200;

        private readonly ILLMService _llm;
        public BrancheEnvironnementLectureService(ILLMService llm) { _llm = llm; }

        public async Task<(bool ok, string? lecture, string? error)> ReadBrancheAsync(
            CartographieEnvironnement carto,
            int? age,
            string? motif,
            CancellationToken ct = default)
        {
            if (carto == null) return (false, null, "Cartographie nulle.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(carto, age, motif);
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

        private static string BuildPrompt(CartographieEnvironnement carto, int? age, string? motif)
        {
            var ageInfo   = age.HasValue ? $"{age.Value} ans" : "âge non précisé";
            var motifInfo = string.IsNullOrWhiteSpace(motif) ? "(non précisé)" : motif.Trim();

            var sb = new StringBuilder();
            sb.AppendLine("Tu es un assistant clinique destiné à un pédopsychiatre qui synthétise la branche environnement complète d'un enfant qu'il suit.");
            sb.AppendLine();
            sb.AppendLine($"Patient : {ageInfo}, motif : {motifInfo}");
            sb.AppendLine();
            sb.AppendLine("Les 5 feuilles de la branche, dans l'ordre :");

            var feuilles = new[]
            {
                carto.Famille,
                carto.EcolePairs,
                carto.EcransMedias,
                carto.ValeursSocietales,
                carto.CadreEducatif
            };

            int i = 1;
            foreach (var f in feuilles)
            {
                var couleur = EnvironnementScoringService.CalculerFeuille(f);
                var label   = CartographieEnvironnementContent.NiveauLabel(couleur);
                sb.AppendLine();
                sb.AppendLine($"{i}. {f.Label} ({f.SousTitre}) — couleur : {couleur} ({label})");
                if (!string.IsNullOrWhiteSpace(f.LectureMed))
                    sb.AppendLine($"   Lecture feuille : {f.LectureMed.Replace("\r", "").Replace("\n", " ").Trim()}");
                else
                {
                    // Pas de lecture par feuille : on donne les scores bruts pour contexte
                    var cent = EnvironnementScoringService.CalculerNervure(f.NervureCentrale);
                    sb.AppendLine($"   (pas de lecture par feuille) — nervure centrale {f.NervureCentrale.Label} : {f.NervureCentrale.Score}/{f.NervureCentrale.MaxScore} → {cent}");
                    foreach (var sec in f.NervuresSecondaires)
                    {
                        var cs = EnvironnementScoringService.CalculerNervure(sec);
                        sb.AppendLine($"   - {sec.Label} : {sec.Score}/{sec.MaxScore} → {cs}");
                    }
                }
                i++;
            }

            sb.AppendLine();
            sb.AppendLine("CONSIGNE :");
            sb.AppendLine("- 8 à 12 lignes structurées en 3 paragraphes courts :");
            sb.AppendLine("  a) État global de la branche : feuilles solides vs feuilles fragiles.");
            sb.AppendLine("  b) Zone de convergence : où plusieurs feuilles fragiles se rejoignent (interaction clinique).");
            sb.AppendLine("  c) Point central à creuser en consultation suivante.");
            sb.AppendLine("- Ton clinique direct, pas de métaphores poétiques.");
            sb.AppendLine("- INTERDIT : suggestions d'action, conseils, plan, prescription.");
            sb.AppendLine("- Pas de redite littérale des 5 lectures — synthèse, pas concaténation.");
            sb.AppendLine("- Réponds uniquement par la lecture, sans titre, sans préambule, sans signature.");

            return sb.ToString();
        }

        private static string Clean(string raw)
        {
            var t = raw.Trim();
            if (t.StartsWith("```")) { var i = t.IndexOf('\n'); if (i > 0) t = t.Substring(i + 1); }
            if (t.EndsWith("```"))   t = t.Substring(0, t.LastIndexOf("```", StringComparison.Ordinal)).TrimEnd();
            foreach (var prefix in new[] { "Lecture :", "Lecture:", "Réponse :", "Réponse:", "Synthèse :", "Synthèse:" })
                if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    t = t.Substring(prefix.Length).TrimStart();
            return t.Trim();
        }
    }
}

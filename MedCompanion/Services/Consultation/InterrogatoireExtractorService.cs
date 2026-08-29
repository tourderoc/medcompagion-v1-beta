using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Consultation
{
    public class InterrogatoireExtractorService
    {
        private readonly string _promptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "prompt_system.txt");

        private string BuildPrompt(string transcription, string? manualNotes)
        {
            string template;
            try
            {
                template = File.Exists(_promptPath)
                    ? File.ReadAllText(_promptPath, Encoding.UTF8)
                    : GetFallbackPrompt();
            }
            catch
            {
                template = GetFallbackPrompt();
            }

            // Injecter les notes manuelles en tête de la zone transcription si présentes
            string content;
            if (!string.IsNullOrWhiteSpace(manualNotes))
            {
                content =
                    "=== NOTES CERTIFIÉES (saisies par le médecin — PRIORITÉ ABSOLUE) ===\n" +
                    "Prénoms, noms d'école, villes, fratrie : reprendre TELS QUELS.\n" +
                    "La transcription ci-dessous complète ces données mais ne les remplace pas.\n\n" +
                    manualNotes.Trim() +
                    "\n\n=== TRANSCRIPTION (contexte narratif) ===\n" +
                    transcription;
            }
            else
            {
                content = transcription;
            }

            return template.Replace("{TRANSCRIPTION}", content);
        }

        /// <summary>
        /// Budget de génération. Sous contrainte de schéma le modèle ne peut plus partir en roue
        /// libre, donc ce plafond n'est plus qu'un garde-fou de dernier recours.
        /// </summary>
        private const int MaxTokens = 3000;

        /// <summary>
        /// Plafond de citations par consultation. Au-delà, elles cessent d'être des repères et
        /// redeviennent du bruit : l'intérêt d'une phrase gardée tient à ce qu'elle soit rare.
        /// </summary>
        public const int MaxVerbatim = 5;

        /// <param name="allowedBlockKeys">
        /// Clés de blocs réellement présentes dans la consultation. Fournies, elles deviennent un
        /// <c>enum</c> dans le schéma : le modèle ne PEUT plus inventer de clé. Sans elles,
        /// <see cref="ApplyUpdates"/> se contentait d'ignorer silencieusement les clés inconnues —
        /// l'information extraite était alors perdue sans le moindre signal.
        /// </param>
        public async Task<(bool success, ExtractionResult? result, string? error)> ExtractAsync(
            ILLMService llmService,
            string transcription,
            string? manualNotes = null,
            IEnumerable<string>? allowedBlockKeys = null)
        {
            if (string.IsNullOrWhiteSpace(transcription) && string.IsNullOrWhiteSpace(manualNotes))
                return (false, null, "Transcription et notes vides.");

            var prompt = BuildPrompt(transcription, manualNotes);

            // Chemin contraint quand le moteur le permet (llama.cpp) : le JSON est valide par
            // construction. Sinon, ancien chemin texte + extraction d'accolades.
            if (llmService is IStructuredOutputService structured && structured.SupportsStructuredOutput)
            {
                var schema = BuildSchema(allowedBlockKeys);
                var (okJson, rawJson, errJson) = await structured.GenerateJsonAsync(
                    prompt, "extraction_interrogatoire", schema, MaxTokens);

                if (!okJson) return (false, null, errJson);
                return ParseJson(rawJson);
            }

            var (ok, raw, err) = await llmService.GenerateTextAsync(prompt, maxTokens: MaxTokens);
            if (!ok)
                return (false, null, err);

            return ParseJson(raw);
        }

        /// <summary>
        /// Schéma de <see cref="ExtractionResult"/>. Écrit à la main plutôt que dérivé du type par
        /// réflexion : on veut y exprimer des contraintes que C# ne porte pas (liste fermée des
        /// clés, champs obligatoires), et le schéma doit rester lisible à côté du prompt.
        /// </summary>
        private static string BuildSchema(IEnumerable<string>? allowedBlockKeys)
        {
            var keys = allowedBlockKeys?
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .ToArray() ?? Array.Empty<string>();

            // Pas de clés connues : on laisse blockKey libre plutôt que de fabriquer un enum vide,
            // qui rendrait toute réponse impossible à satisfaire.
            var blockKeyProperty = keys.Length > 0
                ? "{ \"type\": \"string\", \"enum\": [" +
                  string.Join(", ", keys.Select(k => JsonSerializer.Serialize(k))) + "] }"
                : "{ \"type\": \"string\" }";

            // maxItems borne le nombre de citations dans la GRAMMAIRE elle-même, pas seulement dans
            // la consigne : un 12B sur-collecte dès que le critère est qualitatif (« une phrase qui
            // porte un affect »), et une consigne de volume se respecte mal. Doublé d'un découpage
            // côté C# — la grammaire peut varier d'une version de llama.cpp à l'autre.
            // Borne AUSSI le tableau des blocs, et pas seulement les citations. Constaté en test :
            // sur une transcription qui colle mal au prompt, Gemma répète indéfiniment la même
            // entrée (« traitement pris tous les matins ») jusqu'à épuiser le budget — la grammaire
            // autorisait un tableau infini. Un update par bloc disponible suffit largement.
            var maxUpdates = keys.Length > 0 ? keys.Length : 20;

            return $$"""
            {
              "type": "object",
              "properties": {
                "updates": {
                  "type": "array",
                  "maxItems": {{maxUpdates}},
                  "items": {
                    "type": "object",
                    "properties": {
                      "blockKey": {{blockKeyProperty}},
                      "appendText": { "type": "string" },
                      "newThemes": { "type": "array", "items": { "type": "string" } }
                    },
                    "required": ["blockKey", "appendText", "newThemes"],
                    "additionalProperties": false
                  }
                },
                "verbatim": {
                  "type": "array",
                  "maxItems": {{MaxVerbatim}},
                  "items": {
                    "type": "object",
                    "properties": {
                      "locuteur": { "type": "string", "enum": ["enfant", "mère", "père", "autre"] },
                      "citation": { "type": "string" }
                    },
                    "required": ["locuteur", "citation"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["updates", "verbatim"],
              "additionalProperties": false
            }
            """;
        }

        private (bool, ExtractionResult?, string?) ParseJson(string raw)
        {
            // Extraire le JSON du texte (le LLM peut ajouter du texte autour)
            var json = ExtractJsonBlock(raw);
            if (string.IsNullOrWhiteSpace(json))
            {
                // Le début de la réponse dit tout de suite ce qu'a fait le modèle (souvent : il a
                // imité le format « [bloc] » des exemples au lieu de produire du JSON). Sans cet
                // extrait, le message n'orientait vers rien.
                var apercu = raw.Trim();
                if (apercu.Length > 160) apercu = apercu[..160] + "…";
                return (false, null,
                    "Le modèle n'a pas produit de JSON. Début de sa réponse : « " + apercu + " »");
            }

            try
            {
                var result = JsonSerializer.Deserialize<ExtractionResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result == null)
                    return (false, null, "Désérialisation JSON échouée.");

                Normalize(result);
                return (true, result, null);
            }
            catch (JsonException ex)
            {
                return (false, null, $"JSON invalide : {ex.Message}");
            }
        }

        /// <summary>
        /// Fusionne les entrées qui visent le même bloc et supprime les lignes répétées.
        ///
        /// Le modèle produit régulièrement deux objets pour un même <c>blockKey</c>, avec un contenu
        /// identique ou quasi (constaté : « peur pour la sixième » deux fois). La consigne de prompt
        /// « un seul objet par blockKey » a été essayée et ne tient pas sur un 12B — les appends se
        /// concaténaient donc en doublons visibles dans la note. Le faire ici est déterministe.
        ///
        /// Les citations subissent le même traitement : une phrase gardée deux fois perdrait sa
        /// valeur de repère.
        /// </summary>
        private static void Normalize(ExtractionResult result)
        {
            var fusionnes = new List<BlockUpdate>();

            foreach (var groupe in result.Updates
                         .Where(u => !string.IsNullOrWhiteSpace(u.BlockKey))
                         .GroupBy(u => u.BlockKey.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                // Lignes de tous les updates du bloc, dans l'ordre, sans répétition.
                var lignes = groupe
                    .SelectMany(u => (u.AppendText ?? "").Split('\n'))
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var themes = groupe
                    .SelectMany(u => u.NewThemes ?? new List<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (lignes.Count == 0) continue;

                fusionnes.Add(new BlockUpdate
                {
                    BlockKey   = groupe.First().BlockKey.Trim(),
                    AppendText = string.Join("\n", lignes),
                    NewThemes  = themes
                });
            }

            result.Updates = fusionnes;

            result.Verbatim = result.Verbatim
                .Where(q => !string.IsNullOrWhiteSpace(q.Citation))
                .GroupBy(q => q.Citation.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(MaxVerbatim)
                .ToList();
        }

        private static string ExtractJsonBlock(string text)
        {
            // Cherche le premier { et le dernier } pour extraire l'objet JSON
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end < start)
                return "";
            return text[start..(end + 1)];
        }

        public static void ApplyUpdates(List<ConsultationBlock> blocks, ExtractionResult result)
        {
            foreach (var update in result.Updates)
            {
                var block = blocks.Find(b => b.Key == update.BlockKey);
                if (block == null) continue;

                if (!string.IsNullOrWhiteSpace(update.AppendText))
                {
                    block.FreeText = string.IsNullOrWhiteSpace(block.FreeText)
                        ? update.AppendText.Trim()
                        : block.FreeText + "\n" + update.AppendText.Trim();
                }

                foreach (var theme in update.NewThemes)
                {
                    if (!block.CoveredThemes.Contains(theme))
                        block.CoveredThemes.Add(theme);
                }
            }
        }

        /// <param name="verbatim">
        /// Phrases gardées telles quelles, rendues en une SECTION UNIQUE en fin de note plutôt que
        /// sous chaque bloc : la note se relit avant la consultation suivante pour pouvoir reciter
        /// une formule à l'enfant, et un récapitulatif d'un coup d'œil sert cet usage mieux qu'un
        /// semis de citations qu'il faudrait aller chercher bloc par bloc.
        /// </param>
        public static string BuildFinalNote(
            List<ConsultationBlock> blocks,
            DateTime date,
            IEnumerable<VerbatimQuote>? verbatim = null)
        {
            var sb = new StringBuilder();

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.FreeText)) continue;
                sb.AppendLine($"## {block.Title}");
                // Supprimer les lignes "Date : ..." redondantes injectées par le LLM
                var lines = block.FreeText
                    .Split('\n')
                    .Where(l => !Regex.IsMatch(l.Trim(), @"^\*?\*?Date\s*:.*", RegexOptions.IgnoreCase));
                sb.AppendLine(string.Join('\n', lines).Trim());
                sb.AppendLine();
            }

            AppendVerbatimSection(sb, verbatim);

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Rend la section « Ses mots ». N'écrit rien quand il n'y a aucune citation : un titre
        /// suivi du vide donnerait à lire une absence là où il n'y a qu'une consultation sans phrase
        /// marquante, ce qui est un cas normal et non un manque.
        /// </summary>
        public static void AppendVerbatimSection(StringBuilder sb, IEnumerable<VerbatimQuote>? verbatim)
        {
            var quotes = verbatim?
                .Where(q => !string.IsNullOrWhiteSpace(q.Citation))
                .Take(MaxVerbatim)
                .ToList();

            if (quotes == null || quotes.Count == 0) return;

            sb.AppendLine("## Ses mots");
            sb.AppendLine();
            foreach (var q in quotes)
            {
                var citation = q.Citation.Trim().Trim('«', '»', '"').Trim();
                var locuteur = string.IsNullOrWhiteSpace(q.Locuteur) ? "indéterminé" : q.Locuteur.Trim();
                sb.AppendLine($"> « {citation} » — {locuteur}");
            }
            sb.AppendLine();
        }

        private static string GetFallbackPrompt() =>
            "Extrais les informations médicales de cette transcription et retourne un JSON avec la clé \"updates\". " +
            "Chaque update a : blockKey (parmi identite/motif/famille/fratrie/atcds/scolarite/activites/maison), appendText, newThemes.\n\n{TRANSCRIPTION}";
    }
}

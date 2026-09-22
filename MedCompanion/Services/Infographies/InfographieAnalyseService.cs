using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using MedCompanion.Models.Infographies;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Infographies
{
    /// <summary>
    /// Lit une infographie avec le modèle vision local (Gemma via llama.cpp) et propose
    /// titre, type, sujets et public. Plutôt qu'un OCR : les polices stylisées et le texte
    /// éparpillé entre les dessins donnent du charabia à Tesseract, et le modèle lit et classe
    /// en une seule étape. La proposition ne valide rien : la fiche reste « à relire ».
    /// </summary>
    public class InfographieAnalyseService
    {
        /// <summary>Largeur maximale envoyée au modèle : au-delà, l'encodeur vision réduit de toute façon.</summary>
        private const int LargeurMax = 2048;

        private readonly LlamaCppProvider _vision = new();

        public record Proposition(string Titre, string Type, List<string> Sujets, string Public, List<string> AVerifier);

        public async Task<(bool success, Proposition? proposition, string? error)> AnalyserAsync(
            Infographie fiche,
            IEnumerable<string> typesConnus,
            IEnumerable<string> sujetsConnus,
            IEnumerable<string> publicsConnus)
        {
            byte[] image;
            try { image = PreparerImage(fiche.ImagePath); }
            catch (Exception ex) { return (false, null, $"Image illisible : {ex.Message}"); }

            var prompt = new StringBuilder();
            prompt.AppendLine("Cette image est une infographie destinée aux familles d'un cabinet de pédopsychiatrie.");
            prompt.AppendLine("Lis-la et propose son classement dans la bibliothèque du médecin.");
            prompt.AppendLine();
            prompt.AppendLine("- titre : court (au plus 8 mots), en français correct, fidèle au sujet de l'infographie.");
            prompt.AppendLine($"- type : un seul, choisi de préférence parmi : {Liste(typesConnus)}.");
            // Mesuré le 22/09 : sans la seconde phrase, la fiche dépression est repartie avec
            // « TDAH », pris dans la liste des sujets connus, et « Traitement » y doublait le type.
            prompt.AppendLine("- sujets : 1 à 4 sujets précis (trouble, médicament, thème). " +
                              $"Réutilise l'orthographe exacte de ceux-ci quand ils conviennent : {Liste(sujetsConnus)}. " +
                              "N'en reprends aucun dont l'infographie ne parle pas réellement, et ne répète pas le type " +
                              "(« Traitement », « Trouble »…) comme sujet.");
            prompt.AppendLine($"- public : un seul, choisi de préférence parmi : {Liste(publicsConnus)}.");
            prompt.AppendLine("- a_verifier : recopie mot pour mot, sans les juger, les affirmations chiffrées ou " +
                              "réglementaires de l'infographie (durées, pourcentages, règles de prescription, fréquences " +
                              "de surveillance), au plus 6. Le médecin les vérifiera avant de remettre la fiche.");
            prompt.AppendLine();
            prompt.AppendLine("Réponds UNIQUEMENT avec cet objet JSON, sans commentaire ni texte autour :");
            prompt.AppendLine("""{ "titre": "", "type": "", "sujets": [], "public": "", "a_verifier": [] }""");

            var (ok, brut, erreur) = await _vision.AnalyzeImageAsync(prompt.ToString(), image, maxTokens: 800);
            if (!ok) return (false, null, erreur ?? "échec du modèle");

            var texte = brut ?? "";
            int debut = texte.IndexOf('{'), fin = texte.LastIndexOf('}');
            if (debut < 0 || fin <= debut) return (false, null, "le modèle n'a pas rendu de JSON");

            try
            {
                using var doc = JsonDocument.Parse(texte[debut..(fin + 1)]);
                var r = doc.RootElement;
                return (true, new Proposition(
                    Chaine(r, "titre"),
                    Chaine(r, "type"),
                    Tableau(r, "sujets"),
                    Chaine(r, "public"),
                    Tableau(r, "a_verifier")), null);
            }
            catch (Exception ex)
            {
                return (false, null, $"JSON illisible : {ex.Message}");
            }
        }

        /// <summary>PNG réduit à LargeurMax : la requête reste légère sans perdre ce que le modèle peut lire.</summary>
        private static byte[] PreparerImage(string chemin)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(chemin);
            bmp.EndInit();

            BitmapSource source = bmp;
            if (bmp.PixelWidth > LargeurMax)
            {
                double f = (double)LargeurMax / bmp.PixelWidth;
                source = new TransformedBitmap(bmp, new System.Windows.Media.ScaleTransform(f, f));
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private static string Liste(IEnumerable<string> valeurs)
        {
            var l = valeurs.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            return l.Count == 0 ? "(aucun pour l'instant)" : string.Join(", ", l);
        }

        private static string Chaine(JsonElement r, string nom)
            => r.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

        private static List<string> Tableau(JsonElement r, string nom)
        {
            if (!r.TryGetProperty(nom, out var v) || v.ValueKind != JsonValueKind.Array) return new List<string>();
            return v.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => (e.GetString() ?? "").Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}

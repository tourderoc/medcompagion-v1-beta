using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Vision
{
    /// <summary>
    /// Service de lecture d'images (OCR + manuscrit) basé sur <b>GLM-OCR</b> exécuté en local via
    /// Ollama. Remplace l'approche Tesseract pour tout nouveau développement.
    ///
    /// CONFIDENTIALITÉ : 100 % local. Le service <b>refuse de s'exécuter</b> si le modèle configuré
    /// est un modèle Ollama Cloud (suffixe -cloud) — voir <see cref="OllamaModelInfo.IsCloudModel"/>.
    /// C'est un refus, pas un avertissement : ce service lit des documents patients.
    ///
    /// Le modèle est épinglé sur <c>AppSettings.OcrModel</c> : l'OCR ne dépend jamais du modèle de
    /// conversation sélectionné dans l'UI (même motif que l'anonymisation).
    ///
    /// Le service appelle <c>/api/generate</c> directement plutôt que de passer par
    /// <c>OllamaLLMProvider</c> : l'OCR exige des réglages spécifiques (contexte réduit, séquences
    /// d'arrêt, découpe des répétitions) qui n'ont pas leur place dans l'interface LLM générique.
    /// </summary>
    public class GlmOcrService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _model;

        /// <summary>Taille max d'une image envoyée au modèle (côté le plus long, en pixels).</summary>
        private const int MaxDimension = 2000;

        /// <summary>
        /// Contexte volontairement réduit. Le défaut d'Ollama (32768 sur ce modèle) sature la VRAM
        /// d'une carte 6 Go en cache KV pour aucun bénéfice : une image OCR tient très largement
        /// dans 8192. Mesuré sur RTX 3050 : ~100 tokens/s au lieu d'un débit effondré.
        /// </summary>
        private const int NumCtx = 8192;

        public GlmOcrService(AppSettings settings)
            : this(settings.OllamaBaseUrl, settings.OcrModel) { }

        public GlmOcrService(string baseUrl, string model)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _model = model;
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public string ModelName => _model;

        // ──────────────────────────────────────────────
        //  DISPONIBILITÉ
        // ──────────────────────────────────────────────

        /// <summary>
        /// Vérifie qu'Ollama répond et que le modèle OCR est bien installé localement.
        /// </summary>
        public async Task<(bool ready, string? error)> IsReadyAsync()
        {
            var guard = CloudGuardError();
            if (guard != null) return (false, guard);

            try
            {
                var body = await _http.GetStringAsync($"{_baseUrl}/api/tags").ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("models", out var models))
                    return (false, "Réponse Ollama inattendue.");

                var names = models.EnumerateArray()
                    .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                    .Where(n => n.Length > 0)
                    .ToList();

                if (!names.Any())
                    return (false, "Ollama répond mais aucun modèle n'est installé.");

                // Tolère l'absence du tag ':latest' dans la configuration.
                bool present = names.Any(n =>
                    string.Equals(n, _model, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(StripTag(n), StripTag(_model), StringComparison.OrdinalIgnoreCase));

                if (!present)
                    return (false, $"Modèle OCR « {_model} » absent.\n\nInstallez-le avec :\n    ollama pull {_model}");

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Ollama injoignable sur {_baseUrl} : {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────
        //  1. TEXTE BRUT
        // ──────────────────────────────────────────────

        /// <summary>
        /// Lit l'intégralité du texte d'une image (imprimé et manuscrit) et le rend en markdown.
        /// Usage : page de livre scannée, bilan, courrier reçu.
        /// </summary>
        public Task<(bool success, string text, string? error)> ExtractTextAsync(
            byte[] imageBytes,
            CancellationToken cancellationToken = default)
        {
            // Consigne volontairement tenue sur UNE ligne : GLM-OCR est un modèle OCR, pas un
            // modèle d'instruction. Mesuré — une consigne multi-lignes est recopiée telle quelle
            // au milieu de la transcription.
            const string prompt = "Transcris le texte de cette image en markdown, sans commentaire.";

            return RunAsync(prompt, imageBytes, numPredict: 3000,
                StopMode.RepeatedLine, cancellationToken);
        }

        // ──────────────────────────────────────────────
        //  2. EXTRACTION STRUCTURÉE
        // ──────────────────────────────────────────────

        /// <summary>
        /// Extrait des données structurées d'une image selon un schéma JSON fourni par l'appelant.
        /// </summary>
        /// <param name="schemaJson">
        /// Gabarit JSON attendu, avec <c>null</c> pour les champs à remplir.
        /// Ex. : <c>{ "nom": null, "prenom": null }</c>
        /// </param>
        /// <returns>Le JSON brut, à parser par l'appelant.</returns>
        public async Task<(bool success, string json, string? error)> ExtractJsonAsync(
            byte[] imageBytes,
            string schemaJson,
            string? consigne = null,
            CancellationToken cancellationToken = default)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Extrais les informations de cette image.");
            if (!string.IsNullOrWhiteSpace(consigne))
                prompt.AppendLine(consigne);
            prompt.AppendLine();
            prompt.AppendLine("Réponds UNIQUEMENT avec un objet JSON valide : pas de commentaire,");
            prompt.AppendLine("aucun texte avant ou après, et n'écris qu'une seule fois l'objet.");
            prompt.AppendLine("Utilise null pour tout champ absent ou illisible — n'invente jamais de valeur.");
            prompt.AppendLine();
            prompt.AppendLine("Schéma attendu :");
            prompt.AppendLine(schemaJson);

            var (ok, raw, error) = await RunAsync(
                prompt.ToString(), imageBytes, numPredict: 1200,
                StopMode.JsonObject, cancellationToken).ConfigureAwait(false);

            if (!ok) return (false, "", error);

            var json = ExtractJsonBlock(raw);
            if (json == null)
                return (false, "", "Aucun objet JSON trouvé dans la réponse du modèle.");

            return (true, json, null);
        }

        // ──────────────────────────────────────────────
        //  3. CHAMP CONTRAINT (cases à lettres)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Lit une bande de « cases à lettres » (un caractère par case) — le cas des formulaires
        /// type CERFA. Contraindre le jeu de caractères élimine mécaniquement une grande part des
        /// erreurs : A-Z pour un nom, 0-9 pour un téléphone.
        /// </summary>
        /// <param name="charset">Description du jeu autorisé, ex. « lettres majuscules A-Z ».</param>
        /// <param name="expectedLength">Nombre de cases (data-len du template), si connu.</param>
        public async Task<(bool success, string value, string? error)> ExtractConstrainedAsync(
            byte[] stripImage,
            string charset,
            int? expectedLength = null,
            CancellationToken cancellationToken = default)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Cette image montre une rangée de cases contenant un seul caractère manuscrit par case.");
            prompt.AppendLine($"Caractères autorisés : {charset}.");
            if (expectedLength.HasValue)
                prompt.AppendLine($"La rangée comporte {expectedLength.Value} cases ; les cases vides sont en fin de rangée.");
            prompt.AppendLine();
            prompt.AppendLine("Réponds UNIQUEMENT par les caractères lus, de gauche à droite, sans espace,");
            prompt.AppendLine("sans ponctuation ajoutée, sur une seule ligne et sans aucun commentaire.");
            prompt.AppendLine("Ignore les cases vides. Si la rangée est entièrement vide, réponds exactement : VIDE");

            var (ok, raw, error) = await RunAsync(
                prompt.ToString(), stripImage, numPredict: 96,
                StopMode.FirstLine, cancellationToken).ConfigureAwait(false);

            if (!ok) return (false, "", error);

            // Une seule ligne attendue : on ignore tout ce que le modèle ajouterait après.
            var value = raw.Split('\n').FirstOrDefault()?.Trim().Trim('`').Trim() ?? "";

            if (string.Equals(value, "VIDE", StringComparison.OrdinalIgnoreCase))
                return (true, "", null);

            return (true, value, null);
        }

        // ──────────────────────────────────────────────
        //  NOYAU
        // ──────────────────────────────────────────────

        /// <summary>
        /// Mode de détection de fin, choisi selon ce que l'appel attend.
        /// </summary>
        private enum StopMode
        {
            /// <summary>Texte libre : on s'arrête quand la première ligne réapparaît (bouclage).</summary>
            RepeatedLine,
            /// <summary>JSON : on s'arrête dès que l'objet est complet (accolades équilibrées).</summary>
            JsonObject,
            /// <summary>Réponse d'une seule ligne : on s'arrête au premier saut de ligne utile.</summary>
            FirstLine
        }

        /// <summary>
        /// Appelle Ollama <b>en streaming</b> et coupe la génération dès que la réponse utile est
        /// complète.
        ///
        /// Indispensable ici : le build Ollama de GLM-OCR n'émet jamais sa fin de séquence et
        /// réémet la transcription en boucle jusqu'à épuiser <c>num_predict</c> — constaté sur
        /// <c>/api/generate</c> comme sur <c>/api/chat</c>, à température 0, quelle que soit la
        /// consigne (jusqu'à 19 répétitions mesurées). Attendre la fin puis nettoyer coûterait
        /// 30 s au lieu de 5 s. On coupe donc au vol, et le nettoyage en aval ne sert plus que de
        /// filet de sécurité.
        /// </summary>
        private async Task<(bool success, string text, string? error)> RunAsync(
            string prompt,
            byte[] imageBytes,
            int numPredict,
            StopMode stopMode = StopMode.RepeatedLine,
            CancellationToken cancellationToken = default)
        {
            var guard = CloudGuardError();
            if (guard != null) return (false, "", guard);

            if (imageBytes == null || imageBytes.Length == 0)
                return (false, "", "Image vide.");

            try
            {
                var prepared = ImageOps.EnsureMaxDimension(imageBytes, MaxDimension);

                var request = new
                {
                    model = _model,
                    prompt,
                    stream = true,
                    images = new[] { Convert.ToBase64String(prepared) },
                    options = new
                    {
                        num_ctx = NumCtx,
                        num_predict = numPredict,
                        // Le Modelfile officiel impose temperature 0 : l'OCR n'a rien à inventer.
                        temperature = 0
                    }
                };

                using var httpRequest = new HttpRequestMessage(
                    HttpMethod.Post, $"{_baseUrl}/api/generate")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
                };

                using var response = await _http
                    .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return (false, "", $"Erreur Ollama {response.StatusCode} : {err}");
                }

                var accumulated = new StringBuilder();
                int cutoff = -1;

                using (var stream = await response.Content
                           .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (line.Length == 0) continue;

                        string chunk;
                        bool done;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            chunk = doc.RootElement.TryGetProperty("response", out var r)
                                ? r.GetString() ?? "" : "";
                            done = doc.RootElement.TryGetProperty("done", out var d) && d.GetBoolean();
                        }
                        catch (JsonException)
                        {
                            continue; // fragment non exploitable : on poursuit
                        }

                        if (chunk.Length > 0)
                        {
                            accumulated.Append(chunk);

                            // Ne tester qu'aux frontières utiles : inutile à chaque token.
                            if (chunk.Contains('\n') || chunk.Contains('}'))
                            {
                                cutoff = FindCutoff(accumulated.ToString(), stopMode);
                                if (cutoff >= 0) break; // sortir dispose la réponse → coupe la génération
                            }
                        }

                        if (done) break;
                    }
                }

                var raw = accumulated.ToString();
                if (cutoff >= 0) raw = raw.Substring(0, cutoff);

                if (string.IsNullOrWhiteSpace(raw))
                    return (false, "", "Le modèle n'a renvoyé aucun texte.");

                return (true, CutRepetition(StripMarkdownFences(raw)), null);
            }
            catch (OperationCanceledException)
            {
                return (false, "", "Lecture annulée.");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur OCR : {ex.Message}");
            }
        }

        /// <summary>
        /// Position à laquelle la réponse utile se termine, ou -1 s'il faut continuer à lire.
        /// </summary>
        private static int FindCutoff(string acc, StopMode mode) => mode switch
        {
            StopMode.FirstLine   => FindFirstLineEnd(acc),
            StopMode.JsonObject  => FindJsonEnd(acc),
            _                    => FindRepeatStart(acc)
        };

        /// <summary>Fin de la première ligne non vide (réponses attendues sur une seule ligne).</summary>
        private static int FindFirstLineEnd(string acc)
        {
            int i = acc.IndexOf('\n');
            if (i < 0) return -1;
            return acc.Substring(0, i).Trim().Length > 0 ? i : -1;
        }

        /// <summary>Fin du premier objet JSON complet, accolades équilibrées hors chaînes.</summary>
        private static int FindJsonEnd(string acc)
        {
            int depth = 0;
            bool inString = false, escaped = false;

            for (int i = 0; i < acc.Length; i++)
            {
                char c = acc[i];

                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i + 1;
            }
            return -1;
        }

        /// <summary>
        /// Début de la répétition : index auquel la première ligne significative réapparaît.
        /// </summary>
        private static int FindRepeatStart(string acc)
        {
            var normalized = acc.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            if (lines.Length < 3) return -1;

            int anchor = -1;
            string anchorKey = "";
            for (int i = 0; i < lines.Length; i++)
            {
                var key = NormalizeLine(lines[i]);
                if (key.Length >= 12) { anchor = i; anchorKey = key; break; }
            }
            if (anchor < 0) return -1;

            // Position (en caractères) du début de chaque ligne.
            int offset = 0;
            for (int i = 0; i <= anchor; i++) offset += lines[i].Length + 1;

            for (int i = anchor + 1; i < lines.Length - 1; i++) // -1 : dernière ligne encore en cours
            {
                if (NormalizeLine(lines[i]) == anchorKey) return offset;
                offset += lines[i].Length + 1;
            }
            return -1;
        }

        /// <summary>
        /// Barrière de confidentialité : un modèle -cloud n'exécute pas l'inférence sur cette
        /// machine. Ce service traitant des documents patients, on refuse plutôt que d'alerter.
        /// </summary>
        private string? CloudGuardError() =>
            OllamaModelInfo.IsCloudModel(_model)
                ? $"Refus : « {_model} » est un modèle Ollama Cloud — les images partiraient sur des " +
                  "serveurs distants. L'OCR de documents patients doit rester local. " +
                  "Choisissez un modèle local dans AppSettings.OcrModel."
                : null;

        // ──────────────────────────────────────────────
        //  NETTOYAGE DE SORTIE
        // ──────────────────────────────────────────────

        /// <summary>
        /// GLM-OCR reboucle : il lui arrive de réémettre la transcription entière plusieurs fois
        /// d'affilée, avec ou sans balises de code, jusqu'à épuiser le budget de tokens.
        /// <c>repeat_penalty</c> ne l'en empêche pas — mesuré : 5 répétitions consécutives.
        /// On coupe donc en aval, dès que la première ligne significative réapparaît.
        /// </summary>
        internal static string CutRepetition(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');

            // Première ligne assez longue pour servir de signature fiable.
            int anchor = -1;
            string anchorKey = "";
            for (int i = 0; i < lines.Length; i++)
            {
                var key = NormalizeLine(lines[i]);
                if (key.Length >= 12) { anchor = i; anchorKey = key; break; }
            }

            var kept = lines.ToList();

            // Réapparition de cette même ligne ⇒ le modèle a rebouclé : on tronque avant.
            if (anchor >= 0)
            {
                for (int i = anchor + 1; i < lines.Length; i++)
                {
                    if (NormalizeLine(lines[i]) != anchorKey) continue;
                    kept = lines.Take(i).ToList();
                    break;
                }
            }

            // Toujours retirer les résidus de fin (amorce de bouclage « ```markdown », « : », etc.),
            // y compris quand la coupure a déjà eu lieu en streaming.
            while (kept.Count > 0 && IsNoise(kept[^1])) kept.RemoveAt(kept.Count - 1);

            return string.Join("\n", kept).Trim();
        }

        /// <summary>Forme comparable d'une ligne : sans balisage markdown, sans casse ni espaces multiples.</summary>
        private static string NormalizeLine(string line)
        {
            var s = line.Trim().TrimStart('#', '*', '>', '-', '`', ' ').Trim();
            s = Regex.Replace(s, @"\s+", " ");
            return s.ToLowerInvariant();
        }

        private static bool IsNoise(string line)
        {
            var s = line.Trim();
            return s.Length == 0 || s.StartsWith("```") || s == ":" || s == "---";
        }

        /// <summary>Retire les clôtures markdown (```json … ```) dont les modèles enrobent souvent leur sortie.</summary>
        private static string StripMarkdownFences(string raw)
        {
            var t = raw.Trim();
            if (!t.StartsWith("```")) return t;

            int firstBreak = t.IndexOf('\n');
            if (firstBreak < 0) return t.Trim('`').Trim();

            t = t.Substring(firstBreak + 1);
            int closing = t.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0) t = t.Substring(0, closing);

            return t.Trim();
        }

        /// <summary>Isole le premier objet JSON de la réponse, même si du texte l'entoure.</summary>
        private static string? ExtractJsonBlock(string raw)
        {
            var match = Regex.Match(raw, @"\{[\s\S]*\}", RegexOptions.Multiline);
            return match.Success ? match.Value : null;
        }

        private static string StripTag(string model)
        {
            int i = model.IndexOf(':');
            return i < 0 ? model : model.Substring(0, i);
        }
    }

    /// <summary>
    /// Opérations image utilisées par la chaîne OCR. S'appuie sur l'imagerie WPF : aucune
    /// dépendance externe à déployer.
    /// </summary>
    public static class ImageOps
    {
        /// <summary>Dimensions en pixels d'une image encodée.</summary>
        public static (int width, int height) GetSize(byte[] imageBytes)
        {
            var frame = Decode(imageBytes);
            return (frame.PixelWidth, frame.PixelHeight);
        }

        /// <summary>
        /// Découpe une région. Le rectangle est ramené dans les bornes de l'image : une carte de
        /// coordonnées légèrement débordante ne fait pas planter la lecture.
        /// </summary>
        public static byte[] Crop(byte[] imageBytes, Int32Rect rect)
        {
            var frame = Decode(imageBytes);

            int x = Math.Max(0, Math.Min(rect.X, frame.PixelWidth - 1));
            int y = Math.Max(0, Math.Min(rect.Y, frame.PixelHeight - 1));
            int w = Math.Max(1, Math.Min(rect.Width, frame.PixelWidth - x));
            int h = Math.Max(1, Math.Min(rect.Height, frame.PixelHeight - y));

            var cropped = new CroppedBitmap(frame, new Int32Rect(x, y, w, h));
            return EncodePng(cropped);
        }

        /// <summary>
        /// Réduit l'image si son côté le plus long dépasse <paramref name="maxDimension"/>.
        /// Une page A4 scannée à 300 dpi fait 2480×3508 : l'envoyer telle quelle coûte beaucoup de
        /// tokens visuels pour aucun gain de lisibilité.
        /// </summary>
        public static byte[] EnsureMaxDimension(byte[] imageBytes, int maxDimension)
        {
            var frame = Decode(imageBytes);
            int longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (longest <= maxDimension) return imageBytes;

            double scale = (double)maxDimension / longest;
            var scaled = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            return EncodePng(scaled);
        }

        private static BitmapFrame Decode(byte[] imageBytes)
        {
            using var ms = new MemoryStream(imageBytes);
            var decoder = BitmapDecoder.Create(
                ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }

        private static byte[] EncodePng(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Description d'un modèle servi par llama-server.
    ///
    /// Deux natures de champs, volontairement séparées :
    ///  • L'IDENTITÉ et les CAPACITÉS sont figées — chemins, présence des tenseurs MTP dans le GGUF,
    ///    support du raisonnement par le chat template. Ce sont des faits sur le fichier, pas des
    ///    préférences : les exposer au réglage ne ferait que produire des démarrages en erreur.
    ///  • Les RÉGLAGES sont modifiables et persistés — contexte, MTP actif, quantification du cache.
    ///    Ils sont pilotables depuis l'onglet Moteur local.
    ///
    /// Chaque profil correspond à un rôle : Gemma pour le volume et le long contexte, Qwen pour le
    /// raisonnement. Les deux ne tiennent pas ensemble en VRAM, donc changer de profil arrête et
    /// relance le serveur (~6-10 s mesurés).
    /// </summary>
    public sealed class LlamaCppModelProfile
    {
        // ── Identité et capacités (figées) ────────────────────────────────────

        /// <summary>Nom du modèle côté Ollama, utilisé pour retrouver le profil depuis le sélecteur.</summary>
        public required string Id { get; init; }

        public required string DisplayName { get; init; }

        /// <summary>Nom court pour l'interface de réglage.</summary>
        public required string ShortName { get; init; }

        public required string ModelPath { get; init; }

        /// <summary>Projecteur de vision, ou null si le modèle n'en a pas.</summary>
        public string? MmprojPath { get; init; }

        /// <summary>
        /// Le GGUF embarque lui-même les tenseurs de prédiction multi-tokens (`nextn`). Vérifié par
        /// inspection : présent sur la quantization Qwen, ABSENT du gemma4:12b standard.
        /// </summary>
        public bool HasMtpTensors { get; init; }

        /// <summary>
        /// Brouillon MTP livré dans un fichier séparé, à passer en `--spec-draft-model`. C'est la
        /// forme retenue par Gemma 4 : le modèle n'embarque pas les tenseurs, Google et Unsloth
        /// publient le brouillon à côté. Null quand le modèle se suffit à lui-même (Qwen).
        /// </summary>
        public string? DraftModelPath { get; init; }

        /// <summary>MTP possible, que les tenseurs soient dans le modèle ou dans un fichier à part.</summary>
        public bool MtpAvailable => HasMtpTensors || !string.IsNullOrEmpty(DraftModelPath);

        /// <summary>
        /// Le chat template accepte un niveau de réflexion. Un niveau envoyé à un modèle qui n'en
        /// veut pas fait échouer la requête côté serveur (exception Jinja, erreur 500) — ce n'est
        /// jamais ignoré silencieusement.
        /// </summary>
        public bool SupportsReasoning { get; init; }

        /// <summary>Contexte maximal supporté par le modèle, borne haute du réglage.</summary>
        public required int MaxContextSize { get; init; }

        public required int DefaultContextSize { get; init; }

        // ── Réglages (modifiables, persistés) ─────────────────────────────────

        private int _contextSize;

        /// <summary>Taille de contexte demandée au serveur. Bornée par <see cref="MaxContextSize"/>.</summary>
        public int ContextSize
        {
            get => _contextSize;
            set => _contextSize = Math.Clamp(value, 1024, MaxContextSize);
        }

        /// <summary>Prédiction multi-tokens. Toujours inopérant si le GGUF ne la porte pas.</summary>
        public bool MtpEnabled { get; set; }

        /// <summary>
        /// Cache KV en q8_0 plutôt qu'en pleine précision. C'est ce réglage — qu'Ollama n'expose pas
        /// — qui permet les contextes longs sans déborder de la VRAM. Le désactiver double
        /// l'empreinte du cache.
        /// </summary>
        public bool KvQuantized { get; set; } = true;

        private int _draftTokens = 3;

        /// <summary>
        /// Tokens que le brouillon propose par passe de vérification (`--spec-draft-n-max`).
        /// Réglage sensible et propre à chaque modèle — MESURÉ sur Qwen : 3 → acceptation 0,84 et
        /// 48 t/s ; 5 → acceptation 0,37 et 34 t/s. Au-delà de l'optimum, les tokens refusés sont
        /// générés puis jetés et coûtent plus qu'ils ne rapportent. À ne changer qu'en mesurant.
        /// </summary>
        public int DraftTokens
        {
            get => _draftTokens;
            set => _draftTokens = Math.Clamp(value, 1, 16);
        }

        public required int DefaultDraftTokens { get; init; }

        /// <summary>
        /// Taille attendue du GGUF en octets, ou 0 si inconnue. Sert à distinguer un fichier
        /// COMPLET d'un fichier simplement PRÉSENT : pendant un téléchargement, le fichier existe
        /// déjà mais tronqué, et le serveur échoue à le charger. Tester l'existence seule proposait
        /// donc un modèle inutilisable au choix de l'utilisateur.
        /// </summary>
        public long ExpectedSizeBytes { get; init; }

        /// <summary>Le modèle est présent ET complet — seul état où il peut être proposé.</summary>
        public bool IsReady
        {
            get
            {
                try
                {
                    var file = new System.IO.FileInfo(ModelPath);
                    if (!file.Exists) return false;
                    return ExpectedSizeBytes == 0 || file.Length >= ExpectedSizeBytes;
                }
                catch { return false; }
            }
        }

        /// <summary>Progression du téléchargement en cours (0-1), ou null si sans objet.</summary>
        public double? DownloadProgress
        {
            get
            {
                try
                {
                    if (ExpectedSizeBytes == 0) return null;
                    var file = new System.IO.FileInfo(ModelPath);
                    if (!file.Exists || file.Length >= ExpectedSizeBytes) return null;
                    return (double)file.Length / ExpectedSizeBytes;
                }
                catch { return null; }
            }
        }

        public bool HasVision => !string.IsNullOrEmpty(MmprojPath);

        /// <summary>MTP réellement applicable : demandé ET disponible.</summary>
        public bool MtpEffective => MtpEnabled && MtpAvailable;

        public void ResetToDefaults()
        {
            ContextSize = DefaultContextSize;
            MtpEnabled  = MtpAvailable;
            DraftTokens = DefaultDraftTokens;
            KvQuantized = true;
        }
    }

    /// <summary>Profils connus, résolution depuis le nom de modèle, et persistance des réglages.</summary>
    public static class LlamaCppProfiles
    {
        private const string ModelsDir = @"C:\Users\nair\llamacpp-models";

        /// <summary>
        /// Qwen3.8-27B — le raisonnement (courriers, rapports, restitutions).
        /// Contexte 32768 par défaut : meilleur compromis vitesse/contexte mesuré. Vision via le
        /// mmproj officiel Unsloth, la quantization communautaire ne l'embarquant pas.
        /// </summary>
        public static readonly LlamaCppModelProfile Qwen = new()
        {
            Id                 = "Qwen3.8-27B",
            ShortName          = "Qwen3.8-27B",
            DisplayName        = "hf.co/jrell/Qwen3.8-27B-i1-IQ4_XS-GGUF-Smaller (llama.cpp)",
            ModelPath          = $@"{ModelsDir}\Qwen3.8-27B-IQ4_XS.gguf",
            MmprojPath         = $@"{ModelsDir}\mmproj-F16.gguf",
            MaxContextSize     = 131072,
            DefaultContextSize = 32768,
            DefaultDraftTokens = 3,
            HasMtpTensors      = true,
            SupportsReasoning  = true,
        };

        /// <summary>
        /// Gemma 4 12B — le volume et le long contexte (analyse de PDF, conclusions de documents).
        /// Mesuré à 128k avec cache q8_0 : 8 956 Mo de VRAM, chargement ~10 s. Soit deux fois le
        /// contexte de la configuration Ollama (64k) pour 3,4 Go de moins.
        /// Projecteur de vision de 167 Mo présent (5× plus léger que celui de Qwen), non exploité.
        /// </summary>
        public static readonly LlamaCppModelProfile Gemma4 = new()
        {
            Id                 = "gemma4:12b",
            ShortName          = "Gemma 4 12B",
            DisplayName        = "gemma4:12b (llama.cpp)",
            ModelPath          = $@"{ModelsDir}\gemma4-12b.gguf",
            MmprojPath         = $@"{ModelsDir}\gemma4-12b-mmproj.gguf",
            MaxContextSize     = 131072,
            DefaultContextSize = 131072,
            DefaultDraftTokens = 4,
            HasMtpTensors      = false,
            SupportsReasoning  = false,
        };

        /// <summary>
        /// Gemma 4 12B en QAT, avec son brouillon MTP séparé — la variante d'Unsloth.
        /// Deux différences avec le profil ci-dessus, et elles sont indépendantes :
        ///  • QAT (Quantization-Aware Training) : le modèle est entraîné en tenant compte de la
        ///    quantification, donc le 4 bits perd nettement moins de qualité qu'une conversion
        ///    classique. Unsloth a retravaillé la conversion, la Q4_0 directe depuis le BF16 QAT
        ///    dégradant la précision.
        ///  • Le brouillon MTP, absent du GGUF standard, est ici fourni à part.
        /// Conservé en parallèle du profil standard pour pouvoir les comparer sur de vrais documents.
        /// </summary>
        public static readonly LlamaCppModelProfile Gemma4Qat = new()
        {
            Id                 = "gemma4-qat-mtp",
            ShortName          = "Gemma 4 12B QAT + MTP",
            DisplayName        = "gemma-4-12B-it-qat + MTP (llama.cpp)",
            ModelPath          = $@"{ModelsDir}\gemma4-12b-qat.gguf",
            DraftModelPath     = $@"{ModelsDir}\gemma4-12b-qat-mtp.gguf",
            MmprojPath         = $@"{ModelsDir}\gemma4-12b-qat-mmproj.gguf",
            ExpectedSizeBytes  = 6_716_356_800,   // taille publiée par Hugging Face
            MaxContextSize     = 131072,
            DefaultContextSize = 131072,
            DefaultDraftTokens = 4,
            HasMtpTensors      = false,
            SupportsReasoning  = false,
        };

        public static readonly IReadOnlyList<LlamaCppModelProfile> All = new[] { Qwen, Gemma4, Gemma4Qat };

        static LlamaCppProfiles()
        {
            foreach (var p in All) p.ResetToDefaults();
            LoadSettings();
        }

        /// <summary>
        /// Retrouve le profil correspondant à un nom de modèle Ollama, ou null si ce modèle doit
        /// rester sur Ollama. C'est ce test qui décide du routage dans le sélecteur de modèles.
        /// </summary>
        public static LlamaCppModelProfile? Resolve(string? modelName)
        {
            if (!Enabled) return null;   // moteur désactivé : tout repasse par Ollama
            if (string.IsNullOrWhiteSpace(modelName)) return null;
            return All.FirstOrDefault(p =>
                modelName.Contains(p.Id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Interrupteur général du moteur local. À false, aucun modèle n'est routé vers llama.cpp :
        /// tout retombe sur Ollama, y compris Qwen. Filet de sécurité si le moteur pose problème —
        /// l'application reste utilisable sans lui.
        ///
        /// Le routage est lu à la construction du sélecteur de modèles et au changement de modèle :
        /// après bascule, il faut donc re-sélectionner le modèle pour que ça prenne effet.
        /// </summary>
        public static bool Enabled { get; private set; } = true;

        /// <summary>
        /// Profils capables de lire une image — ceux qui ont un projecteur et dont le fichier est
        /// présent et complet.
        /// </summary>
        public static IEnumerable<LlamaCppModelProfile> VisionCapable =>
            All.Where(p => p.HasVision && p.IsReady && File.Exists(p.MmprojPath!));

        private static string _visionProfileId = Gemma4Qat.Id;

        /// <summary>
        /// Modèle utilisé pour les requêtes image, INDÉPENDANT du modèle de texte sélectionné.
        ///
        /// Les deux se dissocient parce que leurs contraintes diffèrent : Qwen en mode vision occupe
        /// 13,8 Go et déborde de la carte, là où Gemma 4 tient en 7,6 Go (mesuré) avec 7,7 Go de
        /// marge. On peut donc lire un formulaire avec Gemma tout en gardant Qwen pour rédiger.
        /// La lecture d'image provoque de toute façon un redémarrage du serveur (le MTP y est
        /// incompatible), donc changer de modèle au passage ne coûte rien de plus.
        /// </summary>
        public static LlamaCppModelProfile VisionProfile
        {
            get
            {
                var chosen = All.FirstOrDefault(p => p.Id == _visionProfileId);
                // Repli si le choix persisté n'est plus utilisable (fichier supprimé, profil retiré).
                if (chosen != null && chosen.HasVision && chosen.IsReady) return chosen;
                return VisionCapable.FirstOrDefault() ?? LlamaCppServerManager.CurrentProfile;
            }
        }

        public static void SetVisionProfile(LlamaCppModelProfile profile)
        {
            _visionProfileId = profile.Id;
            SaveSettings();
        }

        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (!enabled) LlamaCppServerManager.Stop();   // ne pas laisser un serveur orphelin en VRAM
            SaveSettings();
        }

        // ── Persistance ───────────────────────────────────────────────────────
        // Sérialisé en une ligne compacte dans AppSettings plutôt qu'en champs dédiés : le nombre de
        // profils est amené à bouger, et on ne veut pas modifier AppSettings à chaque ajout.
        // Format : "id=contexte,mtp,kv;id=contexte,mtp,kv"

        public static void LoadSettings()
        {
            try
            {
                var settings = AppSettings.Load();
                Enabled = settings.LlamaCppEnabled;

                if (!string.IsNullOrWhiteSpace(settings.LlamaCppVisionProfile)) _visionProfileId = settings.LlamaCppVisionProfile;

                var raw = settings.LlamaCppProfileSettings;
                if (string.IsNullOrWhiteSpace(raw)) return;

                foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = entry.Split('=', 2);
                    if (kv.Length != 2) continue;
                    var profile = All.FirstOrDefault(p => p.Id.Equals(kv[0], StringComparison.OrdinalIgnoreCase));
                    if (profile == null) continue;

                    var parts = kv[1].Split(',');
                    if (parts.Length >= 1 && int.TryParse(parts[0], out var ctx)) profile.ContextSize = ctx;
                    if (parts.Length >= 2 && bool.TryParse(parts[1], out var mtp)) profile.MtpEnabled  = mtp;
                    if (parts.Length >= 3 && bool.TryParse(parts[2], out var kvq)) profile.KvQuantized = kvq;
                    if (parts.Length >= 4 && int.TryParse(parts[3], out var dft)) profile.DraftTokens = dft;
                }
            }
            catch { /* réglages illisibles : on garde les valeurs par défaut */ }
        }

        public static void SaveSettings()
        {
            try
            {
                var raw = string.Join(";", All.Select(p =>
                    $"{p.Id}={p.ContextSize},{p.MtpEnabled},{p.KvQuantized},{p.DraftTokens}"));
                var settings = AppSettings.Load();
                settings.LlamaCppProfileSettings = raw;
                settings.LlamaCppEnabled         = Enabled;
                settings.LlamaCppVisionProfile   = _visionProfileId;
                settings.Save();
            }
            catch { /* la persistance ne doit jamais bloquer un réglage appliqué en mémoire */ }
        }
    }
}

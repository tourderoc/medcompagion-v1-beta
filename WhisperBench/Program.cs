using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Services.Consultation;
using NAudio.Wave;
using Whisper.net;

// ═══════════════════════════════════════════════════════════════════════════
//  Banc d'essai Whisper — rejoue un enregistrement déjà fait, en dehors de Med.
//
//  Il ne modifie RIEN dans Med : il rejoue l'audio sauvegardé des séances
//  (AppData\MedCompanion\recordings\session_*) avec plusieurs configurations,
//  et mesure. La carte graphique et le chemin CUDA sont résolus par le code de
//  Med lui-même, pour que les conditions soient les mêmes qu'en consultation.
//
//  Ce qu'on cherche à trancher (15/09-16/09/2026) :
//   • modèle : large-v3 générique ou large-v3 spécialisé français ;
//   • prompt : vocabulaire complet, vocabulaire court, ou aucun — c'est lui qui
//     est recraché en boucle quand un tronçon ne contient pas de parole ;
//   • découpage : 90 s fixes (actuel) ou ~30 s coupés sur un silence.
// ═══════════════════════════════════════════════════════════════════════════

const int SampleRate = 16000;

var sessions = new List<string>();
string? dossierSortie = null, cheminReference = null;
string choixModeles = "tous", choixPrompts = "tous", choixDecoupes = "90";

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--session": sessions.Add(args[++i]); break;
        case "--out":     dossierSortie = args[++i]; break;
        case "--ref":     cheminReference = args[++i]; break;
        case "--modele":  choixModeles = args[++i]; break;
        case "--prompt":  choixPrompts = args[++i]; break;
        case "--seg":     choixDecoupes = args[++i]; break;
        case "-h":
        case "--help":    AfficherAide(); return 0;
    }
}

if (sessions.Count == 0) { AfficherAide(); return 1; }

var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
dossierSortie ??= Path.Combine(appData, "MedCompanion", "bench-whisper",
                               DateTime.Now.ToString("yyyyMMdd_HHmmss"));
Directory.CreateDirectory(dossierSortie);

// ── Modèles disponibles ────────────────────────────────────────────────────
var dossierModeles = MedCompanion.AppSettings.Load().WhisperModelsDir;
if (string.IsNullOrWhiteSpace(dossierModeles))
    dossierModeles = Path.Combine(appData, "MedCompanion", "models");

var modeles = new Dictionary<string, string>
{
    ["large-v3"] = Path.Combine(dossierModeles, "ggml-large-v3.bin"),
    ["francais"] = Path.Combine(dossierModeles, "ggml-large-v3-french.bin"),
};

// ── Prompts : les trois variantes à comparer ───────────────────────────────
var vocab = new WhisperVocabService();
vocab.Load();
const string PromptDeBase = "Conversation médicale entre un médecin et une famille en français. ";

var promptComplet = PromptDeBase + vocab.BuildPromptFragment();   // ce que Med envoie aujourd'hui
var promptCourt   = PromptDeBase + ConstruirePromptCourt(vocab);  // noms propres seuls, sans intitulés

var prompts = new Dictionary<string, string>
{
    ["complet"] = promptComplet,
    ["court"]   = promptCourt,
    ["aucun"]   = "",
};

var decoupes = new Dictionary<string, (int Secondes, bool AuSilence)>
{
    ["90"]    = (90, false),   // l'actuel
    ["30vad"] = (30, true),    // vise 30 s, coupe au prochain silence (+10 s max)
};

// ── Audio ──────────────────────────────────────────────────────────────────
var audio = new List<float>();
foreach (var s in sessions)
{
    var n = ChargerSession(s, audio);
    Console.WriteLine($"Session {Path.GetFileName(s)} : {n} fichier(s), total {audio.Count / (double)SampleRate / 60:F1} min");
}
if (audio.Count == 0) { Console.WriteLine("Aucun audio lu — arrêt."); return 1; }
var echantillons = audio.ToArray();

string? reference = cheminReference != null && File.Exists(cheminReference)
    ? File.ReadAllText(cheminReference) : null;
if (cheminReference != null && reference == null)
    Console.WriteLine($"⚠ Référence introuvable : {cheminReference} — pas de taux d'erreur.");

// ── Configurations à jouer ─────────────────────────────────────────────────
var listeModeles  = Choisir(choixModeles,  modeles.Keys);
var listePrompts  = Choisir(choixPrompts,  prompts.Keys);
var listeDecoupes = Choisir(choixDecoupes, decoupes.Keys);

var absents = listeModeles.Where(m => !File.Exists(modeles[m])).ToList();
foreach (var m in absents)
    Console.WriteLine($"⚠ Modèle absent, ignoré : {modeles[m]}");
listeModeles = listeModeles.Except(absents).ToList();
if (listeModeles.Count == 0) { Console.WriteLine("Aucun modèle disponible — arrêt."); return 1; }

Console.WriteLine($"\n{listeModeles.Count * listePrompts.Count * listeDecoupes.Count} configuration(s) à jouer.");
Console.WriteLine($"Sortie : {dossierSortie}\n");

WhisperStreamingService.EnsureCudaInPath();

var resultats = new List<Resultat>();

// Groupé par modèle : le fichier (3 Go) n'est chargé qu'une fois par modèle.
foreach (var nomModele in listeModeles)
{
    Console.WriteLine($"── Chargement du modèle {nomModele} …");
    var chrono  = System.Diagnostics.Stopwatch.StartNew();
    using var factory = WhisperStreamingService.CreerFactory(modeles[nomModele]);
    Console.WriteLine($"   chargé en {chrono.Elapsed.TotalSeconds:F1} s");

    foreach (var nomDecoupe in listeDecoupes)
    {
        var (secondes, auSilence) = decoupes[nomDecoupe];
        var troncons = Decouper(echantillons, secondes, auSilence);

        foreach (var nomPrompt in listePrompts)
        {
            var nom = $"{nomModele}_{nomPrompt}_{nomDecoupe}";
            Console.Write($"   {nom} : {troncons.Count} tronçons ");

            using var processeur = factory.CreateBuilder()
                                          .WithLanguage("fr")
                                          .WithNoContext()
                                          .WithPrompt(prompts[nomPrompt])
                                          .WithTemperature(0f)
                                          .Build();

            var textes = new List<string>();
            var durees = new List<double>();

            foreach (var t in troncons)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var sb = new StringBuilder();
                await foreach (var segment in processeur.ProcessAsync(t))
                    sb.Append(segment.Text);
                sw.Stop();
                textes.Add(sb.ToString().Trim());
                durees.Add(sw.Elapsed.TotalSeconds);
                Console.Write(".");
            }

            var texteComplet = string.Join("\n", textes);
            File.WriteAllText(Path.Combine(dossierSortie, nom + ".txt"), texteComplet, Encoding.UTF8);

            var r = new Resultat(
                Nom: nom, Modele: nomModele, Prompt: nomPrompt, Decoupe: nomDecoupe,
                Troncons: troncons.Count,
                SecondesTotal: durees.Sum(),
                SecondesMax: durees.Count > 0 ? durees.Max() : 0,
                TronconsEchoPrompt: textes.Count(t => EchoDuPrompt(t, prompts[nomPrompt])),
                RepetitionMax: textes.Count > 0 ? textes.Max(RepetitionMax) : 0,
                TermesTrouves: TermesTrouves(texteComplet, vocab),
                Mots: Normaliser(texteComplet).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                Wer: reference != null ? Wer(reference, texteComplet) : null);

            resultats.Add(r);
            Console.WriteLine($" {r.SecondesTotal:F0}s · écho prompt {r.TronconsEchoPrompt} · boucle max {r.RepetitionMax}"
                            + (r.Wer.HasValue ? $" · WER {r.Wer:P1}" : ""));
        }
    }
}

EcrireRapport(Path.Combine(dossierSortie, "rapport.md"), resultats, sessions, echantillons.Length, vocab, reference != null);
Console.WriteLine($"\nRapport : {Path.Combine(dossierSortie, "rapport.md")}");
return 0;

// ═══════════════════════════════════════════════════════════════════════════

static void AfficherAide()
{
    Console.WriteLine("""
        Banc d'essai Whisper — rejoue une séance enregistrée, hors de Med.

          WhisperBench --session <dossier_session> [--session <autre>]
                       [--out <dossier>] [--ref <transcription_corrigee.txt>]
                       [--modele large-v3|francais|tous]
                       [--prompt complet|court|aucun|tous]
                       [--seg 90|30vad|tous]

        Par défaut : tous les modèles, tous les prompts, découpage 90 s (phase 1).
        Les sessions sont dans %APPDATA%\\MedCompanion\\recordings\\session_*.
        """);
}

static List<string> Choisir(string choix, IEnumerable<string> toutes) =>
    choix.Equals("tous", StringComparison.OrdinalIgnoreCase)
        ? toutes.ToList()
        : choix.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();

/// <summary>
/// Vocabulaire réduit aux noms propres et sigles — ce que le modèle ne peut pas deviner
/// (Médikinet, Vyvanse, WPPSI-IV). Les intitulés de sections et les expressions courantes
/// sont écartés : ce sont eux qu'on a vus recrachés en boucle.
/// </summary>
static string ConstruirePromptCourt(WhisperVocabService vocab)
{
    var gardes = new List<string>();
    foreach (var e in vocab.Entries)
    {
        var t = e.Trim();
        if (t.Length == 0 || t.Contains(' ')) continue;                 // expressions courantes : le modèle sait
        if (!(Regex.IsMatch(t, "[A-Z]{2,}|[0-9]") || char.IsUpper(t[0]))) continue;
        gardes.Add(t);
    }

    var sb = new StringBuilder();
    foreach (var g in gardes)
    {
        if (sb.Length + g.Length + 2 > 700) break;   // ~200 tokens : la limite de prompt de Whisper
        if (sb.Length > 0) sb.Append(", ");
        sb.Append(g);
    }
    return sb.Length > 0 ? sb.ToString() + "." : "";
}

static int ChargerSession(string dossier, List<float> sortie)
{
    if (!Directory.Exists(dossier)) { Console.WriteLine($"⚠ Dossier introuvable : {dossier}"); return 0; }

    var fichiers = Directory.GetFiles(dossier, "*.wav").OrderBy(f => f, StringComparer.Ordinal).ToList();
    foreach (var f in fichiers)
    {
        using var lecteur = new WaveFileReader(f);
        var tampon = new byte[lecteur.Length];
        var lus    = lecteur.Read(tampon, 0, tampon.Length);
        for (int i = 0; i + 1 < lus; i += 2)
            sortie.Add(BitConverter.ToInt16(tampon, i) / 32768f);
    }
    return fichiers.Count;
}

/// <summary>
/// Découpe l'audio. À 90 s : coupe sèche, comme Med aujourd'hui. À 30 s « au silence » : vise la
/// durée, puis attend le prochain silence (jusqu'à +10 s) pour couper sur une pause plutôt qu'au
/// milieu d'une phrase.
/// </summary>
static List<float[]> Decouper(float[] audio, int secondes, bool auSilence)
{
    const float SeuilSilence = 0.015f;
    const int   FenetreMs    = 100;
    const int   SilenceMs    = 400;
    const int   RallongeMax  = 10;

    var res    = new List<float[]>();
    int cible  = secondes * SampleRate;
    int debut  = 0;

    while (debut < audio.Length)
    {
        int fin = Math.Min(debut + cible, audio.Length);

        if (auSilence && fin < audio.Length)
        {
            int limite  = Math.Min(debut + cible + RallongeMax * SampleRate, audio.Length);
            int fenetre = FenetreMs * SampleRate / 1000;
            int requis  = SilenceMs / FenetreMs;
            int compte  = 0;

            for (int p = fin; p + fenetre <= limite; p += fenetre)
            {
                if (Rms(audio, p, fenetre) < SeuilSilence)
                {
                    if (++compte >= requis) { fin = p + fenetre; break; }
                }
                else compte = 0;
            }
        }

        res.Add(audio[debut..fin]);
        debut = fin;
    }
    return res;
}

static float Rms(float[] a, int debut, int longueur)
{
    double somme = 0;
    int fin = Math.Min(debut + longueur, a.Length);
    for (int i = debut; i < fin; i++) somme += a[i] * (double)a[i];
    return fin > debut ? (float)Math.Sqrt(somme / (fin - debut)) : 0f;
}

// ── Mesures ────────────────────────────────────────────────────────────────

static string Normaliser(string t)
{
    t = t.ToLowerInvariant();
    t = Regex.Replace(t, @"[^\p{L}\p{N}\s]", " ");
    return Regex.Replace(t, @"\s+", " ").Trim();
}

/// <summary>
/// Vrai si le tronçon recopie le prompt : une suite de 6 mots du prompt s'y retrouve telle quelle.
/// C'est la signature de la boucle observée le 16/09 (le vocabulaire répété à l'identique).
/// </summary>
static bool EchoDuPrompt(string texte, string prompt)
{
    if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(texte)) return false;

    var motsPrompt = Normaliser(prompt).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var normalise  = " " + Normaliser(texte) + " ";
    if (motsPrompt.Length < 6) return false;

    for (int i = 0; i + 6 <= motsPrompt.Length; i++)
    {
        var suite = " " + string.Join(' ', motsPrompt.Skip(i).Take(6)) + " ";
        if (normalise.Contains(suite, StringComparison.Ordinal)) return true;
    }
    return false;
}

/// <summary>Nombre de fois où la phrase la plus répétée revient dans un même tronçon.</summary>
static int RepetitionMax(string texte)
{
    var phrases = texte.Split(['.', '?', '!'], StringSplitOptions.RemoveEmptyEntries)
                       .Select(Normaliser)
                       .Where(p => p.Length > 12)
                       .ToList();
    if (phrases.Count == 0) return 0;
    return phrases.GroupBy(p => p).Max(g => g.Count());
}

static int TermesTrouves(string texte, WhisperVocabService vocab)
{
    var n = Normaliser(texte);
    return vocab.Entries.Count(e => !string.IsNullOrWhiteSpace(e) && n.Contains(Normaliser(e), StringComparison.Ordinal));
}

/// <summary>Taux d'erreur mot (insertions + suppressions + substitutions) contre la référence.</summary>
static double Wer(string reference, string hypothese)
{
    var r = Normaliser(reference).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var h = Normaliser(hypothese).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (r.Length == 0) return 0;

    var precedent = new int[h.Length + 1];
    var courant   = new int[h.Length + 1];
    for (int j = 0; j <= h.Length; j++) precedent[j] = j;

    for (int i = 1; i <= r.Length; i++)
    {
        courant[0] = i;
        for (int j = 1; j <= h.Length; j++)
        {
            var cout = r[i - 1] == h[j - 1] ? 0 : 1;
            courant[j] = Math.Min(Math.Min(courant[j - 1] + 1, precedent[j] + 1), precedent[j - 1] + cout);
        }
        (precedent, courant) = (courant, precedent);
    }
    return precedent[h.Length] / (double)r.Length;
}

static void EcrireRapport(string chemin, List<Resultat> resultats, List<string> sessions,
                          int echantillons, WhisperVocabService vocab, bool avecReference)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Banc d'essai Whisper");
    sb.AppendLine();
    sb.AppendLine($"- Date : {DateTime.Now:dd/MM/yyyy HH:mm}");
    sb.AppendLine($"- Audio : {echantillons / (double)SampleRate / 60:F1} min — {string.Join(", ", sessions.Select(Path.GetFileName))}");
    sb.AppendLine($"- Vocabulaire personnalisé : {vocab.Count} entrées");
    sb.AppendLine();
    sb.AppendLine("| Configuration | Tronçons | Transcription | Pire tronçon | Écho du prompt | Boucle max | Termes trouvés | Mots" + (avecReference ? " | WER |" : " |"));
    sb.AppendLine("|---|---|---|---|---|---|---|---" + (avecReference ? "|---|" : "|"));

    foreach (var r in resultats.OrderBy(r => r.Modele).ThenBy(r => r.Prompt).ThenBy(r => r.Decoupe))
    {
        sb.Append($"| {r.Nom} | {r.Troncons} | {r.SecondesTotal:F0} s | {r.SecondesMax:F0} s | "
                + $"{r.TronconsEchoPrompt} | {r.RepetitionMax} | {r.TermesTrouves} | {r.Mots} ");
        sb.AppendLine(avecReference ? $"| {r.Wer:P1} |" : "|");
    }

    sb.AppendLine();
    sb.AppendLine("**Lecture :** « écho du prompt » compte les tronçons où une suite de six mots du prompt");
    sb.AppendLine("est recopiée telle quelle — c'est le défaut observé le 16/09. « Boucle max » compte la");
    sb.AppendLine("phrase la plus répétée dans un même tronçon. « Termes trouvés » indique combien d'entrées");
    sb.AppendLine("du vocabulaire apparaissent dans la transcription (indice, pas une preuve de justesse).");
    sb.AppendLine();
    sb.AppendLine("Les transcriptions complètes sont dans les fichiers `.txt` à côté de ce rapport.");

    File.WriteAllText(chemin, sb.ToString(), Encoding.UTF8);
}

record Resultat(string Nom, string Modele, string Prompt, string Decoupe, int Troncons,
                double SecondesTotal, double SecondesMax, int TronconsEchoPrompt, int RepetitionMax,
                int TermesTrouves, int Mots, double? Wer);

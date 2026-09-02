using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services;
using MedCompanion.Services.Evaluations;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;

// Évaluation ciblée (3e séance) : construction séquentielle des axes, et aller-retour de la fiche.
// Le point le plus surveillé : « non observé » ne doit JAMAIS se relire comme un « non ».

class FauxMoteur : ILLMService, IStructuredOutputService
{
    public List<string> Prompts { get; } = new();
    public Func<int, (bool ok, string json)>? Reponse;

    public bool SupportsStructuredOutput { get; set; } = true;

    public Task<(bool success, string result, string? error)> GenerateJsonAsync(
        string prompt, string schemaName, string jsonSchema,
        int maxTokens = 1500, CancellationToken ct = default)
    {
        Prompts.Add(prompt);
        var (ok, json) = Reponse!(Prompts.Count - 1);
        return Task.FromResult(ok ? (true, json, (string?)null) : (false, "", (string?)"panne simulée"));
    }

    public Task<(bool success, string result, string? error)> GenerateTextAsync(
        string prompt, int maxTokens = 1500, CancellationToken ct = default, string? forceModel = null)
        => GenerateJsonAsync(prompt, "", "", maxTokens, ct);

    public string GetProviderName() => "faux";
    public string GetModelName() => "faux";
    public bool IsConfigured() => true;
    public Task<(bool isConnected, string message)> CheckConnectionAsync() => Task.FromResult((true, ""));
    public Task<(bool success, string message)> WarmupAsync() => Task.FromResult((true, ""));
    public Task<(bool success, string message)> UnloadAsync() => Task.FromResult((true, ""));
    public Task<(bool success, string result, string? error)> ChatAsync(string s, List<(string role, string content)> m,
        int mt = 1500, CancellationToken ct = default, string? fm = null, int? nc = null)
        => Task.FromResult((true, "", (string?)null));
    public Task<(bool success, string fullResponse, string? error)> ChatStreamAsync(string s,
        List<(string role, string content)> m, Action<string> cb, int mt = 1500, CancellationToken ct = default)
        => Task.FromResult((true, "", (string?)null));
    public Task<(bool success, string result, string? error)> AnalyzeImageAsync(string p, byte[] d,
        int mt = 1500, CancellationToken ct = default)
        => Task.FromResult((true, "", (string?)null));
}

class Program
{
    static int echecs = 0;

    static void Verifie(string quoi, bool condition, string? detail = null)
    {
        Console.WriteLine($"{(condition ? "  ok  " : " ÉCHEC")} {quoi}{(detail == null ? "" : "  → " + detail)}");
        if (!condition) echecs++;
    }

    static AxesCiblesSuggesterService.Orientation OrientationType() => new()
    {
        Hypotheses    = new() { "TDAH presentation inattentive" },
        Differentiels = new() { "Trouble anxieux" },
        Vigilance     = new() { "Sommeil irregulier" }
    };

    static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ── 1. Construction séquentielle ──────────────────────────────────────
        Console.WriteLine("── construction des axes ──");
        List<AxesCiblesSuggesterService.AxeSuggere>? axesSug = null;
        {
            var moteur = new FauxMoteur
            {
                Reponse = i => (true, i switch
                {
                    0 => """{"axes":[{"intitule":"Attention soutenue","rattachement":"TDAH presentation inattentive"},{"intitule":"Separation","rattachement":"Trouble anxieux"}]}""",
                    1 => """{"propositions":["Se retourne quand on entre","Soutient une tache 10 minutes","Perd le fil de sa phrase"]}""",
                    _ => """{"propositions":["Cherche le parent du regard","Accepte de rester seul"]}"""
                })
            };
            var svc = new AxesCiblesSuggesterService(moteur);

            var ordre = new List<string>();
            var progres = new List<string>();

            var (ok, axes, err) = await svc.SuggestAsync(
                8, "agitation scolaire", OrientationType(), "cartographie : attachement 4/6",
                onAxe: a => ordre.Add(a.Intitule),
                onProgres: m => progres.Add(m));

            Verifie("appel réussi", ok && axes != null, err);
            axesSug = axes;

            Verifie("1 appel pour les axes + 1 par axe", moteur.Prompts.Count == 3, $"{moteur.Prompts.Count}");
            Verifie("2 axes construits", axes!.Count == 2, $"{axes.Count}");
            Verifie("rattachement repris de l'orientation",
                axes[0].Rattachement == "TDAH presentation inattentive", axes[0].Rattachement);
            Verifie("propositions posées sur chaque axe",
                axes[0].Propositions.Count == 3 && axes[1].Propositions.Count == 2);
            Verifie("axes remontés au fil de l'eau", string.Join(">", ordre) == "Attention soutenue>Separation",
                string.Join(">", ordre));
            Verifie("avancement numéroté", progres.Count == 3 && progres[1].Contains("1/2"), string.Join(" | ", progres));

            // Le socle doit être identique et en tête des 3 prompts (cache de préfixe).
            var tete = moteur.Prompts[0].Substring(0, moteur.Prompts[0].IndexOf("═══ TRAVAIL"));
            Verifie("socle identique en tête des 3 prompts", moteur.Prompts.All(p => p.StartsWith(tete)),
                $"préfixe partagé = {tete.Length} car.");
            Verifie("l'orientation est dans le socle", tete.Contains("TDAH presentation inattentive"));
            Verifie("le 2e axe voit les propositions du 1er",
                moteur.Prompts[2].Contains("AXES DÉJÀ TRAITÉS")
                && moteur.Prompts[2].Contains("Se retourne quand on entre"));
            Verifie("le prompt d'un axe rappelle ce que l'axe sert",
                moteur.Prompts[1].Contains("Cet axe sert à trancher : TDAH presentation inattentive"));
        }

        // ── 2. Refus quand l'orientation est vide ─────────────────────────────
        Console.WriteLine();
        Console.WriteLine("── orientation vide ──");
        {
            var moteur = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svc = new AxesCiblesSuggesterService(moteur);
            var (ok, _, err) = await svc.SuggestAsync(8, "", new AxesCiblesSuggesterService.Orientation());

            Verifie("refuse plutôt que de produire des axes génériques", !ok);
            Verifie("aucun appel LLM dépensé", moteur.Prompts.Count == 0, $"{moteur.Prompts.Count}");
            Verifie("le message renvoie à l'étape précédente",
                err != null && err.Contains("orientation"), err);
        }

        // ── 3. Un axe sans propositions n'emporte pas les autres ──────────────
        Console.WriteLine();
        Console.WriteLine("── panne sur un seul axe ──");
        {
            var moteur = new FauxMoteur
            {
                Reponse = i => i switch
                {
                    0 => (true, """{"axes":[{"intitule":"A","rattachement":"h"},{"intitule":"B","rattachement":"h"}]}"""),
                    1 => (false, ""),
                    _ => (true, """{"propositions":["x","y"]}""")
                }
            };
            var svc = new AxesCiblesSuggesterService(moteur);
            var (ok, axes, err) = await svc.SuggestAsync(8, "", OrientationType());

            Verifie("appel globalement réussi", ok);
            Verifie("les 2 axes sont là", axes!.Count == 2);
            Verifie("l'axe A garde sa charpente sans propositions",
                axes[0].Intitule == "A" && axes[0].Propositions.Count == 0);
            Verifie("l'axe B est complet", axes[1].Propositions.Count == 2);
            Verifie("l'échec est nommé", err != null && err.Contains("A"), err);
        }

        // ── 4. Aller-retour de la fiche ───────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("── aller-retour de la fiche ──");
        {
            var racine = Path.Combine(Path.GetTempPath(), "medtest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(racine);

            var svcFiche = new SeanceEnvironnementService();
            var vm = new MedCompanion.ViewModels.EvaluationCibleeV2ViewModel();
            foreach (var a in axesSug!) vm.AjouterSuggestion(a);

            // Le médecin coche : un oui, un non, et laisse le 3e non observé.
            var axe0 = vm.Axes[0];
            axe0.Propositions[0].Basculer(ReponseProposition.Oui);
            axe0.Propositions[1].Basculer(ReponseProposition.Non);
            axe0.Remarques = "décroche après 10 min\nmais se remobilise seul";
            vm.Axes[1].Propositions[0].Basculer(ReponseProposition.Oui);

            Verifie("compteur ignore le non observé", axe0.NbRepondu == 2 && axe0.NbNonObserve == 1,
                $"répondu={axe0.NbRepondu} non observé={axe0.NbNonObserve}");
            Verifie("avancement lisible", vm.AvancementText == "2 axes — 3/5 constats renseignés", vm.AvancementText);

            var fiche = new SeanceEnvironnement
            {
                PatientNom = "GOBLET Adrien", Age = 8, Date = new DateTime(2026, 9, 2),
                HypothesesPrincipales = new() { "TDAH presentation inattentive" },
                OrientationDate = DateTime.Now,
                Axes = vm.ToList(),
                EvaluationDate = DateTime.Now
            };

            var (okSave, path, errSave) = svcFiche.Save(racine, fiche);
            Verifie("écriture", okSave, errSave);

            var relu = svcFiche.Load(path!)!;
            Verifie("relecture", relu != null);
            Verifie("2 axes relus", relu.Axes.Count == 2, $"{relu.Axes.Count}");
            Verifie("intitulé conservé", relu.Axes[0].Intitule == "Attention soutenue", relu.Axes[0].Intitule);
            Verifie("rattachement conservé",
                relu.Axes[0].Rattachement == "TDAH presentation inattentive", relu.Axes[0].Rattachement);

            var p = relu.Axes[0].Propositions;
            Verifie("3 propositions relues", p.Count == 3, $"{p.Count}");
            Verifie("texte conservé", p[0].Texte == "Se retourne quand on entre", p[0].Texte);
            Verifie("OUI relu comme OUI", p[0].EstOui);
            Verifie("NON relu comme NON", p[1].EstNon);
            Verifie("NON OBSERVÉ ne devient PAS un NON", p[2].EstNonObservee && !p[2].EstNon,
                $"état relu = {p[2].Reponse}");
            Verifie("remarques multi-lignes conservées",
                relu.Axes[0].Remarques == "décroche après 10 min\nmais se remobilise seul",
                relu.Axes[0].Remarques.Replace("\n", "⏎"));
            Verifie("l'orientation cohabite avec l'évaluation",
                relu.HypothesesPrincipales.Count == 1 && relu.Axes.Count == 2);
            Verifie("compteurs rebranchés après relecture",
                relu.Axes[0].NbRepondu == 2 && relu.EtatEvaluationLisible == "2/2 axes renseignés",
                relu.EtatEvaluationLisible);

            // La fiche doit rester lisible à l'œil nu.
            var texte = File.ReadAllText(path!);
            Verifie("la fiche dit ce qu'une case vide signifie",
                texte.Contains("Une case vide signifie « non observé » — jamais « non »"));
            Console.WriteLine();
            Console.WriteLine("── extrait de la fiche ──");
            var i = texte.IndexOf("## Évaluation ciblée");
            Console.WriteLine(texte[i..Math.Min(texte.Length, i + 700)]);

            // Clôture
            relu.DateCloture = DateTime.Now;
            var (okC, _, _) = svcFiche.Save(racine, relu);
            var (okApres, _, errApres) = svcFiche.Save(racine, relu);
            Verifie("clôture acceptée", okC);
            Verifie("écriture refusée après clôture", !okApres, errApres);

            try { Directory.Delete(racine, true); } catch { }
        }

        // ── 5. Cotation de l'environnement, versant médecin ───────────────────
        Console.WriteLine();
        Console.WriteLine("── cotation environnement (14 items médecin) ──");
        {
            var vm = new MedCompanion.ViewModels.CartographieEnvMedecinViewModel();

            var lignes = vm.Feuilles.SelectMany(f => f.Nervures).SelectMany(n => n.Lignes).ToList();
            var med    = lignes.Where(l => l.EstMedecin).ToList();

            Verifie("les 4 feuilles sont là", vm.Feuilles.Count == 4, $"{vm.Feuilles.Count}");
            Verifie("36 lignes au total", lignes.Count == CartographieEnvironnementV2.NbItems, $"{lignes.Count}");
            Verifie("14 items médecin cliquables", med.Count == 14, $"{med.Count}");
            Verifie("22 items parent en attente",
                lignes.Count(l => l.EstParent) == 22, $"{lignes.Count(l => l.EstParent)}");
            Verifie("rien de coté au départ", vm.NbCote == 0 && vm.AvancementText == "14 items à coter",
                vm.AvancementText);

            // Une nervure dit ce qu'elle attend, sans couleur.
            var centrale = vm.Feuilles[0].Nervures[0];
            Verifie("la nervure annonce ce qui manque",
                centrale.EtatText.Contains("feuille parent"), centrale.EtatText);

            // Le médecin cote : deux oui, un non, et laisse le reste.
            med[0].Basculer(ReponseProposition.Oui);
            med[1].Basculer(ReponseProposition.Non);
            med[2].Basculer(ReponseProposition.Oui);

            Verifie("3 items cotés", vm.NbCote == 3, vm.AvancementText);

            // Se dédire : recliquer OUI sur un OUI le remet à non renseigné.
            med[2].Basculer(ReponseProposition.Oui);
            Verifie("recliquer décoche", vm.NbCote == 2, vm.AvancementText);
            med[2].Basculer(ReponseProposition.Oui);

            var d = vm.ToDictionary();
            Verifie("seuls les items répondus sont exportés", d.Count == 3, $"{d.Count}");

            var racine = Path.Combine(Path.GetTempPath(), "medtest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(racine);
            var svcFiche = new SeanceEnvironnementService();

            var fiche = new SeanceEnvironnement
            {
                PatientNom = "GOBLET Adrien", Age = 8, Date = new DateTime(2026, 9, 2),
                HypothesesPrincipales = new() { "TDAH presentation inattentive" },
                CotationsEnv = d, CotationEnvDate = DateTime.Now
            };

            var (okS, path, errS) = svcFiche.Save(racine, fiche);
            Verifie("écriture", okS, errS);

            var relu = svcFiche.Load(path!)!;
            Verifie("3 cotations relues", relu.CotationsEnv.Count == 3, $"{relu.CotationsEnv.Count}");
            Verifie("OUI relu comme OUI", relu.CotationsEnv[med[0].Texte] == ReponseProposition.Oui);
            Verifie("NON relu comme NON", relu.CotationsEnv[med[1].Texte] == ReponseProposition.Non);
            Verifie("un item non coté reste ABSENT, pas un non",
                !relu.CotationsEnv.ContainsKey(med[5].Texte));
            Verifie("état lisible", relu.EtatCotationEnvLisible == "3/14 items cotés",
                relu.EtatCotationEnvLisible);

            // Rechargement dans un VM neuf : les coches reviennent au bon endroit.
            var vm2 = new MedCompanion.ViewModels.CartographieEnvMedecinViewModel();
            vm2.Charger(relu);
            var med2 = vm2.Feuilles.SelectMany(f => f.Nervures).SelectMany(n => n.Lignes)
                          .Where(l => l.EstMedecin).ToList();
            Verifie("rechargement fidèle",
                med2[0].EstOui && med2[1].EstNon && !med2[5].EstOui && !med2[5].EstNon);
            Verifie("compteur rechargé", vm2.NbCote == 3, vm2.AvancementText);

            var texte = File.ReadAllText(path!);
            Verifie("la fiche dit ce qu'une case vide signifie",
                texte.Contains("« non renseigné » — jamais « non »"));
            Verifie("la fiche garde l'ordre des feuilles",
                texte.IndexOf("### Famille") < texte.IndexOf("### École & Pairs")
                || !texte.Contains("### École & Pairs"));

            Console.WriteLine();
            Console.WriteLine("── extrait de la fiche ──");
            var i = texte.IndexOf("## Cartographie de l'environnement");
            Console.WriteLine(texte[i..Math.Min(texte.Length, i + 620)]);

            try { Directory.Delete(racine, true); } catch { }
        }

        // ── 6. Feuille parents : dépouillement et aller-retour ────────────────
        Console.WriteLine();
        Console.WriteLine("── feuille parents (22 items) ──");
        {
            var vm = new MedCompanion.ViewModels.EnvSaisieViewModel(null);

            Verifie("4 blocs", vm.Blocs.Count == 4, $"{vm.Blocs.Count}");
            Verifie("22 items au total", vm.NbItems == CartographieEnvironnementV2.NbItemsParent,
                $"{vm.NbItems}");
            Verifie("tailles inégales 5/6/9/2",
                string.Join("/", vm.Blocs.Select(b => b.Items.Count)) == "5/6/9/2",
                string.Join("/", vm.Blocs.Select(b => b.Items.Count)));
            Verifie("chaque bloc annonce ce que le médecin cote",
                vm.Blocs.All(b => b.AttenteMedecinText.Contains("coté")),
                vm.Blocs[0].AttenteMedecinText);

            // Lecture automatique simulée sur deux blocs.
            var lecture = new Dictionary<string, ReponseItem[]>
            {
                ["famille"] = new[] { ReponseItem.Oui, ReponseItem.Non, ReponseItem.NonRepondu, ReponseItem.Oui,
                                      ReponseItem.NonRepondu },
                ["ecrans"]  = new[] { ReponseItem.Non, ReponseItem.Oui, ReponseItem.NonRepondu, ReponseItem.Oui,
                                      ReponseItem.Oui, ReponseItem.NonRepondu, ReponseItem.Non, ReponseItem.Oui,
                                      ReponseItem.Oui }
            };

            // Le médecin a déjà corrigé une ligne à la main : la lecture ne doit PAS la reprendre.
            vm.Blocs[0].Items[1].Reponse = ReponseItem.Oui;
            vm.Prefill(lecture);
            Verifie("la lecture ne réécrit pas une ligne déjà saisie",
                vm.Blocs[0].Items[1].EstOui, $"{vm.Blocs[0].Items[1].Reponse}");
            Verifie("les lignes vides sont pré-remplies",
                vm.Blocs[0].Items[0].EstOui && vm.Blocs[0].Items[3].EstOui);
            Verifie("un doute du modèle reste vide", vm.Blocs[0].Items[2].EstVide);

            vm.PrefillInformateur("mere", "Sophie, mère");
            Verifie("informateur pré-rempli", vm.EstMere && vm.InformateurNom == "Sophie, mère");

            var reps = vm.ToReponses();
            Verifie("4 blocs exportés", reps.Count == 4, $"{reps.Count}");
            Verifie("longueur du bloc écrans conservée", reps["ecrans"].Length == 9, $"{reps["ecrans"].Length}");
            Verifie("un non répondu s'exporte vide", reps["famille"][2] == "", $"'{reps["famille"][2]}'");

            var racine = Path.Combine(Path.GetTempPath(), "medtest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(racine);
            var svcFiche = new SeanceEnvironnementService();

            var fiche = new SeanceEnvironnement
            {
                PatientNom = "GOBLET Adrien", Age = 8, Date = new DateTime(2026, 9, 2),
                HypothesesPrincipales = new() { "TDAH presentation inattentive" },
                ReponsesParent = reps, ReponsesParentDate = DateTime.Now,
                InformateurEnv = vm.Informateur, InformateurEnvNom = vm.InformateurNom,
                ScanEnvImage = @"C:\scans\feuille_env.pdf"
            };

            var (okS, path, errS) = svcFiche.Save(racine, fiche);
            Verifie("écriture", okS, errS);

            var relu = svcFiche.Load(path!)!;
            Verifie("4 blocs relus", relu.ReponsesParent.Count == 4, $"{relu.ReponsesParent.Count}");
            Verifie("longueurs conservées",
                relu.ReponsesParent["famille"].Length == 5 && relu.ReponsesParent["ecrans"].Length == 9,
                $"{relu.ReponsesParent["famille"].Length}/{relu.ReponsesParent["ecrans"].Length}");
            Verifie("OUI relu", relu.ReponsesParent["famille"][0] == "oui");
            Verifie("NON relu", relu.ReponsesParent["ecrans"][0] == "non");
            Verifie("non répondu reste vide, PAS un non",
                relu.ReponsesParent["famille"][2] == "", $"'{relu.ReponsesParent["famille"][2]}'");
            Verifie("informateur relu",
                relu.InformateurEnv == "mere" && relu.InformateurEnvNom == "Sophie, mère",
                $"{relu.InformateurEnv} / {relu.InformateurEnvNom}");
            Verifie("chemin du scan retenu", relu.ScanEnvImage == @"C:\scans\feuille_env.pdf", relu.ScanEnvImage);
            Verifie("état lisible", relu.EtatFeuilleParentLisible.Contains("réponses dépouillées"),
                relu.EtatFeuilleParentLisible);

            // Rechargement dans un dépouillement neuf : les réponses reviennent au bon endroit.
            var vm2 = new MedCompanion.ViewModels.EnvSaisieViewModel(null, relu.ReponsesParent);
            Verifie("rechargement fidèle",
                vm2.Blocs[0].Items[0].EstOui && vm2.Blocs[0].Items[2].EstVide
                && vm2.Blocs[2].Items[0].EstNon);
            Verifie("compteur rechargé", vm2.NbRepondus == vm.NbRepondus, vm2.AvancementText);

            var texte = File.ReadAllText(path!);
            Verifie("la fiche nomme l'informateur", texte.Contains("Remplie par la mère (Sophie, mère)"));
            Verifie("la fiche dit ce qu'une case vide signifie",
                texte.Contains("« non répondu » — jamais « non »"));

            Console.WriteLine();
            Console.WriteLine("── extrait de la fiche ──");
            var i = texte.IndexOf("## Cartographie de l'environnement — feuille parents");
            Console.WriteLine(texte[i..Math.Min(texte.Length, i + 640)]);

            try { Directory.Delete(racine, true); } catch { }
        }

        // ── 7. Aiguillage des deux feuilles ───────────────────────────────────
        // « formulaire_cartoenv » CONTIENT « formulaire_carto » : c'est ce qui faisait ouvrir la
        // feuille de l'environnement avec le dépouillement de la feuille de l'enfant.
        Console.WriteLine();
        Console.WriteLine("── aiguillage enfant / environnement ──");
        {
            static MedCompanion.Models.PatientDocumentItem Doc(string nom)
                => new() { FileName = nom, Category = "Formulaires" };

            var enfant = Doc("2026-09-02_formulaire_carto_rempli.pdf");
            var env    = Doc("2026-09-02_formulaire_cartoenv_rempli.pdf");

            Verifie("feuille enfant → dépouillement enfant",
                enfant.IsQuestionnaireCartographie && !enfant.IsQuestionnaireEnvironnement);
            Verifie("feuille environnement → dépouillement environnement",
                env.IsQuestionnaireEnvironnement && !env.IsQuestionnaireCartographie);
            Verifie("les deux restent des formulaires", enfant.IsFormulaire && env.IsFormulaire);

            // Repli sur le nom, pour les feuilles importées avant que l'identifiant soit posé.
            var vieilEnfant = Doc("scan_cartographie_de_l_enfant.pdf");
            var vieilEnv    = Doc("scan_cartographie_de_l_environnement.pdf");
            Verifie("ancienne feuille enfant reconnue",
                vieilEnfant.IsQuestionnaireCartographie && !vieilEnfant.IsQuestionnaireEnvironnement);
            Verifie("ancienne feuille environnement non prise pour l'enfant",
                vieilEnv.IsQuestionnaireEnvironnement && !vieilEnv.IsQuestionnaireCartographie);

            // Reconnaissance du jeton : l'exact doit battre l'approché d'une AUTRE définition.
            static string Norm(string s) => MedCompanion.Models.FormulairesConnus.NormaliserPourComparaison(s);

            var (defEnv, vEnv) = MedCompanion.Models.FormulairesConnus.Reconnaitre(
                Norm("MedCompanion CARTOGRAPHIE DE L'ENVIRONNEMENT MEDCOMP-FORM-CARTOENV-V1"));
            Verifie("jeton CARTOENV reconnu comme CARTOENV",
                defEnv?.Id == "CARTOENV" && vEnv == 1, $"{defEnv?.Id} v{vEnv}");

            var (defEnf, vEnf) = MedCompanion.Models.FormulairesConnus.Reconnaitre(
                Norm("MedCompanion CARTOGRAPHIE DE L'ENFANT MEDCOMP-FORM-CARTO-V1"));
            Verifie("jeton CARTO reconnu comme CARTO",
                defEnf?.Id == "CARTO" && vEnf == 1, $"{defEnf?.Id} v{vEnf}");
        }

        // ── 8. Carte de coordonnées du gabarit environnement ──────────────────
        // Les blocs de cette feuille sont posés en C# à la place de {{blocs}} : mesurer le gabarit
        // BRUT ne rapportait que le bandeau informateur, d'où « Aucun bloc n'a pu être lu ».
        Console.WriteLine();
        Console.WriteLine("── carte de coordonnées (gabarit environnement) ──");
        {
            var gabaritBrut = await File.ReadAllTextAsync(QuestionnaireEnvironnementService.TemplatePath,
                                                          System.Text.Encoding.UTF8);
            Verifie("le gabarit brut ne porte AUCUN bloc (c'était la cause)",
                !gabaritBrut.Contains("data-bloc=\"famille\""));

            var mesurable = await QuestionnaireEnvironnementService.ConstruireGabaritMesurableAsync();
            Verifie("gabarit mesurable construit", mesurable != null);
            Verifie("les 4 blocs y sont posés",
                mesurable!.Contains("data-bloc=\"famille\"") && mesurable.Contains("data-bloc=\"ecole_pairs\"")
                && mesurable.Contains("data-bloc=\"ecrans\"") && mesurable.Contains("data-bloc=\"cadre_reperes\""));
            Verifie("plus aucun jeton {{...}} non résolu",
                !System.Text.RegularExpressions.Regex.IsMatch(mesurable, @"\{\{\s*[a-zA-Z0-9_]+\s*\}\}"));
            Verifie("le bandeau informateur est conservé", mesurable.Contains("data-zone=\"informateur\""));

            // Mesure réelle par Edge, si disponible sur ce poste.
            var pdf = new EdgeHeadlessPdfService();
            if (!pdf.IsAvailable)
                Console.WriteLine("  (Edge indisponible — mesure réelle non exécutée)");
            else
            {
                var tmp = Path.Combine(Path.GetTempPath(), "test_cartoenv_coordmap.html");
                await File.WriteAllTextAsync(tmp, mesurable, System.Text.Encoding.UTF8);
                var (okMap, jsonMap, errMap) = await pdf.ExtractCoordMapAsync(tmp);
                Verifie("carte de coordonnées extraite", okMap, errMap);

                if (okMap)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonMap);
                    var mesures = new Dictionary<string, (double y, double h, int nb)>();
                    foreach (var f in doc.RootElement.GetProperty("fields").EnumerateArray())
                    {
                        var type = f.TryGetProperty("type", out var t) ? t.GetString() : null;
                        if (type != "bloc" && type != "zone") continue;
                        var r = f.GetProperty("rect");
                        mesures[f.GetProperty("name").GetString() ?? ""] = (
                            r.GetProperty("y").GetDouble(), r.GetProperty("h").GetDouble(),
                            f.TryGetProperty("nb", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.Number
                                ? n.GetInt32() : 0);
                    }

                    Verifie("bandeau informateur mesuré", mesures.ContainsKey("zone_informateur"));
                    foreach (var feuille in CartographieEnvironnementV2.Feuilles)
                    {
                        var attendu = feuille.ItemsParent.Count();
                        var cle = $"bloc_{feuille.Key}";
                        var present = mesures.TryGetValue(cle, out var m);
                        Verifie($"{feuille.Label} : bloc mesuré, {attendu} lignes",
                            present && m.nb == attendu,
                            present ? $"nb={m.nb}  y={m.y:0.0}→{m.y + m.h:0.0} mm" : "ABSENT");
                    }

                    var ordonnees = CartographieEnvironnementV2.Feuilles
                        .Select(f => mesures.TryGetValue($"bloc_{f.Key}", out var m) ? m.y : -1).ToList();
                    Verifie("blocs dans l'ordre, sans chevauchement",
                        ordonnees.All(y => y > 0) && ordonnees.SequenceEqual(ordonnees.OrderBy(y => y)),
                        string.Join(" · ", ordonnees.Select(y => $"{y:0.0}")));
                }
                try { File.Delete(tmp); } catch { }
            }
        }

        // ── 9. Réunion des deux moitiés et couleurs ───────────────────────────
        Console.WriteLine();
        Console.WriteLine("── réunion des deux moitiés ──");
        {
            var famille = CartographieEnvironnementV2.Par("famille")!;

            // Rien du tout : aucune nervure lisible, aucune couleur.
            var vides = LectureEnvironnementV2.Construire(null, null);
            Verifie("4 feuilles construites", vides.Count == 4, $"{vides.Count}");
            Verifie("aucune nervure lisible sans réponses",
                vides.All(f => f.NbNervuresLisibles == 0) && vides.All(f => !f.EstLisible));
            Verifie("gris quand on ne sait pas",
                vides.All(f => f.Couleur == LectureEnvironnementV2.GrisIndetermine
                            && f.Nervures.All(n => n.Couleur == LectureEnvironnementV2.GrisIndetermine)));
            Verifie("36 items au total", vides.Sum(f => f.NbTotal) == CartographieEnvironnementV2.NbItems,
                $"{vides.Sum(f => f.NbTotal)}");

            // Famille complète : 5 réponses parent + ses 5 items médecin.
            var repsP = new Dictionary<string, string[]>
            {
                ["famille"] = new[] { "oui", "oui", "oui", "oui", "non" }
            };
            var cotM = famille.ItemsMedecin.ToDictionary(i => i.Texte, _ => ReponseProposition.Oui);

            var lues = LectureEnvironnementV2.Construire(cotM, repsP);
            var fam  = lues.First(f => f.Key == "famille");

            Verifie("famille entièrement lisible", fam.EstLisible, fam.EtatText);
            Verifie("9 favorables sur 10", fam.NbOui == 9 && fam.NbTotal == 10, $"{fam.NbOui}/{fam.NbTotal}");
            Verifie("famille colorée, plus grise", fam.Couleur != LectureEnvironnementV2.GrisIndetermine,
                $"{fam.Couleur} — {fam.EtatText}");
            Verifie("les autres feuilles restent grises",
                lues.Where(f => f.Key != "famille")
                    .All(f => f.Couleur == LectureEnvironnementV2.GrisIndetermine));

            // Les deux sources doivent être distinguées ligne par ligne.
            var toutesLignes = fam.Nervures.SelectMany(n => n.Lignes).ToList();
            Verifie("5 lignes parent / 5 lignes entretien",
                toutesLignes.Count(l => l.Source == SourceItemEnv.Parent) == 5
                && toutesLignes.Count(l => l.Source == SourceItemEnv.Medecin) == 5);

            // UNE seule réponse retirée doit suffire à faire retomber la nervure au gris.
            var repsTrouees = new Dictionary<string, string[]>
            {
                ["famille"] = new[] { "oui", "oui", "oui", "oui", "" }   // la 5e manque
            };
            var trouee = LectureEnvironnementV2.Construire(cotM, repsTrouees).First(f => f.Key == "famille");
            Verifie("une réponse manquante retire la couleur de la feuille",
                !trouee.EstLisible && trouee.Couleur == LectureEnvironnementV2.GrisIndetermine,
                trouee.EtatText);
            Verifie("les nervures complètes gardent la leur",
                trouee.Nervures.Count(n => n.EstComplete) == 2
                && trouee.Nervures.Count(n => !n.EstComplete) == 1,
                string.Join(" · ", trouee.Nervures.Select(n => $"{n.Label}={n.EtatText}")));

            // Le prompt doit DIRE ce qui n'est pas lisible, pas l'omettre.
            var prompt = LectureEnvironnementV2.PourPrompt(trouee is null ? lues : new List<FeuilleLue> { trouee });
            Verifie("le prompt nomme les nervures non lisibles", prompt.Contains("NON LISIBLE"));
            Verifie("le prompt distingue les deux sources",
                prompt.Contains("(feuille parents)") && prompt.Contains("(entretien)"));
        }

        // ── 10. Synthèse pondérée ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("── synthèse pondérée ──");
        {
            var lues = LectureEnvironnementV2.Construire(
                CartographieEnvironnementV2.Par("famille")!.ItemsMedecin.ToDictionary(i => i.Texte, _ => ReponseProposition.Oui),
                new Dictionary<string, string[]> { ["famille"] = new[] { "oui", "oui", "oui", "oui", "non" } });

            var axe = new AxeCible { Intitule = "Attention soutenue", Rattachement = "TDAH inattentif" };
            axe.Ajouter(new PropositionObservable { Texte = "Se retourne quand on entre", Reponse = ReponseProposition.Oui });
            axe.Remarques = "décroche après 10 min";

            var entree = new SyntheseSeance3Service.Entree
            {
                PatientNom = "GOBLET Adrien", Age = 8,
                Environnement = lues, Axes = new List<AxeCible> { axe },
                FiabiliteEnv = "moyenne", FiabiliteAxes = "fiable",
                Informateur = "mere", InformateurNom = "Sophie"
            };

            var moteur = new FauxMoteur { Reponse = i => (true, $"Paragraphe {i + 1}.") };
            var svc = new SyntheseSeance3Service(moteur);
            var progres = new List<string>();
            var (ok, texte, err) = await svc.RedigerAsync(entree, m => progres.Add(m));

            Verifie("rédaction réussie", ok, err);
            Verifie("3 appels : env, axes, mise en regard", moteur.Prompts.Count == 3, $"{moteur.Prompts.Count}");
            Verifie("avancement en 3 temps", progres.Count == 3 && progres[2].Contains("3/3"),
                string.Join(" | ", progres));
            Verifie("les deux fiabilités sont dans le prompt",
                moteur.Prompts[0].Contains("Moyennement fiable") && moteur.Prompts[0].Contains("Fiable"));
            Verifie("la consigne de ne pas conclure est posée",
                moteur.Prompts[0].Contains("tu ne conclus pas"));
            Verifie("les remarques du médecin sont transmises",
                moteur.Prompts[1].Contains("décroche après 10 min"));
            Verifie("la mise en regard reçoit les deux textes",
                moteur.Prompts[2].Contains("Paragraphe 1.") && moteur.Prompts[2].Contains("Paragraphe 2."));
            Verifie("les fiabilités sont écrites EN TÊTE du texte",
                texte!.StartsWith("_Environnement : moyennement fiable. Évaluation ciblée : fiable._"),
                texte.Split('\n')[0]);
            Verifie("les trois sections sont titrées",
                texte.Contains("### Cartographie de l'environnement")
                && texte.Contains("### Évaluation ciblée") && texte.Contains("### Mise en regard"));

            // Source écartée : elle sort du texte, et son absence est dite.
            var moteur2 = new FauxMoteur { Reponse = i => (true, $"P{i + 1}.") };
            var entree2 = new SyntheseSeance3Service.Entree
            {
                PatientNom = "X", Age = 8, Environnement = lues, Axes = new List<AxeCible> { axe },
                FiabiliteEnv = "non_exploitable", FiabiliteAxes = "fiable"
            };
            var (ok2, texte2, _) = await new SyntheseSeance3Service(moteur2).RedigerAsync(entree2);

            Verifie("une source écartée n'est pas rédigée", ok2 && moteur2.Prompts.Count == 1,
                $"{moteur2.Prompts.Count} appel(s)");
            Verifie("son écartement est DIT, pas tu",
                texte2!.Contains("écartée de cette synthèse"), texte2.Split('\n')[0]);
            Verifie("pas de mise en regard avec un seul bloc",
                !texte2.Contains("### Mise en regard"));

            // Deux sources écartées : refus explicite plutôt qu'un texte creux.
            var moteur3 = new FauxMoteur { Reponse = _ => (true, "P.") };
            var (ok3, _, err3) = await new SyntheseSeance3Service(moteur3).RedigerAsync(
                new SyntheseSeance3Service.Entree
                {
                    Environnement = lues, Axes = new List<AxeCible> { axe },
                    FiabiliteEnv = "non_exploitable", FiabiliteAxes = "non_exploitable"
                });
            Verifie("tout écarté → refus, sans dépenser d'appel",
                !ok3 && moteur3.Prompts.Count == 0, err3);

            // Aller-retour dans la fiche.
            var racine = Path.Combine(Path.GetTempPath(), "medtest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(racine);
            var svcF = new SeanceEnvironnementService();
            var fiche = new SeanceEnvironnement
            {
                PatientNom = "GOBLET Adrien", Date = new DateTime(2026, 9, 2),
                FiabiliteEnv = "moyenne", FiabiliteAxes = "fiable",
                SyntheseTexte = texte, SyntheseDate = DateTime.Now
            };
            var (okS, path, errS) = svcF.Save(racine, fiche);
            Verifie("écriture", okS, errS);

            var relu = svcF.Load(path!)!;
            Verifie("fiabilités relues",
                relu.FiabiliteEnv == "moyenne" && relu.FiabiliteAxes == "fiable",
                $"{relu.FiabiliteEnv} / {relu.FiabiliteAxes}");
            // Comparaison à fins de ligne normalisées : le lecteur ramène CRLF à LF, comme partout
            // ailleurs dans la fiche. Ce n'est pas une perte, c'est la forme canonique.
            static string Lignes(string? s) => (s ?? "").Replace("\r\n", "\n");
            Verifie("texte relu à l'identique", Lignes(relu.SyntheseTexte) == Lignes(texte).Trim(),
                $"{relu.SyntheseTexte?.Length} car. vs {Lignes(texte).Trim().Length}");
            Verifie("état lisible", relu.EtatSyntheseLisible.StartsWith("synthèse du"),
                relu.EtatSyntheseLisible);

            try { Directory.Delete(racine, true); } catch { }
        }

        // ── 11. Les trois entrées du dossier bleu ─────────────────────────────
        Console.WriteLine();
        Console.WriteLine("── dossier bleu : BILANS et SYNTHÈSE ──");
        {
            var famille = CartographieEnvironnementV2.Par("famille")!;

            var axe = new AxeCible { Intitule = "Attention soutenue", Rattachement = "TDAH inattentif" };
            axe.Ajouter(new PropositionObservable { Texte = "Se retourne quand on entre", Reponse = ReponseProposition.Oui });
            axe.Ajouter(new PropositionObservable { Texte = "Soutient une tache 10 min", Reponse = ReponseProposition.Non });
            axe.Ajouter(new PropositionObservable { Texte = "Perd le fil de sa phrase" });   // non observé
            axe.Remarques = "décroche après 10 min, se remobilise seul";

            var fiche = new SeanceEnvironnement
            {
                PatientNom = "GOBLET Adrien", Age = 8, Date = new DateTime(2026, 9, 2),
                CotationsEnv = famille.ItemsMedecin.ToDictionary(i => i.Texte, _ => ReponseProposition.Oui),
                ReponsesParent = new Dictionary<string, string[]>
                {
                    ["famille"] = new[] { "oui", "oui", "non", "oui", "" }   // la 5e manque
                },
                InformateurEnv = "mere", InformateurEnvNom = "Sophie",
                Axes = new List<AxeCible> { axe },
                FiabiliteEnv = "moyenne", FiabiliteAxes = "fiable",
                SyntheseTexte = "Un texte de synthèse.", SyntheseDate = DateTime.Now
            };

            // ── Carte BILANS : cartographie de l'environnement
            var carte = new MedCompanion.ViewModels.SeanceEnvCardViewModel(fiche);
            Verifie("titre daté et âgé",
                carte.TitreCard == "Cartographie de l'environnement — 02/09/2026 (8 ans)", carte.TitreCard);
            Verifie("l'incomplétude est écrite, pas masquée",
                !carte.EstComplete && carte.EtatLigne.Contains("manquante"), carte.EtatLigne);
            Verifie("informateur nommé", carte.InformateurLigne == "Rempli par : Mère · Sophie",
                carte.InformateurLigne);
            Verifie("4 feuilles", carte.Feuilles.Count == 4, $"{carte.Feuilles.Count}");

            var fam = carte.Feuilles[0];
            Verifie("famille reste grise (une réponse manque)",
                fam.Couleur == LectureEnvironnementV2.GrisIndetermine, fam.Couleur);
            Verifie("ses nervures complètes sont colorées",
                fam.Nervures.Count(n => n.Couleur != LectureEnvironnementV2.GrisIndetermine) == 2,
                string.Join(" · ", fam.Nervures.Select(n => $"{n.Label}={n.EtatText}")));

            var toutes = carte.Feuilles.SelectMany(f => f.Nervures).SelectMany(n => n.Reponses).ToList();
            Verifie("36 réponses au total", toutes.Count == CartographieEnvironnementV2.NbItems, $"{toutes.Count}");
            Verifie("les trois marques existent",
                toutes.Any(r => r.Marque == "✓") && toutes.Any(r => r.Marque == "✗") && toutes.Any(r => r.Marque == "—"));
            Verifie("chaque réponse dit d'où elle vient",
                toutes.All(r => r.Source is "feuille parents" or "entretien"));
            Verifie("replié par défaut", fam.Nervures.All(n => !n.IsExpanded && n.Chevron == "▸"));
            fam.Nervures[0].ToggleCommand.Execute(null);
            Verifie("le clic déplie", fam.Nervures[0].IsExpanded && fam.Nervures[0].Chevron == "▾");

            // ── Carte BILANS : évaluation ciblée, séparée
            var cible = new MedCompanion.ViewModels.EvaluationCibleeCardViewModel(fiche);
            Verifie("carte séparée, titrée", cible.TitreCard == "Évaluation ciblée — 02/09/2026", cible.TitreCard);
            Verifie("état : constats renseignés", cible.EtatLigne == "1 axe · 2/3 constats renseignés",
                cible.EtatLigne);
            Verifie("un bloc par axe", cible.Axes.Count == 1, $"{cible.Axes.Count}");
            Verifie("l'axe dit ce qu'il sert",
                cible.Axes[0].SousLabel == "sert à trancher : TDAH inattentif", cible.Axes[0].SousLabel);
            Verifie("les 3 constats sont là avec leurs marques",
                cible.Axes[0].Reponses.Count == 3
                && cible.Axes[0].Reponses[0].Marque == "✓"
                && cible.Axes[0].Reponses[1].Marque == "✗"
                && cible.Axes[0].Reponses[2].Marque == "—",
                string.Join("", cible.Axes[0].Reponses.Select(r => r.Marque)));
            Verifie("les remarques du médecin sont conservées",
                cible.Axes[0].HasRemarques && cible.Axes[0].Remarques.Contains("se remobilise seul"));

            // ── Bloc SYNTHÈSE
            var bloc = new MedCompanion.ViewModels.SeanceEnvSyntheseBlocViewModel(fiche);
            Verifie("titre du bloc synthèse",
                bloc.Titre == "Environnement & évaluation ciblée — 02/09/2026", bloc.Titre);
            Verifie("texte repris", bloc.Texte == "Un texte de synthèse.", bloc.Texte);
            Verifie("les DEUX fiabilités voyagent avec le texte",
                bloc.Qualification == "Environnement : moyennement fiable · Évaluation ciblée : fiable",
                bloc.Qualification);

            // Une fiche vide ne doit rien produire de trompeur.
            var vide = new SeanceEnvironnement { Date = new DateTime(2026, 9, 2) };
            Verifie("fiche vide : rien à verser",
                !vide.HasReponsesParent && !vide.HasCotationEnv && !vide.HasEvaluation && !vide.HasSynthese);
        }

        // ── 12. Garde-fou de clôture ──────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("── garde-fou : ce qui manque avant de terminer ──");
        {
            var famille = CartographieEnvironnementV2.Par("famille")!;

            // Séance entièrement vide : les cinq parties sont nommées.
            var vide = new SeanceEnvironnement { Date = new DateTime(2026, 9, 2) };
            var mVide = vide.PartiesManquantes();
            Verifie("une séance vide nomme les 5 parties", mVide.Count == 5, $"{mVide.Count}");
            Verifie("chaque partie est nommée en clair",
                mVide.Any(x => x.StartsWith("Orientation"))
                && mVide.Any(x => x.StartsWith("Évaluation ciblée"))
                && mVide.Any(x => x.StartsWith("Cartographie"))
                && mVide.Any(x => x.StartsWith("Feuille parents"))
                && mVide.Any(x => x.StartsWith("Synthèse")));
            Verifie("feuille non revenue, pas « non dépouillée »",
                mVide.Any(x => x.Contains("non revenue de la salle d'attente")),
                mVide.First(x => x.StartsWith("Feuille parents")));

            // Feuille scannée mais pas dépouillée : le message change.
            var scannee = new SeanceEnvironnement { ScanEnvImage = @"C:\scan.pdf" };
            Verifie("scannée non dépouillée est distingué",
                scannee.PartiesManquantes().Any(x => x.Contains("scannée mais non dépouillée")));

            // Séance complète : rien à signaler.
            var axe = new AxeCible { Intitule = "A", Rattachement = "h" };
            axe.Ajouter(new PropositionObservable { Texte = "c1", Reponse = ReponseProposition.Oui });
            axe.Ajouter(new PropositionObservable { Texte = "c2", Reponse = ReponseProposition.Non });

            var pleine = new SeanceEnvironnement
            {
                PatientNom = "GOBLET Adrien", Date = new DateTime(2026, 9, 2),
                HypothesesPrincipales = new() { "TDAH" },
                Axes = new List<AxeCible> { axe },
                CotationsEnv = CartographieEnvironnementV2.Feuilles
                    .SelectMany(f => f.ItemsMedecin)
                    .ToDictionary(i => i.Texte, _ => ReponseProposition.Oui),
                ScanEnvImage = @"C:\scan.pdf",
                ReponsesParent = CartographieEnvironnementV2.Feuilles
                    .Where(f => f.ItemsParent.Any())
                    .ToDictionary(f => f.Key, f => f.ItemsParent.Select(_ => "oui").ToArray()),
                SyntheseTexte = "texte", SyntheseDate = DateTime.Now
            };
            Verifie("séance complète : rien à signaler", pleine.PartiesManquantes().Count == 0,
                string.Join(" · ", pleine.PartiesManquantes()));

            // Partiel : le compte exact est donné, pas un « incomplet » vague.
            var partielle = new SeanceEnvironnement
            {
                HypothesesPrincipales = new() { "TDAH" },
                Axes = new List<AxeCible> { axe },
                CotationsEnv = famille.ItemsMedecin.ToDictionary(i => i.Texte, _ => ReponseProposition.Oui),
                ScanEnvImage = @"C:\scan.pdf",
                ReponsesParent = new Dictionary<string, string[]> { ["famille"] = new[] { "oui", "oui", "", "", "" } },
                SyntheseTexte = "texte"
            };
            var mp = partielle.PartiesManquantes();
            Verifie("le compte exact est donné pour vos items",
                mp.Any(x => x.Contains($"{CartographieEnvironnementV2.NbItemsMedecin - 5} de vos items non cotés")),
                string.Join(" · ", mp));
            Verifie("le compte exact est donné pour la feuille parents",
                mp.Any(x => x.Contains($"{CartographieEnvironnementV2.NbItemsParent - 2} réponse(s) manquante(s)")),
                string.Join(" · ", mp));
            Verifie("la synthèse rédigée n'est plus signalée", !mp.Any(x => x.StartsWith("Synthèse")));

            // Clôture : irréversible, et l'écriture qui la pose passe.
            var racine = Path.Combine(Path.GetTempPath(), "medtest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(racine);
            var svcF = new SeanceEnvironnementService();

            var (ok1, path, _) = svcF.Save(racine, pleine);
            Verifie("enregistrement avant clôture", ok1);

            pleine.DateCloture = DateTime.Now;
            var (ok2, _, err2) = svcF.Save(racine, pleine);
            Verifie("l'écriture qui POSE la clôture passe", ok2, err2);

            var relu = svcF.Load(path!)!;
            Verifie("la fiche est relue close", relu.EstCloturee);

            relu.SyntheseTexte = "modifié après coup";
            var (ok3, _, err3) = svcF.Save(racine, relu);
            Verifie("plus aucune écriture après clôture", !ok3, err3);

            try { Directory.Delete(racine, true); } catch { }
        }

        // ── 13. Ce que l'aval voit des deux séances ───────────────────────────
        Console.WriteLine();
        Console.WriteLine("── contexte V2 transmis aux moteurs d'aval ──");
        {
            var racine = Path.Combine(Path.GetTempPath(), "medtest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(racine);

            var svcCtx = new EvaluationV2ContextService();

            Verifie("dossier vide : rien à transmettre", !svcCtx.ADuContenu(racine),
                $"'{svcCtx.PourPrompt(racine)}'");

            // Une séance 3 renseignée.
            var famille = CartographieEnvironnementV2.Par("famille")!;
            var axe = new AxeCible { Intitule = "Attention soutenue", Rattachement = "TDAH inattentif" };
            axe.Ajouter(new PropositionObservable { Texte = "Se retourne quand on entre", Reponse = ReponseProposition.Oui });
            axe.Ajouter(new PropositionObservable { Texte = "Soutient une tache 10 min", Reponse = ReponseProposition.Non });
            axe.Ajouter(new PropositionObservable { Texte = "Perd le fil de sa phrase" });   // non observé
            axe.Remarques = "décroche après 10 min";

            var fiche = new SeanceEnvironnement
            {
                PatientNom = "GOBLET Adrien", Age = 8, Date = new DateTime(2026, 9, 2),
                HypothesesPrincipales = new() { "TDAH presentation inattentive" },
                Axes = new List<AxeCible> { axe },
                CotationsEnv = famille.ItemsMedecin.ToDictionary(i => i.Texte, _ => ReponseProposition.Oui),
                ReponsesParent = new Dictionary<string, string[]> { ["famille"] = new[] { "oui", "oui", "oui", "oui", "non" } },
                FiabiliteEnv = "moyenne", FiabiliteAxes = "fiable",
                SyntheseTexte = "Le socle familial tient.", SyntheseDate = DateTime.Now,
                DateCloture = DateTime.Now
            };
            new SeanceEnvironnementService().Save(racine, fiche);

            var ctx = svcCtx.PourPrompt(racine);
            Verifie("la séance 3 est transmise", svcCtx.ADuContenu(racine));
            Verifie("les DEUX fiabilités sont transmises",
                ctx.Contains("environnement : Moyennement fiable") && ctx.Contains("évaluation ciblée : Fiable"),
                ctx.Split('\n').FirstOrDefault(l => l.Contains("Fiabilité"))?.Trim());
            Verifie("la synthèse du médecin est transmise", ctx.Contains("Le socle familial tient."));
            Verifie("l'orientation est étiquetée « PAS un diagnostic »",
                ctx.Contains("PAS un diagnostic"));
            Verifie("une feuille lisible est chiffrée", ctx.Contains("Famille : 9/10 favorables"),
                ctx.Split('\n').FirstOrDefault(l => l.Contains("- Famille"))?.Trim());
            Verifie("une feuille non lisible est DITE, pas omise",
                ctx.Contains("École & Pairs : NON LISIBLE"));
            Verifie("l'avertissement sur les non lisibles est posé",
                ctx.Contains("ne pas l'interpréter"));
            Verifie("les constats cochés sont transmis",
                ctx.Contains("[oui] Se retourne quand on entre") && ctx.Contains("[NON] Soutient une tache 10 min"));
            Verifie("un constat NON OBSERVÉ n'est pas transmis comme un non",
                !ctx.Contains("Perd le fil de sa phrase"));
            Verifie("l'avertissement sur les cases vides est posé",
                ctx.Contains("NON OBSERVÉ — jamais"));
            Verifie("la remarque du médecin est transmise", ctx.Contains("décroche après 10 min"));
            Verifie("les 156 items ne sont PAS déversés", ctx.Length < 3000, $"{ctx.Length} caractères");

            Console.WriteLine();
            Console.WriteLine("── extrait transmis au moteur ──");
            Console.WriteLine(ctx[..Math.Min(ctx.Length, 900)]);

            try { Directory.Delete(racine, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine(echecs == 0 ? "=== SÉANCE 3 OK ===" : $"=== {echecs} ÉCHEC(S) ===");
        return echecs == 0 ? 0 : 1;
    }
}

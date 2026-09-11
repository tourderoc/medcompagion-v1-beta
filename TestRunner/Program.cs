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
    // ChatAsync enregistre aussi : c'est le chemin qu'emprunte le suggester de restitution.
    // Sans ça, une génération réelle passait pour « aucun appel » et renvoyait du vide.
    public Task<(bool success, string result, string? error)> ChatAsync(string s, List<(string role, string content)> m,
        int mt = 1500, CancellationToken ct = default, string? fm = null, int? nc = null)
    {
        Prompts.Add(string.Join("\n", m.Select(x => x.content)));
        var (ok, json) = Reponse!(Prompts.Count - 1);
        return Task.FromResult(ok ? (true, json, (string?)null) : (false, "", (string?)"panne simulée"));
    }
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

        // ── 14. Année scolaire et bascule de rentrée ──────────────────────────
        Console.WriteLine();
        Console.WriteLine("── rentrée scolaire ──");
        {
            static DateTime D(int a, int m, int j) => new(a, m, j);

            Verifie("septembre bascule sur la nouvelle année",
                MedCompanion.Models.Scolarite.AnneeScolaireDe(D(2026, 9, 1)) == "2026-2027",
                MedCompanion.Models.Scolarite.AnneeScolaireDe(D(2026, 9, 1)));
            Verifie("31 août appartient encore à l'année précédente",
                MedCompanion.Models.Scolarite.AnneeScolaireDe(D(2026, 8, 31)) == "2025-2026",
                MedCompanion.Models.Scolarite.AnneeScolaireDe(D(2026, 8, 31)));
            Verifie("janvier appartient à l'année commencée en septembre",
                MedCompanion.Models.Scolarite.AnneeScolaireDe(D(2027, 1, 15)) == "2026-2027",
                MedCompanion.Models.Scolarite.AnneeScolaireDe(D(2027, 1, 15)));
            Verifie("le défaut codé en dur a disparu",
                MedCompanion.Models.Scolarite.AnneeScolaireDe(DateTime.Today) != "2025-2026"
                || DateTime.Today < D(2026, 9, 1),
                MedCompanion.Models.Scolarite.AnneeScolaireDe(DateTime.Today));

            // La question se pose une fois par année scolaire, pas à chaque séance.
            var aujourdhui = D(2026, 9, 3);
            Verifie("jamais confirmée → on demande",
                MedCompanion.Models.Scolarite.DoitConfirmer(null, aujourdhui));
            Verifie("confirmée avant la rentrée → on demande",
                MedCompanion.Models.Scolarite.DoitConfirmer(D(2026, 6, 12), aujourdhui));
            Verifie("confirmée après la rentrée → on ne demande plus",
                !MedCompanion.Models.Scolarite.DoitConfirmer(D(2026, 9, 2), aujourdhui));
            Verifie("confirmée le jour même de la rentrée → on ne demande plus",
                !MedCompanion.Models.Scolarite.DoitConfirmer(D(2026, 9, 1), aujourdhui));
            Verifie("une confirmation de septembre tient jusqu'en août suivant",
                !MedCompanion.Models.Scolarite.DoitConfirmer(D(2026, 9, 2), D(2027, 8, 31)));
            Verifie("et cesse de tenir à la rentrée suivante",
                MedCompanion.Models.Scolarite.DoitConfirmer(D(2026, 9, 2), D(2027, 9, 1)));

            // Un patient créé en cours d'année n'est pas questionné avant la rentrée suivante.
            Verifie("patient créé en octobre : pas de question avant septembre suivant",
                !MedCompanion.Models.Scolarite.DoitConfirmer(D(2026, 10, 5), D(2027, 6, 30))
                && MedCompanion.Models.Scolarite.DoitConfirmer(D(2026, 10, 5), D(2027, 9, 10)));
        }

        // ── 15. Page 2 : la feuille de route attend le projet de soins ────────
        Console.WriteLine();
        Console.WriteLine("── restitution page 2 : feuille de route différée ──");
        {
            var moteur = new FauxMoteur { Reponse = _ => (true, "1. **Étape :** contenu généré.") };
            var svc = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            var lecture = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "GOBLET Adrien",
                PatientJson = "{\"prenom\":\"Adrien\",\"nom\":\"GOBLET\"}",
                PremiereConsultation = "Motif : agitation scolaire."
            };

            var dossier = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();

            // Projet de soins encore vide : la section attend, sans appeler le modèle.
            var enAttente = await svc.SuggestRestitution1PageSectionAsync(
                MedCompanion.Services.Restitutions.RestitutionSuggesterService.IndexFeuilleDeRoute,
                lecture, default, dossier);

            Verifie("projet vide → la feuille de route attend",
                enAttente == MedCompanion.Services.Restitutions.RestitutionSuggesterService.FeuilleDeRouteEnAttente,
                enAttente);
            Verifie("aucun appel au modèle tant que le projet est vide",
                moteur.Prompts.Count == 0, $"{moteur.Prompts.Count}");

            // Le médecin remplit le projet de soins.
            foreach (var b in dossier.Blocs.Where(b => b.Key.StartsWith("pt_")))
                b.ContenuValide = b.Key switch
                {
                    "pt_s1" => "Consultation de suivi dans 3 mois. Pas de traitement pour l'instant.",
                    "pt_s2" => "Les parents prendront rendez-vous chez un psychologue.",
                    _       => ""
                };

            var redigee = await svc.SuggestRestitution1PageSectionAsync(
                MedCompanion.Services.Restitutions.RestitutionSuggesterService.IndexFeuilleDeRoute,
                lecture, default, dossier);

            Verifie("projet rempli → la feuille de route est rédigée",
                redigee.Contains("contenu généré"), redigee);
            Verifie("un seul appel au modèle", moteur.Prompts.Count == 1, $"{moteur.Prompts.Count}");

            var p = moteur.Prompts[0];
            Verifie("elle lit le PROJET DE SOINS, pas le dossier bleu",
                p.Contains("PROJET DE SOINS qui vient d'être décidé")
                && p.Contains("Consultation de suivi dans 3 mois")
                && p.Contains("prendront rendez-vous chez un psychologue"));
            Verifie("les blocs de projet vides ne sont pas transmis", !p.Contains("pt_s3"));
            Verifie("interdiction d'ajouter des étapes", p.Contains("Tu n'ajoutes aucune étape"));
            Verifie("consigne « qui fait quoi »",
                p.Contains("DIS QUI FAIT QUOI") && p.Contains("n'attribue la responsabilité à personne"));
            // Un projet en texte libre — dossier rédigé avant les actions structurées — doit
            // continuer de produire une feuille de route, sans tri puisqu'il n'y a rien à trier.
            Verifie("un projet en texte libre passe par le repli, sans consigne de tri",
                p.Contains("PROJET DE SOINS qui vient d'être décidé") && !p.Contains("GARDE L'ORDRE"));

            // Sans dossier fourni, on n'invente pas non plus.
            var sansDossier = await svc.SuggestRestitution1PageSectionAsync(
                MedCompanion.Services.Restitutions.RestitutionSuggesterService.IndexFeuilleDeRoute,
                lecture);
            Verifie("sans dossier, la section attend au lieu d'inventer",
                sansDossier == MedCompanion.Services.Restitutions.RestitutionSuggesterService.FeuilleDeRouteEnAttente);

            // « Ce qui peut aider » ne doit plus proposer d'orientation.
            var aider = await svc.SuggestRestitution1PageSectionAsync(3, lecture);
            Verifie("« Ce qui peut aider » interdit les orientations",
                moteur.Prompts.Last().Contains("INTERDIT ici : toute orientation"));
        }

        // ── 16. Page 3 — identification, contexte familial, antécédents ──────
        Console.WriteLine();
        Console.WriteLine("── restitution page 3 : identité admin, figures d'attachement, bilans en cours ──");
        {
            var patientJson = """
                {"prenom":"Adrien","nom":"GOBLET","dob":"2018-07-24",
                 "perePrenom":"Julien","pereNom":"GOBLET",
                 "merePrenom":"Sophie","mereNom":"MARTIN",
                 "accompagnantPrenom":"Sophie","accompagnantNom":"MARTIN","accompagnantLien":"Mère",
                 "situationParentale":"Parents séparés, garde alternée"}
                """;

            var lecture = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "GOBLET Adrien",
                PatientJson = patientJson,
                PremiereConsultation = "Reçu en présence de la mère et du beau-père.",
                DatePremierEntretien = new DateTime(2025, 12, 10)
            };

            // ── Identification : déterministe + une seule question au modèle ──
            var moteurIdent = new FauxMoteur { Reponse = _ => (true, "Il s'agit de l'enfant Adrien GOBLET, 8 ans.") };
            var svcIdent = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurIdent, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));

            var identification = await svcIdent.SuggerIdentificationAsync(lecture);

            Verifie("un seul appel au modèle pour l'identification",
                moteurIdent.Prompts.Count == 1, $"{moteurIdent.Prompts.Count}");
            Verifie("l'identité des parents est transmise en clair, pas en JSON brut",
                moteurIdent.Prompts[0].Contains("Père : Julien GOBLET")
                && moteurIdent.Prompts[0].Contains("Mère : Sophie MARTIN"));
            Verifie("la situation parentale est transmise",
                moteurIdent.Prompts[0].Contains("Parents séparés, garde alternée"));
            Verifie("la consigne distingue accompagnant habituel et présent au 1er entretien",
                moteurIdent.Prompts[0].Contains("PAS d'après") && moteurIdent.Prompts[0].Contains("l'accompagnant habituel"));
            Verifie("dates, évaluateur, lieu sont déterministes (pas dans le prompt du modèle)",
                !moteurIdent.Prompts[0].Contains("Dr Lassoued"));
            Verifie("le bloc final porte les 5 champs attendus",
                identification.Contains("**Présentation**") && identification.Contains("**Période d'évaluation**")
                && identification.Contains("**Date de restitution**") && identification.Contains("**Évaluateur** : Dr Lassoued Nair")
                && identification.Contains("**Lieu**"));
            Verifie("la présentation générée est reprise telle quelle",
                identification.Contains("Il s'agit de l'enfant Adrien GOBLET, 8 ans."));

            // Sans formulaire de complétion rempli : rien à transmettre, pas de bloc vide trompeur.
            var lectureSansAdmin = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = lecture.PatientNomComplet,
                PatientJson = """{"prenom":"X","nom":"Y"}""",
                PremiereConsultation = lecture.PremiereConsultation
            };
            var moteurVide = new FauxMoteur { Reponse = _ => (true, "présentation.") };
            var svcVide = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurVide, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcVide.SuggerIdentificationAsync(lectureSansAdmin);
            Verifie("sans identité admin, on dit qu'elle n'est pas renseignée plutôt que de deviner",
                moteurVide.Prompts[0].Contains("identité des parents non renseignée"));

            // ── Contexte familial : ADMIN priorisé, professionnels exclus des figures ──
            var moteurCf = new FauxMoteur { Reponse = _ => (true, "contenu.") };
            var svcCf = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurCf, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));

            var sections = new List<string>();
            await svcCf.SuggestContexteFamilialProgressiveAsync(lecture, s => sections.Add(s));

            Verifie("6 appels séquentiels (récit, père, mère, fratrie, autres figures, points à retenir)",
                moteurCf.Prompts.Count == 6, $"{moteurCf.Prompts.Count}");
            Verifie("l'identité admin est injectée en tête de TOUS les appels, pas seulement Père/Mère",
                moteurCf.Prompts.All(p => p.Contains("IDENTITÉ DES PARENTS")));
            Verifie("Père et Mère priorisent l'ADMIN, avec repli sur les notes seulement si absent",
                moteurCf.Prompts[1].Contains("EN PRIORITÉ du bloc « IDENTITÉ DES PARENTS »")
                && moteurCf.Prompts[1].Contains("SEULEMENT si ce bloc ne mentionne")
                && moteurCf.Prompts[2].Contains("EN PRIORITÉ du bloc « IDENTITÉ DES PARENTS »")
                && moteurCf.Prompts[2].Contains("SEULEMENT si ce bloc ne mentionne"));

            var promptAutresFigures = moteurCf.Prompts[4];
            Verifie("« Autres figures » exclut explicitement les professionnels",
                promptAutresFigures.Contains("EXCLUS, MÊME S'ILS SONT PROCHES DE L'ENFANT")
                && promptAutresFigures.Contains("orthophoniste")
                && promptAutresFigures.Contains("psychomotricien"));
            Verifie("la règle distingue lien affectif et intervention professionnelle",
                promptAutresFigures.Contains("LIEN AFFECTIF DURABLE"));
            Verifie("le doute penche vers l'exclusion, pas l'inclusion",
                promptAutresFigures.Contains("EXCLUS — ne classe comme figure d'attachement"));

            // ── Antécédents : le bilan en cours ne doit pas se citer lui-même ──
            var moteurAt = new FauxMoteur { Reponse = _ => (true, "contenu.") };
            var svcAt = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurAt, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));

            var sectionsAt = new List<string>();
            await svcAt.SuggestAntecedentsProgressiveAsync(lecture, s => sectionsAt.Add(s));

            Verifie("6 appels pour les antécédents", moteurAt.Prompts.Count == 6, $"{moteurAt.Prompts.Count}");

            var promptSuiviResume = moteurAt.Prompts[3];
            var promptBilansResume = moteurAt.Prompts[4];
            Verifie("« Suivi résumé » exclut le suivi du médecin lui-même",
                promptSuiviResume.Contains("N'INCLUS JAMAIS le suivi assuré par vous-même"));
            Verifie("« Suivi résumé » exclut l'évaluation en cours",
                promptSuiviResume.Contains("N'INCLUS PAS non plus l'évaluation en"));
            Verifie("« Bilans résumé » exclut explicitement l'évaluation en cours",
                promptBilansResume.Contains("N'INCLUS JAMAIS l'évaluation EN COURS"));
            Verifie("« Bilans résumé » précise qu'elle sert de base à CE dossier",
                promptBilansResume.Contains("le bilan que tu es en train de restituer"));
            Verifie("« Bilans résumé » exclut aussi le suivi du médecin",
                promptBilansResume.Contains("N'INCLUS PAS non plus votre propre suivi"));
        }

        // ── 17. Le trou transversal : les cartographies atteignent enfin le modèle ──
        Console.WriteLine();
        Console.WriteLine("── restitution : cartographies transmises au modèle (V1 + V2) ──");
        {
            // Une cartographie V1 réelle : 4 des 6 affirmations d'Attachement cochées,
            // tempérament renseigné, le reste vide (comme une fiche en cours d'observation).
            var cartoEnfant = new CartographieEnfant();
            for (int i = 0; i < 4; i++) cartoEnfant.Attachement.Items[i].IsChecked = true;
            cartoEnfant.Temperament.NiveauActivite = 4;
            cartoEnfant.Temperament.Regularite = 3;
            cartoEnfant.Temperament.ReactiviteSensorielle = 2;
            cartoEnfant.Temperament.IntensiteEmotionnelle = 4;
            cartoEnfant.Temperament.Adaptabilite = 3;

            var lecture = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "GOBLET Adrien",
                PatientJson = """{"prenom":"Adrien","nom":"GOBLET"}""",
                LatestCartographieEnfant = cartoEnfant,
                EvaluationsV2Contexte = "■ ENVIRONNEMENT & ÉVALUATION CIBLÉE — séance du 02/09/2026\n  Fiabilité — environnement : Moyennement fiable"
            };

            var rendu = lecture.RenderForLlm();

            Verifie("la cartographie V1 de l'enfant apparaît dans le texte transmis",
                rendu.Contains("[CARTOGRAPHIE DE L'ENFANT — V1"));
            Verifie("le score d'Attachement (4 cochés) est transmis",
                rendu.Contains("Attachement : 4/6"));
            Verifie("un segment non renseigné (Langage à 0) est transmis tel quel, pas omis",
                rendu.Contains("Langage : 0/6"));
            Verifie("le tempérament détaillé est transmis",
                rendu.Contains("activité=4/5") && rendu.Contains("adaptabilité=3/5"));
            Verifie("l'attention non renseignée n'apparaît pas (IsRenseigne = false)",
                !rendu.Contains("Attention & FE"));

            Verifie("les séances V2 (nouveau parcours) apparaissent aussi",
                rendu.Contains("SÉANCES D'ÉVALUATION") && rendu.Contains("environnement : Moyennement fiable"));

            // Une cartographie totalement vierge ne doit produire AUCUNE section — pas de bloc
            // "[CARTOGRAPHIE DE L'ENFANT]" rempli de zéros qui laisserait croire à une évaluation
            // faite alors que rien n'a encore été observé.
            var lectureVierge = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "X",
                PatientJson = "{}",
                LatestCartographieEnfant = new CartographieEnfant()
            };
            Verifie("une cartographie entièrement vide ne produit aucune section trompeuse",
                !lectureVierge.RenderForLlm().Contains("[CARTOGRAPHIE DE L'ENFANT"));

            // Sans cartographie du tout (patient encore au 1er entretien) : pas de section non plus.
            var lectureSansCarto = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "X", PatientJson = "{}"
            };
            Verifie("sans cartographie chargée, aucune section n'est générée",
                !lectureSansCarto.RenderForLlm().Contains("[CARTOGRAPHIE"));
        }

        // ── 18. Le détail des bilans devient une annexe LOCALE (page D), juste
        //        après la situation actuelle — pas mêlé au résumé, pas reporté
        //        à la toute fin du dossier ──────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("── restitution : le détail du parcours de soins se lit juste après, pas mêlé au résumé ni en toute fin ──");
        {
            var dossier = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();

            var blocAt = dossier.Blocs.First(b => b.Key == "patient_antecedents");
            blocAt.ContenuValide = """
                **Antécédents médicaux**
                - Grossesse et accouchement sans particularité.

                **Antécédents développementaux**
                - Marche à 14 mois.

                **Antécédents familiaux**
                - Non connu.

                **Suivi résumé**
                - Suivi CMP — En cours.

                **Bilans résumé**
                - Bilan neuropsychologique — avril 2026.

                **Parcours — détail**
                **Suivi antérieur**
                - CMP de secteur, depuis janvier 2026, motif : agitation scolaire, évolution favorable.

                **Bilans réalisés**
                - Bilan neuropsychologique et psychologique — avril 2026 — psychologue.
                  QI total : 106 (moyenne). Points forts : compréhension verbale.
                  Fragilités : fonctions visuospatiales et exécutives.
                  Hypothèses diagnostiques formulées : TDAH, présentation combinée.
                  Recommandations : évaluation pédopsychiatrique, bilan en ergothérapie.
                """;

            var blocConclusion = dossier.Blocs.First(b => b.Key == "conclusion");
            blocConclusion.ContenuValide = """{"syntheseFinale":"Texte de conclusion."}""";

            var previewSvc = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService());
            var html = previewSvc.BuildPreviewHtml(dossier, "GOBLET Adrien");

            var iAntecedentsResume = html.IndexOf("Bilan neuropsychologique — avril 2026", StringComparison.Ordinal);
            var iSituationActuelle = html.IndexOf("SITUATION ACTUELLE", StringComparison.Ordinal);
            var iQiDetail          = html.IndexOf("QI total", StringComparison.Ordinal);
            var iConclusion        = html.IndexOf("CONCLUSION ET PERSPECTIVES", StringComparison.Ordinal);
            // Sans accent : WebUtility.HtmlEncode rend "é" en entité numérique (&#233;), ce que
            // le navigateur affiche correctement mais qu'une comparaison littérale ne trouve pas.
            var iAnnexeHeader      = html.IndexOf("Parcours de soins", StringComparison.Ordinal);

            Verifie("le résumé compact (page B) est présent", iAntecedentsResume > 0);

            // Cas réel signalé : le modèle a bien séparé résumé (1 ligne, sans mot-clé) et détail
            // (« Parcours — détail » > « Bilans réalisés », avec hypothèses diagnostiques) — le
            // résumé compact doit quand même porter le mot-clé, allé chercher dans l'annexe.
            var iTdahDansResume = html.IndexOf("TDAH", StringComparison.Ordinal);
            Verifie("le résumé déjà compact est enrichi avec le mot-clé trouvé dans l'annexe (TDAH)",
                iTdahDansResume > iAntecedentsResume && iTdahDansResume < iAnnexeHeader,
                $"résumé={iAntecedentsResume} mot-clé={iTdahDansResume} annexe={iAnnexeHeader}");

            Verifie("la situation actuelle (page C) est présente", iSituationActuelle > 0);
            Verifie("le détail (QI, fragilités…) est présent quelque part", iQiDetail > 0);
            Verifie("la conclusion est présente", iConclusion > 0);
            Verifie("l'annexe existe", iAnnexeHeader > 0);

            Verifie("le résumé compact précède l'annexe — B avant D",
                iAntecedentsResume < iAnnexeHeader, $"résumé={iAntecedentsResume} annexe={iAnnexeHeader}");
            Verifie("l'annexe (page D) vient COLLÉE juste après le résumé, AVANT la situation actuelle (page C)",
                iAnnexeHeader < iSituationActuelle, $"annexe={iAnnexeHeader} situation={iSituationActuelle}");
            Verifie("le DÉTAIL (QI, fragilités) est dans cette annexe locale, AVANT la situation actuelle",
                iQiDetail < iSituationActuelle, $"détail={iQiDetail} situation={iSituationActuelle}");
            Verifie("l'annexe (et son détail) vient AVANT la conclusion — pas reportée en toute fin de dossier",
                iAnnexeHeader < iConclusion && iQiDetail < iConclusion,
                $"annexe={iAnnexeHeader} détail={iQiDetail} conclusion={iConclusion}");

            // Le renvoi depuis la page B doit pointer vers la page D, collée juste après —
            // PAS vers la toute dernière page du dossier (qui contient la conclusion, pas l'annexe).
            var derniereMention = System.Text.RegularExpressions.Regex.Match(html, @"Annexe, p\.(\d+)");
            var totalPagesMatch = System.Text.RegularExpressions.Regex.Matches(html, @"Page (\d+)/(\d+)")
                .Cast<System.Text.RegularExpressions.Match>().FirstOrDefault();

            Verifie("le renvoi \"Annexe, p.N\" est présent sur la page des antécédents",
                derniereMention.Success, html.Contains("Détail des suivis et bilans") ? "libellé présent, motif introuvable" : "libellé absent");
            if (derniereMention.Success && totalPagesMatch != null)
            {
                var nRenvoi = int.Parse(derniereMention.Groups[1].Value);
                var nTotal  = int.Parse(totalPagesMatch.Groups[2].Value);
                Verifie("le renvoi pointe vers une page LOCALE (juste après le résumé), pas la dernière page du dossier",
                    nRenvoi < nTotal, $"renvoi=p.{nRenvoi} total={nTotal}");
            }

            // Un dossier SANS détail de parcours ne doit générer aucune annexe — la page ne doit
            // pas apparaître "vide" à la fin de chaque restitution.
            var dossierSansDetail = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            var blocAt2 = dossierSansDetail.Blocs.First(b => b.Key == "patient_antecedents");
            blocAt2.ContenuValide = "**Bilans résumé**\n- Aucun bilan formel.\n\n**Suivi résumé**\n- Aucun suivi spécialisé.";
            var htmlSansDetail = previewSvc.BuildPreviewHtml(dossierSansDetail, "X Y");
            Verifie("sans contenu détaillé, aucune annexe n'est générée",
                !htmlSansDetail.Contains("Parcours de soins"));
        }

        // ── 19. Filet de sécurité : détail écrit DIRECTEMENT sous « résumé »,
        //        sans section « Parcours — détail » séparée (cas réel Gemma) ──
        Console.WriteLine();
        Console.WriteLine("── restitution : le résumé reste compact même quand le LLM n'a pas séparé le détail ──");
        {
            var dossier = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();

            var blocAt = dossier.Blocs.First(b => b.Key == "patient_antecedents");
            // Structure réelle observée avec Gemma : PAS de section "Parcours — détail" du tout ;
            // le modèle écrit le titre du bilan puis ses sous-puces courtes (chacune < 80 car.)
            // directement sous "Bilans résumé" — la détection par longueur de ligne ne voit rien.
            blocAt.ContenuValide = """
                **Antécédents médicaux**
                - RAS.

                **Antécédents développementaux**
                - RAS.

                **Antécédents familiaux**
                - Non connu.

                **Suivi résumé**
                - Suivi psychologique — en cours.
                - Traitement par méthylphénidate — débuté le 18/08/2026.

                **Bilans résumé**
                Bilan neuropsychologique et psychologique — avril 2026.
                - QI total : 106 (moyenne).
                - Points forts : compréhension verbale, raisonnement abstrait.
                - Fragilités : fonctions visuospatiales et exécutives.
                - Hypothèses diagnostiques formulées : TDAH, présentation combinée.
                - Recommandations : évaluation pédopsychiatrique, bilan en ergothérapie.

                Analyses hématologiques — 30/07/2026, laboratoire SYNERGIE.
                - NFS dans les normes pour l'âge.
                - Conclusion : aucune anomalie majeure.
                """;

            var blocConclusion = dossier.Blocs.First(b => b.Key == "conclusion");
            blocConclusion.ContenuValide = """{"syntheseFinale":"Texte de conclusion."}""";

            var previewSvc = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService());
            var html = previewSvc.BuildPreviewHtml(dossier, "GOBLET Adrien");

            var iBilanTitreCompact = html.IndexOf("Bilan neuropsychologique et psychologique", StringComparison.Ordinal);
            var iSituationActuelle = html.IndexOf("SITUATION ACTUELLE", StringComparison.Ordinal);
            var iQiDetail          = html.IndexOf("QI total", StringComparison.Ordinal);
            var iAnnexeHeader      = html.IndexOf("Parcours de soins", StringComparison.Ordinal);

            Verifie("le titre du bilan reste visible dans le résumé compact, avant l'annexe",
                iBilanTitreCompact > 0 && iBilanTitreCompact < iAnnexeHeader,
                $"titre={iBilanTitreCompact} annexe={iAnnexeHeader}");
            Verifie("le QI n'apparaît PAS avant l'annexe (donc pas dans le résumé compact)",
                !(iQiDetail > 0 && iQiDetail < iAnnexeHeader),
                $"QI trouvé avant l'annexe : détail={iQiDetail} annexe={iAnnexeHeader}");
            Verifie("une annexe EST générée malgré l'absence de section « Parcours — détail », collée juste après le résumé",
                iAnnexeHeader > iBilanTitreCompact && iAnnexeHeader < iSituationActuelle,
                $"annexe={iAnnexeHeader} titre={iBilanTitreCompact} situation={iSituationActuelle}");
            Verifie("le détail (QI) se retrouve bien dans cette annexe locale, avant la situation actuelle",
                iQiDetail > iAnnexeHeader && iQiDetail < iSituationActuelle,
                $"QI={iQiDetail} annexe={iAnnexeHeader} situation={iSituationActuelle}");

            // Une puce isolée déjà courte (format attendu) ne doit ni disparaître ni être dupliquée.
            Verifie("une puce déjà compacte (Suivi psychologique) reste telle quelle dans le résumé",
                html.IndexOf("Suivi psychologique", StringComparison.Ordinal) is var iSuivi && iSuivi > 0 && iSuivi < iAnnexeHeader);

            // Vue rapide : le résumé compact doit porter un mot-clé de conclusion (ou, à défaut,
            // d'hypothèse diagnostique), pas seulement le type de bilan et sa date.
            var iCompactHint = html.IndexOf("TDAH", StringComparison.Ordinal);
            Verifie("le résumé compact du bilan neuropsy porte un mot-clé (hypothèse diagnostique) avant l'annexe",
                iCompactHint > 0 && iCompactHint < iAnnexeHeader && iCompactHint > iBilanTitreCompact,
                $"mot-clé={iCompactHint} titre={iBilanTitreCompact} annexe={iAnnexeHeader}");

            // Accent-free : WebUtility.HtmlEncode rend "é" en entité numérique (&#233;).
            var iAnalysesTitre = html.IndexOf("laboratoire SYNERGIE", StringComparison.Ordinal);
            var iAnomalieHint  = html.IndexOf("aucune anomalie majeure", StringComparison.Ordinal);
            Verifie("le résumé compact des analyses porte le mot-clé de sa propre conclusion, avant l'annexe",
                iAnomalieHint > 0 && iAnomalieHint < iAnnexeHeader && iAnomalieHint > iAnalysesTitre,
                $"mot-clé={iAnomalieHint} titre={iAnalysesTitre} annexe={iAnnexeHeader}");

            // Lecture rapide DANS l'annexe elle-même : chaque entrée de "BILANS RÉALISÉS" doit
            // porter son propre rappel de mot-clé, pas seulement le résumé compact de la page B —
            // sinon la seule façon de savoir "ce qu'il faut retenir" est de tout relire.
            var iBilansRealises = html.IndexOf("BILANS R", StringComparison.Ordinal); // accent-free
            // WebUtility.HtmlEncode rend "→" en entité numérique — on cherche la forme encodée.
            var flecheEncodee = System.Net.WebUtility.HtmlEncode("→");
            var iAnnexeQuickRead = html.IndexOf(flecheEncodee, iBilansRealises, StringComparison.Ordinal);
            Verifie("l'annexe (BILANS RÉALISÉS) porte elle aussi un rappel de mot-clé par entrée",
                iBilansRealises > 0 && iAnnexeQuickRead > iBilansRealises,
                $"BILANS RÉALISÉS={iBilansRealises} rappel={iAnnexeQuickRead}");
        }

        // ── 20. Puces consécutives sans ligne vide (cas réel Joan BOKO) :
        //        tous les items du résumé restent visibles, synthèses ancrées sur le
        //        bon label, phrases complètes sans pointillés ──────────────────────
        Console.WriteLine();
        Console.WriteLine("── restitution : items en puces consécutives — rien d'avalé, bonnes synthèses, pas de troncature ──");
        {
            var dossier = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();

            var blocAt = dossier.Blocs.First(b => b.Key == "patient_antecedents");
            // Structure réelle observée : résumés en puces consécutives SANS ligne vide entre
            // elles, et détail en items d'une seule ligne chacun (`*   **Titre** : tout inline`).
            blocAt.ContenuValide = """
                **Antécédents médicaux**
                - RAS.

                **Suivi résumé**
                - Psychomotricité — En cours
                - Guidance parentale — En cours
                - Traitement Medikinet — Actif

                **Bilans résumé**
                - Bilan psychomoteur — 2023
                - Compte-rendu PCO — 2024

                **Parcours — détail**

                **Suivi antérieur**

                *   **Psychomotricité** : 2019 – 2022. Fréquence bimensuelle. Motif : Troubles de l'attention et de la graphomotricité. Évolution : Amélioration significative de la vitesse et de la qualité de la production écrite.
                *   **Suivi psychologique et pédopsychiatrique** : Période non précisée. Motif : Difficultés d'attention. Résultat : Diagnostic de **TDAH**.
                *   **Guidance parentale (type Barkley)** : Début en 2024. Motif : Accompagnement des parents.

                **Bilans réalisés**

                *   **Bilan psychomoteur** : Octobre 2023. Résultats : Déficits marqués en précision visuo-motrice et en vitesse graphique.
                *   **Bilan psychomoteur (PCO)** : Juin 2024. Résultats : Absence de trouble développemental de la coordination.
                """;

            var blocConclusion = dossier.Blocs.First(b => b.Key == "conclusion");
            blocConclusion.ContenuValide = """{"syntheseFinale":"Texte de conclusion."}""";

            var previewSvc = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService());
            var html = previewSvc.BuildPreviewHtml(dossier, "BOKO Joan");

            var iAnnexe = html.IndexOf("Parcours de soins", StringComparison.Ordinal);
            Verifie("l'annexe existe (le détail séparé est bien détecté)", iAnnexe > 0);

            // 1) AUCUN item du résumé n'est avalé : les 3 suivis et les 2 bilans sont tous
            //    visibles AVANT l'annexe (donc sur la page compacte).
            foreach (var attendu in new[] { "Guidance parentale", "Traitement Medikinet", "Compte-rendu PCO" })
            {
                var i = html.IndexOf(attendu, StringComparison.Ordinal);
                Verifie($"« {attendu} » est présent dans le résumé compact, avant l'annexe",
                    i > 0 && i < iAnnexe, $"index={i} annexe={iAnnexe}");
            }

            // 2) La synthèse est ancrée sur le BON label du BON item : la puce compacte
            //    « Psychomotricité » porte son Évolution, pas le contenu d'un autre suivi.
            var iPsychomot = html.IndexOf("Psychomotricit", StringComparison.Ordinal); // sans accent final
            var iEvolution = html.IndexOf("de la vitesse et de la qualit", StringComparison.Ordinal);
            Verifie("la puce Psychomotricité porte sa propre évolution (bonne extraction, bon item)",
                iEvolution > iPsychomot && iEvolution < iAnnexe,
                $"psychomot={iPsychomot} évolution={iEvolution} annexe={iAnnexe}");
            var iMauvaisAppariement = html.IndexOf("riode non pr", StringComparison.Ordinal); // « Période non précisée »
            Verifie("le contenu d'un autre item (Période non précisée) n'est PAS accroché avant l'annexe",
                !(iMauvaisAppariement > 0 && iMauvaisAppariement < iAnnexe),
                $"trouvé avant l'annexe : index={iMauvaisAppariement} annexe={iAnnexe}");

            // 3) Aucune troncature : pas de pointillés entre la carte « PARCOURS DE SOINS »
            //    (page B) et la fin de l'annexe (bornée par la situation actuelle, page C).
            //    (D'autres pages du dossier ont des « … » légitimes dans leurs libellés fixes.)
            var iParcoursCompact = html.IndexOf("PARCOURS DE SOINS", StringComparison.Ordinal);
            var iSituation20     = html.IndexOf("SITUATION ACTUELLE", StringComparison.Ordinal);
            var zoneParcours     = html.Substring(iParcoursCompact, iSituation20 - iParcoursCompact);
            Verifie("aucun pointillé de troncature (…) dans le résumé ni dans l'annexe",
                !zoneParcours.Contains("…") && !zoneParcours.Contains("&#8230;"));

            // 4) Pas de puce « → » orpheline dans l'annexe : les items d'une seule ligne se
            //    lisent déjà d'un coup d'œil, aucun rappel n'est injecté pour eux.
            var flecheEncodee2 = System.Net.WebUtility.HtmlEncode("→");
            var iFlecheAnnexe = html.IndexOf(flecheEncodee2, iAnnexe, StringComparison.Ordinal);
            // (la flèche du lien « Détail des suivis et bilans → Annexe » est AVANT iAnnexe... elle est sur la page B)
            Verifie("aucun rappel → injecté dans l'annexe pour des items d'une seule ligne",
                iFlecheAnnexe < 0, $"flèche trouvée à {iFlecheAnnexe}");
        }

        // ── 21. Cartographie V2 : la sphère 1 (Attachement) lit la feuille parents ──
        Console.WriteLine();
        Console.WriteLine("── restitution : sphère 1 recâblée sur la cartographie V2 (axe attachement) ──");
        {
            var cartoV2 = new MedCompanion.Services.Evaluations.CartographieV2
            {
                PatientNom = "L'ECHARPE Aaron",
                Date = new DateTime(2026, 8, 20),
                Age = 10,
                VerseeAuDossier = true,
                Informateur = "mere",
                InformateurNom = "Sophie",
                ScoresQuestionnaire = new() { ["attachement"] = 4 },
                ReponsesQuestionnaire = new() { ["attachement"] = new[] { "oui", "non", "oui", "oui", "vide", "oui" } },
            };

            var lecture21 = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "L'ECHARPE Aaron",
                PatientJson = "{}",
                LatestCartographieV2 = cartoV2,
            };

            var moteur21 = new FauxMoteur { Reponse = _ => (true, "**Observations**\n- test.\n\n**Niveau clinique** : À surveiller (test).") };
            var svc21 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur21, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));

            string? contenu21 = null;
            await svc21.SuggestCartoSphereAsync(1, lecture21, s => contenu21 = s);

            Verifie("un seul appel au modèle pour l'axe attachement V2", moteur21.Prompts.Count == 1, $"{moteur21.Prompts.Count}");
            var prompt21 = moteur21.Prompts.Count > 0 ? moteur21.Prompts[0] : "";
            Verifie("le score /6 et son niveau couleur (grille unique) sont transmis",
                prompt21.Contains("4/6") && prompt21.Contains("Jaune clair"));
            Verifie("les réponses par dimension sont transmises (Séparation ✓, Recours ✗)",
                prompt21.Contains("✓ Séparation") && prompt21.Contains("✗ Recours"));
            Verifie("une réponse vide est marquée « ? » et explicitement distinguée du non",
                prompt21.Contains("? Confiance en la disponibilité") && prompt21.Contains("n'est PAS un non"));
            Verifie("l'informateur est transmis (feuille remplie par la mère)",
                prompt21.Contains("Mère"));
            Verifie("la voix est celle du parent (« le parent rapporte », pas « on observe »)",
                prompt21.Contains("le parent rapporte"));
            Verifie("le contenu généré est repris tel quel",
                contenu21 != null && contenu21.Contains("**Observations**"));

            // Sans score pour l'axe (feuille non recueillie) : message statique, AUCUN appel.
            var cartoSansFeuille = new MedCompanion.Services.Evaluations.CartographieV2 { VerseeAuDossier = true, Age = 10 };
            var lectureSansFeuille = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "X", PatientJson = "{}", LatestCartographieV2 = cartoSansFeuille };
            var moteurSansF = new FauxMoteur { Reponse = _ => (true, "ne doit pas être appelé") };
            var svcSansF = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurSansF, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            string? contenuSansF = null;
            await svcSansF.SuggestCartoSphereAsync(1, lectureSansFeuille, s => contenuSansF = s);
            Verifie("sans feuille lue : texte statique « non recueilli », aucun appel au modèle",
                moteurSansF.Prompts.Count == 0 && contenuSansF != null && contenuSansF.Contains("non recueilli"));

            // Ancien dossier (aucune V2) : l'ancien chemin V1 reste actif, inchangé.
            var moteurV1 = new FauxMoteur { Reponse = _ => (true, "ok") };
            var svcV1 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurV1, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcV1.SuggestCartoSphereAsync(1,
                new MedCompanion.Services.Restitutions.DossierReading { PatientNomComplet = "X", PatientJson = "{}" },
                _ => { });
            Verifie("sans V2, l'ancien chemin V1 reste actif (pas de régression)",
                moteurV1.Prompts.Count == 1 && moteurV1.Prompts[0].Contains("Aucune cartographie enfant disponible"));
        }

        // ── 22. La synthèse lit le dossier comme un clinicien : séances INTÉGRALES,
        //        sans sa propre version antérieure ni le projet qui en découle ──
        Console.WriteLine();
        Console.WriteLine("── restitution : le rendu réservé à la synthèse (lecture intégrale, sans circularité) ──");
        {
            var lecture22 = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "BOKO Joan",
                PatientJson = """{"prenom":"Joan","nom":"BOKO"}""",
                EvaluationsV2Contexte = "■ CARTOGRAPHIE — Attachement : 4/6",
                EvaluationsV2Integral = "■ CARTOGRAPHIE — Attachement : 4/6\n        [NON] Recours\n        [non renseigné] Consolabilité",
                SyntheseGlobaleMed  = "SYNTHESE-MED-ANTERIEURE",
                SyntheseGlobaleV05  = "SYNTHESE-V05-ANTERIEURE",
                ProjetTherapeutique = "PROJET-QUI-DECOULE-DE-LA-SYNTHESE",
                SyntheseGlobaleDocuments = "META-SYNTHESE-DOCUMENTS",
            };

            var pourBlocs    = lecture22.RenderForLlm();
            var pourSynthese = lecture22.RenderForSynthese();

            // 1. Lecture intégrale des séances — réservée à la synthèse.
            Verifie("la synthèse reçoit le détail item par item des séances",
                pourSynthese.Contains("[NON] Recours") && pourSynthese.Contains("[non renseigné] Consolabilité"));
            Verifie("les autres blocs n'en reçoivent que les conclusions (pas de dilution)",
                !pourBlocs.Contains("[NON] Recours"));
            Verifie("les deux rendus portent bien la conclusion de l'axe",
                pourSynthese.Contains("Attachement : 4/6") && pourBlocs.Contains("Attachement : 4/6"));

            // 2. Pas de circularité : la synthèse ne se relit pas elle-même, ni le projet.
            Verifie("la synthèse ne reçoit PAS sa propre version antérieure",
                !pourSynthese.Contains("SYNTHESE-MED-ANTERIEURE") && !pourSynthese.Contains("SYNTHESE-V05-ANTERIEURE"));
            Verifie("la synthèse ne reçoit PAS le projet thérapeutique qui en découle",
                !pourSynthese.Contains("PROJET-QUI-DECOULE-DE-LA-SYNTHESE"));
            Verifie("les autres blocs les reçoivent toujours (pas de régression)",
                pourBlocs.Contains("SYNTHESE-MED-ANTERIEURE") && pourBlocs.Contains("PROJET-QUI-DECOULE-DE-LA-SYNTHESE"));

            // 3. Les sources externes restent lues des deux côtés : ce sont des faits du
            //    dossier, pas des conclusions de Med.
            Verifie("les documents importés restent transmis à la synthèse",
                pourSynthese.Contains("META-SYNTHESE-DOCUMENTS"));

            // Sans lecture intégrale disponible (ancien dossier), la synthèse retombe sur les
            // conclusions plutôt que de perdre les séances.
            var lectureSansIntegral = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "X", PatientJson = "{}",
                EvaluationsV2Contexte = "■ CARTOGRAPHIE — Attachement : 4/6",
            };
            Verifie("sans lecture intégrale, la synthèse garde au moins les conclusions",
                lectureSansIntegral.RenderForSynthese().Contains("Attachement : 4/6"));
        }

        // ── 23. La synthèse attend les lectures d'amont, puis s'appuie dessus ──
        Console.WriteLine();
        Console.WriteLine("── restitution : la synthèse ne précède pas les lectures, et les relit ──");
        {
            var lecture23 = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "BOKO Joan", PatientJson = "{}",
                EvaluationsV2Contexte = "■ CARTOGRAPHIE — Attachement : 4/6",
            };

            // (a) Dossier vierge : aucune lecture faite → message d'attente, AUCUN appel.
            var dossierVierge = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            var moteurAttente = new FauxMoteur { Reponse = _ => (true, "ne doit pas être appelé") };
            var svcAttente = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurAttente, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));

            string? sortieAttente = null;
            await svcAttente.SuggestSyntheseDiagS1Async(lecture23, s => sortieAttente = s, dossierVierge);

            Verifie("sans lectures d'amont, la synthèse ne lance AUCUN appel au modèle",
                moteurAttente.Prompts.Count == 0, $"{moteurAttente.Prompts.Count} appel(s)");
            Verifie("elle dit qu'elle attend, et nomme ce qui manque",
                sortieAttente != null && sortieAttente.Contains("En attente des lectures")
                && sortieAttente.Contains("Sphère 1") && sortieAttente.Contains("Lecture globale"),
                sortieAttente);
            Verifie("elle n'exige PAS la feuille 5, qui n'existe plus dans le nouveau parcours",
                sortieAttente != null && !sortieAttente.Contains("feuille 5"));

            // (b) Lectures faites : la synthèse part, et les reçoit en tête de prompt.
            var dossierLu = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossierLu.Blocs)
            {
                if (b.Key.StartsWith("carto_s", StringComparison.Ordinal))
                    b.ContenuValide = $"**Observations**\n- Lecture de {b.Key}.\n\n**Niveau clinique** : Satisfaisant.";
                else if (b.Key is "env_edu_f1" or "env_edu_f2" or "env_edu_f3" or "env_edu_f4")
                    b.ContenuValide = $"**Observations**\n- Lecture de {b.Key}.";
                else if (b.Key == "env_edu_global")
                    b.ContenuValide = "**Lecture globale**\nSocle familial solide, vécu scolaire fragile.";
            }

            var moteurLu = new FauxMoteur { Reponse = _ => (true, "synthèse générée.") };
            var svcLu = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurLu, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcLu.SuggestSyntheseDiagS1Async(lecture23, _ => { }, dossierLu);

            Verifie("une fois les lectures faites, la synthèse s'exécute", moteurLu.Prompts.Count == 1, $"{moteurLu.Prompts.Count}");
            var promptLu = moteurLu.Prompts.Count > 0 ? moteurLu.Prompts[0] : "";
            Verifie("les lectures validées sont mises en tête du prompt",
                promptLu.Contains("LECTURES DÉJÀ POSÉES ET VALIDÉES DANS CE DOSSIER"));
            Verifie("la lecture globale de la branche y figure",
                promptLu.Contains("Socle familial solide, vécu scolaire fragile"));
            Verifie("les observations de sphères y figurent aussi",
                promptLu.Contains("Lecture de carto_s1") && promptLu.Contains("Lecture de carto_s8"));
            Verifie("la consigne interdit de les contredire ou de les réinterpréter",
                promptLu.Contains("ne les contredis pas") && promptLu.Contains("CROISER"));

            // (c) Sans dossier fourni (appel programmatique) : pas de blocage — le garde-fou
            //     protège l'usage réel sans casser les appelants qui n'ont pas le dossier.
            var moteurSansDossier = new FauxMoteur { Reponse = _ => (true, "ok") };
            var svcSansDossier = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurSansDossier, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcSansDossier.SuggestSyntheseDiagS1Async(lecture23, _ => { });
            Verifie("sans dossier transmis, l'ancien comportement est préservé",
                moteurSansDossier.Prompts.Count == 1, $"{moteurSansDossier.Prompts.Count}");
        }

        // ── 24. Projet 7.1 : actions portées, datées, hiérarchisées ; bilans à
        //        réaliser seulement ; rôle du médecin fixe ; chaîné à la synthèse ──
        Console.WriteLine();
        Console.WriteLine("── restitution : projet médical — qui fait quoi, quand, à quel degré ──");
        {
            var lecture24 = new MedCompanion.Services.Restitutions.DossierReading
            {
                PatientNomComplet = "GOBLET Adrien", PatientJson = "{}",
                EvaluationsV2Contexte = "■ CARTOGRAPHIE — Attachement : 4/6",
                ProjetTherapeutique = "PROJET-ANTERIEUR-A-NE-PAS-RELIRE",
            };

            // (a) Sans synthèse : le projet attend, aucun appel.
            var dossierSansSynthese = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            var moteurPtAttente = new FauxMoteur { Reponse = _ => (true, "ne doit pas être appelé") };
            var svcPtAttente = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurPtAttente, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            string? sortiePt = null;
            await svcPtAttente.SuggestPtS1Async(lecture24, s => sortiePt = s, dossierSansSynthese);

            Verifie("sans synthèse, le projet ne lance AUCUN appel",
                moteurPtAttente.Prompts.Count == 0, $"{moteurPtAttente.Prompts.Count} appel(s)");
            Verifie("il dit qu'il attend la synthèse et nomme ce qui manque",
                sortiePt != null && sortiePt.Contains("En attente de la synthèse")
                && sortiePt.Contains("Diagnostics retenus"), sortiePt);

            // (b) Synthèse faite : le projet part et la reçoit.
            var dossierAvecSynthese = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossierAvecSynthese.Blocs)
                if (b.Key.StartsWith("synthese_diag_", StringComparison.Ordinal))
                    b.ContenuValide = $"Contenu validé de {b.Key} — hypothèse TDAH à confirmer.";

            var moteurPt = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svcPt = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurPt, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcPt.SuggestPtS1Async(lecture24, _ => { }, dossierAvecSynthese);

            Verifie("avec la synthèse, le projet s'exécute", moteurPt.Prompts.Count == 1, $"{moteurPt.Prompts.Count}");
            var pPt = moteurPt.Prompts.Count > 0 ? moteurPt.Prompts[0] : "";
            Verifie("la synthèse validée est mise en tête du prompt",
                pPt.Contains("SYNTHÈSE DE CE DOSSIER, DÉJÀ RÉDIGÉE ET VALIDÉE")
                && pPt.Contains("hypothèse TDAH à confirmer"));
            Verifie("le projet antérieur n'est PAS relu (plus de circularité)",
                !pPt.Contains("PROJET-ANTERIEUR-A-NE-PAS-RELIRE"));

            // (c) Le vocabulaire des quatre attributs est imposé.
            Verifie("les porteurs sont une liste fermée, avec « professionnel à trouver »",
                pPt.Contains("professionnel à trouver") && pPt.Contains("professionnel en place")
                && pPt.Contains("les parents") && pPt.Contains("l'école"));
            Verifie("les échéances sont imposées", pPt.Contains("sans attendre") && pPt.Contains("cette année scolaire"));
            Verifie("les degrés sont imposés",
                pPt.Contains("indispensable") && pPt.Contains("utile si possible") && pPt.Contains("à réévaluer plus tard"));
            Verifie("l'inflation des « indispensable » est bornée", pPt.Contains("AU PLUS 2"));
            Verifie("degré et certitude diagnostique sont explicitement distincts",
                pPt.Contains("indépendant de la certitude diagnostique"));

            // (d) Bilans : à réaliser seulement, tous types, avec ce qu'ils tranchent.
            Verifie("les bilans déjà faits ne sont plus redemandés",
                pPt.Contains("Ne JAMAIS y remettre les bilans déjà faits"));
            Verifie("les bilans paramédicaux sont inclus (acte de diagnostic)",
                pPt.Contains("orthophonique") && pPt.Contains("neuropsychologique"));
            Verifie("la ligne de partage avec 7.3 est posée (savoir vs faire progresser)",
                pPt.Contains("sert à SAVOIR") && pPt.Contains("appartiennent aux sections 7.2 et 7.3"));
            Verifie("chaque bilan doit dire ce qu'il tranche",
                pPt.Contains("pourTrancher") && pPt.Contains("Un bilan qui ne tranche rien ne se demande pas"));

            // (e) Le rôle du médecin est fixe, jamais généré.
            Verifie("le modèle a interdiction d'écrire l'engagement",
                pPt.Contains("NE PAS écrire de phrase d'engagement"));
            Verifie("le rôle du pédopsychiatre est un texte fixe : préparer, veiller, ajuster, suivre",
                MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService.RoleDuPedopsychiatre.Contains("préparer")
                && MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService.RoleDuPedopsychiatre.Contains("veiller")
                && MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService.RoleDuPedopsychiatre.Contains("ajuster")
                && MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService.RoleDuPedopsychiatre.Contains("suivre"));

            // (f) Rendu : le tableau d'actions et ses pastilles.
            var dossierRendu = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            dossierRendu.Blocs.First(b => b.Key == "pt_s1").ContenuValide = """
                {
                  "intro": "Pilotage médical du projet.",
                  "objectifs": ["Confirmer le diagnostic"],
                  "traitement": { "situationActuelle": "Aucun traitement en cours.", "propositions": [] },
                  "bilans": [
                    { "quoi": "Bilan orthophonique", "porteur": "professionnel à trouver", "echeance": "ce trimestre", "degre": "indispensable", "pourTrancher": "trouble du langage ou retentissement attentionnel" }
                  ],
                  "surveillance": ["Sommeil"],
                  "suivi": [ { "quoi": "Consultation de contrôle", "porteur": "le médecin", "echeance": "à 6 mois", "degre": "recommandé" } ]
                }
                """;
            var htmlPt = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossierRendu, "GOBLET Adrien");

            Verifie("le tableau porte le bilan, son porteur, son échéance et son degré",
                htmlPt.Contains("Bilan orthophonique") && htmlPt.Contains("professionnel")
                && htmlPt.Contains("ce trimestre") && htmlPt.Contains("indispensable"));
            Verifie("ce que le bilan tranche est rendu",
                htmlPt.Contains("retentissement attentionnel"));
            Verifie("le rôle fixe du pédopsychiatre est affiché sans être généré",
                htmlPt.Contains("veiller") && htmlPt.Contains("ajuster"));
            Verifie("l'en-tête dit « à réaliser », plus « déjà réalisés »",
                htmlPt.Contains("BILANS") && !htmlPt.Contains("DÉJÀ RÉALISÉS"));
        }

        // ── 25. L'éditeur du projet médical reflète le modèle : actions portées,
        //        datées, hiérarchisées — et l'aller-retour ne perd rien ──
        Console.WriteLine();
        Console.WriteLine("── restitution : l'éditeur 7.1 édite des actions, pas des phrases ──");
        {
            var dossier25 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            var blocPt = dossier25.Blocs.First(b => b.Key == "pt_s1");
            blocPt.ContenuValide = """
                {
                  "intro": "Pilotage médical.",
                  "objectifs": ["Confirmer le profil exécutif"],
                  "traitement": { "situationActuelle": "Aucun traitement en cours.", "propositions": [] },
                  "bilans": [
                    { "quoi": "Bilan neuropsychologique", "porteur": "professionnel à trouver", "echeance": "sous 1 mois", "degre": "indispensable", "pourTrancher": "quantifier le déficit exécutif" }
                  ],
                  "surveillance": ["Qualité du sommeil"],
                  "suivi": [
                    { "quoi": "Consultation de contrôle", "porteur": "le médecin", "echeance": "à 6 mois", "degre": "recommandé" }
                  ]
                }
                """;

            var vm25 = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocPt, _ => Task.CompletedTask);

            var champs = vm25.PtFields.ToDictionary(f => f.JsonPath);
            Verifie("les bilans déjà réalisés ne s'éditent plus (champ retiré)",
                !champs.ContainsKey("bilans.realises") && !champs.ContainsKey("bilans.aEnvisager"));
            Verifie("l'engagement n'est plus un champ (le rôle du médecin est fixe)",
                !champs.ContainsKey("engagement"));
            Verifie("« bilans » et « suivi » sont des champs d'ACTIONS",
                champs["bilans"].IsActions && champs["suivi"].IsActions);
            Verifie("seuls les bilans portent « pour trancher »",
                champs["bilans"].ActionsAvecPourTrancher && !champs["suivi"].ActionsAvecPourTrancher);

            // Relecture du JSON : les cinq attributs arrivent dans l'éditeur.
            var bilan = champs["bilans"].Actions.FirstOrDefault();
            Verifie("le bilan généré est relu avec ses cinq attributs",
                bilan != null && bilan.Quoi.Contains("neuropsychologique") && bilan.Porteur == "professionnel à trouver"
                && bilan.Echeance == "sous 1 mois" && bilan.Degre == "indispensable"
                && bilan.PourTrancher.Contains("déficit exécutif"));

            // Le vocabulaire proposé à l'écran est CELUI que le prompt impose.
            Verifie("l'éditeur propose exactement les porteurs du prompt",
                MedCompanion.ViewModels.Restitutions.PtActionVm.PorteursPossibles.Contains("professionnel à trouver")
                && MedCompanion.ViewModels.Restitutions.PtActionVm.PorteursPossibles.Contains("les parents")
                && MedCompanion.ViewModels.Restitutions.PtActionVm.PorteursPossibles.Length == 5);
            Verifie("les degrés proposés sont les quatre du prompt",
                MedCompanion.ViewModels.Restitutions.PtActionVm.DegresPossibles.Length == 4
                && MedCompanion.ViewModels.Restitutions.PtActionVm.DegresPossibles.Contains("utile si possible"));

            // ── L'aller-retour : ce que le médecin saisit doit survivre. ──
            // Il ajoute un traitement, puis un bilan, comme en consultation.
            champs["traitement.propositions"].AddItemCommand.Execute(null);
            champs["traitement.propositions"].Items.Last().Value = "Méthylphénidate LP 18 mg le matin";

            champs["bilans"].AddActionCommand.Execute(null);
            var ajoute = champs["bilans"].Actions.Last();
            Verifie("une action ajoutée arrive pré-remplie (saisie rapide en séance)",
                ajoute.Porteur == "professionnel à trouver" && ajoute.Echeance == "ce trimestre" && ajoute.Degre == "recommandé");
            ajoute.Quoi = "ECG";
            ajoute.Degre = "indispensable";
            ajoute.Echeance = "sans attendre";
            ajoute.PourTrancher = "éliminer une contre-indication avant stimulant";

            // Le JSON du bloc doit porter la saisie du médecin.
            var jsonApres = blocPt.ContenuValide ?? "";
            Verifie("le traitement saisi par le médecin est écrit dans le bloc",
                jsonApres.Contains("Méthylphénidate LP 18 mg"));
            Verifie("le bilan ajouté à la main est écrit avec ses attributs",
                jsonApres.Contains("ECG") && jsonApres.Contains("sans attendre")
                && jsonApres.Contains("contre-indication avant stimulant"));
            Verifie("le suivi ne porte pas de clé « pourTrancher » vide",
                !System.Text.RegularExpressions.Regex.IsMatch(jsonApres, @"""quoi""\s*:\s*""Consultation de contr[^""]*""[^}]*pourTrancher"));

            // Rechargé dans un éditeur neuf, rien n'est perdu : c'est ce qui permet
            // de modifier puis de faire propager sans craindre l'écrasement.
            var vmRelu = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocPt, _ => Task.CompletedTask);
            var bilansRelus = vmRelu.PtFields.First(f => f.JsonPath == "bilans").Actions;
            Verifie("après rechargement, les deux bilans sont là, dans l'ordre",
                bilansRelus.Count == 2 && bilansRelus[0].Quoi.Contains("neuropsychologique") && bilansRelus[1].Quoi == "ECG",
                $"{bilansRelus.Count} bilan(s)");
            Verifie("après rechargement, les attributs saisis à la main sont intacts",
                bilansRelus[1].Degre == "indispensable" && bilansRelus[1].Echeance == "sans attendre"
                && bilansRelus[1].PourTrancher.Contains("contre-indication"));
            Verifie("après rechargement, le traitement saisi est intact",
                vmRelu.PtFields.First(f => f.JsonPath == "traitement.propositions")
                      .Items.Any(i => i.Value.Contains("Méthylphénidate")));
        }

        // ── 26. Réécrire un bloc structuré PROPAGE — ça n'efface jamais ──
        Console.WriteLine();
        Console.WriteLine("── restitution : la saisie du médecin survit à la régénération ──");
        {
            var lecture26 = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "GOBLET Adrien", PatientJson = "{}" };

            var blocPt26 = new MedCompanion.Models.Restitutions.RestitutionBloc("pt_s1", "Prise en charge médicale", 27, "clinique");
            const string saisi = """
                {
                  "intro": "La prise en charge vise à stabiliser la régulation émotionnelle.",
                  "objectifs": ["Réduire l'anxiété scolaire"],
                  "traitement": { "situationActuelle": "Aucun traitement en cours.", "propositions": ["Mélatonine 2 mg au coucher"] },
                  "bilans": [],
                  "surveillance": ["Qualité du sommeil"],
                  "suivi": []
                }
                """;

            // (a) Le bloc JSON ne part PLUS par le chemin texte libre.
            var moteurFmt = new FauxMoteur { Reponse = _ => (true, "{\"intro\":\"ok\"}") };
            var svcFmt = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurFmt, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcFmt.ReformuleBlocWithInstructionAsync(blocPt26, saisi, "ajoute un ECG", lecture26);

            var pFmt = moteurFmt.Prompts.Count > 0 ? moteurFmt.Prompts[0] : "";
            Verifie("la reformulation d'un bloc JSON rappelle son format",
                pFmt.Contains("JSON valide uniquement") && pFmt.Contains("pourTrancher"));
            Verifie("elle ne demande plus « commence directement par le contenu » (chemin texte)",
                !pFmt.Contains("sans titre ni commentaire introductif"));
            Verifie("elle exige une réponse JSON complète",
                pFmt.Contains("Réponds UNIQUEMENT par le JSON complet"));

            // (b) La saisie du médecin est transmise et déclarée intouchable.
            Verifie("l'état actuel est transmis au modèle",
                pFmt.Contains("Mélatonine 2 mg au coucher") && pFmt.Contains("ÉTAT ACTUEL DE LA SECTION"));
            Verifie("la règle « ce que le médecin a écrit fait foi » est posée",
                pFmt.Contains("fait foi") && pFmt.Contains("N'en supprime AUCUN"));
            Verifie("la propagation des conséquences est demandée",
                pFmt.Contains("METS À JOUR") && pFmt.Contains("incohérent"));
            Verifie("l'instruction du médecin est transmise", pFmt.Contains("ajoute un ECG"));

            // (c) Filet : une réponse hors format ne détruit pas la saisie.
            var moteurProse = new FauxMoteur { Reponse = _ => (true, "La prise en charge médicale sera adaptée au fil du suivi.") };
            var svcProse = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurProse, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            var apresProse = await svcProse.ReformuleBlocWithInstructionAsync(blocPt26, saisi, "reformule", lecture26);
            Verifie("une réponse en prose est REFUSÉE — la saisie est conservée",
                apresProse.Contains("Mélatonine 2 mg au coucher"), apresProse);

            // Une vraie réponse JSON, elle, est acceptée.
            var moteurJson = new FauxMoteur { Reponse = _ => (true, "{\"intro\":\"nouvelle version\",\"objectifs\":[]}") };
            var svcJson = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurJson, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            var apresJson = await svcJson.ReformuleBlocWithInstructionAsync(blocPt26, saisi, "reformule", lecture26);
            Verifie("une réponse JSON valide est bien appliquée", apresJson.Contains("nouvelle version"));

            // (d) « Suggérer » sur une section déjà remplie propage aussi.
            var dossier26 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossier26.Blocs)
                if (b.Key.StartsWith("synthese_diag_", StringComparison.Ordinal)) b.ContenuValide = "Synthèse validée.";
            dossier26.Blocs.First(b => b.Key == "pt_s1").ContenuValide = saisi;

            var moteurSug = new FauxMoteur { Reponse = _ => (true, "{\"intro\":\"ok\"}") };
            var svcSug = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurSug, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcSug.SuggestPtS1Async(lecture26, _ => { }, dossier26);

            var pSug = moteurSug.Prompts.Count > 0 ? moteurSug.Prompts[0] : "";
            Verifie("Suggérer sur une section déjà remplie ne repart PAS de zéro",
                pSug.Contains("ÉTAT ACTUEL DE LA SECTION") && pSug.Contains("Mélatonine 2 mg au coucher"));
            Verifie("sans instruction, il propage les conséquences de la saisie",
                pSug.Contains("propage simplement les conséquences"));

            // (e) Le « pour trancher » d'un bilan ajouté à la main est rédigé par Med —
            //     seule exception à la règle de conservation.
            Verifie("« pourTrancher » est déclaré comme le SEUL champ réécrivable",
                pFmt.Contains("SEULE EXCEPTION") && pFmt.Contains("SEUL champ que tu peux écrire ou reformuler"));
            Verifie("un bilan sans « pourTrancher » doit être rédigé depuis le dossier",
                pFmt.Contains("rédige-le à partir du dossier"));
            Verifie("un « pourTrancher » sommaire est précisé, pas remplacé",
                pFmt.Contains("reformule-le en question clinique précise")
                && pFmt.Contains("tu la précises, tu ne la remplaces pas"));
            Verifie("le reste du bilan ajouté par le médecin reste intouchable",
                pFmt.Contains("son porteur, son échéance et son degré ne changent JAMAIS"));

            // Sur une section vide, il génère normalement.
            var dossierVide26 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossierVide26.Blocs)
                if (b.Key.StartsWith("synthese_diag_", StringComparison.Ordinal)) b.ContenuValide = "Synthèse validée.";
            var moteurVide = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svcVide = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurVide, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcVide.SuggestPtS1Async(lecture26, _ => { }, dossierVide26);
            Verifie("sur une section vide, Suggérer génère normalement",
                moteurVide.Prompts.Count == 1 && !moteurVide.Prompts[0].Contains("ÉTAT ACTUEL DE LA SECTION"));
        }

        // ── 27. Projet 7.2 : l'indication commande la section ──────────────
        Console.WriteLine();
        Console.WriteLine("── restitution : accompagnement psychologique — indiqué ou non, et pour qui ──");
        {
            var lecture27 = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "GOBLET Adrien", PatientJson = "{}" };

            var dossier27 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossier27.Blocs)
                if (b.Key.StartsWith("synthese_diag_", StringComparison.Ordinal)) b.ContenuValide = "Synthèse validée.";
            dossier27.Blocs.First(b => b.Key == "pt_s1").ContenuValide =
                """{"intro":"Pilotage médical.","bilans":[{"quoi":"Bilan neuropsychologique","echeance":"sous 1 mois","degre":"indispensable"}]}""";

            var moteur27 = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svc27 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur27, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svc27.SuggestPtS2Async(lecture27, _ => { }, dossier27);

            var p27 = moteur27.Prompts.Count > 0 ? moteur27.Prompts[0] : "";

            // (a) L'indication ouvre la section et la commande.
            Verifie("l'indication est le premier champ du format",
                p27.Contains("\"indication\"") && p27.Contains("critereReevaluation"));
            Verifie("le vocabulaire de l'indication inclut « non indiqué à ce stade »",
                p27.Contains("non indiqué à ce stade"));
            Verifie("l'indication commande la section (trois cas exclusifs)",
                p27.Contains("L'INDICATION COMMANDE TOUTE LA SECTION")
                && p27.Contains("(A) INDIQUÉ") && p27.Contains("(B) UN PSYCHOLOGUE SUIT DÉJÀ")
                && p27.Contains("(C) NON INDIQUÉ ou DIFFÉRÉ"));
            Verifie("un suivi non indiqué ne s'accompagne pas d'objectifs (pas d'auto-contradiction)",
                p27.Contains("laisse objectifs, modalites, axesTravail, reperesEvolution VIDES"));
            Verifie("le suivi déjà en place bascule en articulation, pas en projet neuf",
                p27.Contains("n'écris PAS un projet comme si l'on partait de zéro"));

            // (b) Ce qui a été retiré, et pourquoi.
            Verifie("la technique du psychologue n'est plus prescrite",
                p27.Contains("le choix de la") && p27.Contains("appartient au psychologue")
                && !p27.Contains("outilsUtilises"));
            Verifie("les deux listes redondantes ont fusionné",
                !p27.Contains("resultatsAttendus") && !p27.Contains("indicateursPositifs")
                && p27.Contains("reperesEvolution") && p27.Contains("pointsVigilance"));

            // (c) Le chaînage : 7.2 lit la synthèse ET la section médicale.
            Verifie("la section lit la synthèse validée", p27.Contains("SYNTHÈSE DE CE DOSSIER"));
            Verifie("elle lit aussi la prise en charge médicale déjà écrite",
                p27.Contains("SECTIONS DU PROJET DÉJÀ ÉCRITES") && p27.Contains("Bilan neuropsychologique"));
            Verifie("elle doit se mettre en phase, sans répéter ni contredire",
                p27.Contains("mets-la en phase") && p27.Contains("ce qu'elle peut engager de front est limité"));

            // (d) L'éditeur reflète le nouveau modèle.
            var blocS2 = dossier27.Blocs.First(b => b.Key == "pt_s2");
            blocS2.ContenuValide = """
                {
                  "indication": { "degre": "à réévaluer plus tard", "porteur": "professionnel à trouver",
                                  "motif": "La régulation émotionnelle s'améliore avec les aménagements scolaires.",
                                  "critereReevaluation": "Persistance des crises malgré le PAP à 3 mois." },
                  "intro": "", "objectifs": [], "modalites": [], "axesTravail": [],
                  "reperesEvolution": [], "pointsVigilance": [], "articulation": []
                }
                """;
            var vmS2 = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocS2, _ => Task.CompletedTask);
            var champsS2 = vmS2.PtFields.ToDictionary(f => f.JsonPath);

            Verifie("l'éditeur ouvre sur un champ Indication",
                champsS2.ContainsKey("indication") && champsS2["indication"].IsIndication);
            Verifie("les champs retirés ne s'éditent plus",
                !champsS2.ContainsKey("outilsUtilises") && !champsS2.ContainsKey("resultatsAttendus")
                && !champsS2.ContainsKey("indicateursPositifs") && !champsS2.ContainsKey("engagement"));
            Verifie("l'indication générée est relue avec ses quatre informations",
                champsS2["indication"].Indication.Degre == "à réévaluer plus tard"
                && champsS2["indication"].Indication.Porteur == "professionnel à trouver"
                && champsS2["indication"].Indication.Motif.Contains("aménagements scolaires")
                && champsS2["indication"].Indication.CritereReevaluation.Contains("PAP à 3 mois"));

            // Modifiée à la main, elle doit survivre à l'aller-retour.
            champsS2["indication"].Indication.Degre = "indispensable";
            champsS2["indication"].Indication.CritereReevaluation = "";
            var vmS2Relu = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocS2, _ => Task.CompletedTask);
            Verifie("l'indication corrigée à la main survit au rechargement",
                vmS2Relu.PtFields.First(f => f.JsonPath == "indication").Indication.Degre == "indispensable");

            // (e) Le rendu : indication en tête, et pas d'objectifs sous une indication écartée.
            var htmlS2 = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossier27, "GOBLET Adrien");
            Verifie("la page affiche l'indication et son motif",
                htmlS2.Contains("INDICATION") && htmlS2.Contains("indispensable"));
        }

        // ── 28. Projet 7.3 : soins et ressources de vie ne se confondent pas ──
        Console.WriteLine();
        Console.WriteLine("── restitution : soutien développemental — rééducations vs ressources du quotidien ──");
        {
            var lecture28 = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "GOBLET Adrien", PatientJson = "{}" };

            var dossier28 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossier28.Blocs)
                if (b.Key.StartsWith("synthese_diag_", StringComparison.Ordinal)) b.ContenuValide = "Synthèse validée.";
            dossier28.Blocs.First(b => b.Key == "pt_s1").ContenuValide =
                """{"bilans":[{"quoi":"Bilan orthophonique","echeance":"sous 1 mois","degre":"indispensable"}]}""";

            var moteur28 = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svc28 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur28, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svc28.SuggestPtS3Async(lecture28, _ => { }, dossier28);
            var p28 = moteur28.Prompts.Count > 0 ? moteur28.Prompts[0] : "";

            // (a) Les deux natures sont posées, avec des attributs différents.
            Verifie("le format sépare rééducations et ressources de vie",
                p28.Contains("\"reeducations\"") && p28.Contains("\"ressourcesVie\""));
            Verifie("la consigne interdit de les mélanger",
                p28.Contains("DEUX NATURES À NE JAMAIS MÉLANGER"));
            Verifie("les rééducations sont des soins : degré et échéance",
                p28.Contains("= des SOINS") && p28.Contains("degré et une échéance"));
            Verifie("les ressources de vie n'ont NI degré NI échéance",
                p28.Contains("NI degré NI échéance") && p28.Contains("une habitude ne se programme pas"));
            Verifie("leur objectif est obligatoire et précis",
                p28.Contains("« objectif » y est OBLIGATOIRE") && p28.Contains("judo"));
            // La frontière avec 7.4 : ici l'enfant pratique, là-bas le parent change sa façon
            // de faire. Sans elle, « temps d'échange ritualisé » sortait dans les deux sections.
            Verifie("7.3 privilégie l'engagement corporel de l'enfant",
                p28.Contains("D'ABORD L'ENGAGEMENT CORPOREL") && p28.Contains("arts") && p28.Contains("natation"));
            Verifie("7.3 exclut ce que les parents installent ou changent",
                p28.Contains("c'est l'ENFANT QUI FAIT")
                && p28.Contains("minuteur visuel") && p28.Contains("temps d'échange ritualisé"));

            Verifie("une ressource doit être CONCRÈTE, pas une catégorie abstraite",
                p28.Contains("DOIT ÊTRE CONCRET, jamais une catégorie abstraite")
                && p28.Contains("Activités de structuration temporelle")
                && p28.Contains("planning imagé du matin"));
            Verifie("les exemples doivent convenir à cet enfant, pas venir d'un manuel",
                p28.Contains("son âge, ses centres d'intérêt") && p28.Contains("Pas des exemples de manuel"));

            // (b) La frontière avec les bilans de 7.1.
            Verifie("aucun bilan n'est proposé ici",
                p28.Contains("NE PROPOSE PAS DE BILAN ICI") && p28.Contains("appartiennent à la section 7.1"));
            Verifie("une rééducation qui dépend d'un bilan est différée, pas prescrite",
                p28.Contains("à réévaluer plus tard") && p28.Contains("on demande d'abord, on décide ensuite"));
            Verifie("la section lit la prise en charge médicale déjà écrite",
                p28.Contains("SECTIONS DU PROJET DÉJÀ ÉCRITES") && p28.Contains("Bilan orthophonique"));

            // (c) Ce qui a été retiré.
            Verifie("les doublons ont disparu du format",
                !p28.Contains("axesPrioritaires") && !p28.Contains("ressourcesEnfant"));

            // (d) L'éditeur : deux blocs d'actions aux attributs distincts.
            var blocS3 = dossier28.Blocs.First(b => b.Key == "pt_s3");
            blocS3.ContenuValide = """
                {
                  "intro": "S'appuyer sur ses ressources.",
                  "objectifs": ["Soutenir la graphomotricité"],
                  "reeducations": [
                    { "quoi": "Psychomotricité", "porteur": "professionnel à trouver", "echeance": "ce trimestre",
                      "degre": "recommandé", "objectif": "améliorer la précision du geste" }
                  ],
                  "ressourcesVie": [
                    { "quoi": "Judo", "porteur": "les parents", "objectif": "canaliser l'excès moteur dans un cadre structuré" }
                  ],
                  "reperesEvolution": ["Écriture plus lisible"],
                  "reevaluation": ["Point à 6 mois"]
                }
                """;
            var vmS3 = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocS3, _ => Task.CompletedTask);
            var champsS3 = vmS3.PtFields.ToDictionary(f => f.JsonPath);

            Verifie("l'éditeur a deux blocs d'actions distincts",
                champsS3["reeducations"].IsActions && champsS3["ressourcesVie"].IsActions);
            Verifie("les rééducations portent échéance et degré, pas les ressources de vie",
                champsS3["reeducations"].ActionsAvecEcheanceEtDegre
                && !champsS3["ressourcesVie"].ActionsAvecEcheanceEtDegre);
            Verifie("les deux portent un objectif visé, aucune ne porte « pour trancher »",
                champsS3["reeducations"].ActionsAvecObjectif && champsS3["ressourcesVie"].ActionsAvecObjectif
                && !champsS3["reeducations"].ActionsAvecPourTrancher);
            Verifie("le judo est relu avec son objectif",
                champsS3["ressourcesVie"].Actions[0].Objectif.Contains("canaliser l'excès moteur"));

            // Une ressource ajoutée arrive portée par les parents, sans degré.
            champsS3["ressourcesVie"].AddActionCommand.Execute(null);
            var res = champsS3["ressourcesVie"].Actions.Last();
            Verifie("une ressource ajoutée est portée par les parents, sans degré ni échéance",
                res.Porteur == "les parents" && res.Degre == "" && res.Echeance == "");
            res.Quoi = "Marche quotidienne";
            res.Objectif = "améliorer l'endormissement";

            var jsonS3 = blocS3.ContenuValide ?? "";
            Verifie("le JSON des ressources ne porte ni degré ni échéance",
                !System.Text.RegularExpressions.Regex.IsMatch(jsonS3, @"""quoi""\s*:\s*""Marche[^}]*""degre"""));
            var vmS3Relu = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocS3, _ => Task.CompletedTask);
            Verifie("la ressource saisie survit au rechargement",
                vmS3Relu.PtFields.First(f => f.JsonPath == "ressourcesVie").Actions
                        .Any(a => a.Quoi == "Marche quotidienne" && a.Objectif.Contains("endormissement")));

            // (e) Le rendu : deux cartes, la seconde sans colonnes Quand/Degré.
            var htmlS3 = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossier28, "GOBLET Adrien");
            Verifie("la page distingue les deux blocs",
                htmlS3.Contains("RÉÉDUCATIONS ET PRISES EN CHARGE") && htmlS3.Contains("RESSOURCES DU QUOTIDIEN"));
            // Sans accent ni apostrophe : HtmlEncode rend « é » en &#233; et « ' » en &#39;.
            Verifie("les objectifs des deux natures sont rendus",
                htmlS3.Contains("cision du geste") && htmlS3.Contains("moteur dans un cadre structur"));
        }

        // ── 29. Projet 7.4 : trois natures, et un porteur pour chaque ligne ──
        Console.WriteLine();
        Console.WriteLine("── restitution : accompagnement parental — soutenir, faire intervenir, ajuster ──");
        {
            var lecture29 = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "GOBLET Adrien", PatientJson = "{}" };

            var dossier29 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossier29.Blocs)
                if (b.Key.StartsWith("synthese_diag_", StringComparison.Ordinal)) b.ContenuValide = "Synthèse validée.";
            // Une section amont déjà écrite : c'est elle qui déclenche le rappel de non-répétition.
            dossier29.Blocs.First(b => b.Key == "pt_s3").ContenuValide =
                """{"ressourcesVie":[{"quoi":"Judo","porteur":"les parents","objectif":"canaliser l'excès moteur"}]}""";

            var moteur29 = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svc29 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur29, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svc29.SuggestPtS4Async(lecture29, _ => { }, dossier29);
            var p29 = moteur29.Prompts.Count > 0 ? moteur29.Prompts[0] : "";

            // (a) Les trois blocs et leur frontière.
            Verifie("le format sépare les trois natures",
                p29.Contains("\"accompagnementParents\"") && p29.Contains("\"interventionsEducatives\"")
                && p29.Contains("\"auQuotidien\""));
            Verifie("la consigne interdit de se tromper de bloc",
                p29.Contains("TROIS BLOCS DISTINCTS"));

            // (b) La règle du porteur : le médecin en situation simple, un pro si complexe.
            Verifie("le médecin porte la guidance quand la situation est simple",
                p29.Contains("« le médecin » quand la situation est SIMPLE")
                && p29.Contains("C'est le cas le plus fréquent"));
            Verifie("un professionnel prend le relais quand elle est complexe",
                p29.Contains("quand la situation est COMPLEXE")
                && p29.Contains("conflit conjugal") && p29.Contains("épuisement parental"));

            // (c) L'éducatif : un intervenant, ou pas.
            Verifie("l'absence d'intervenant éducatif est une réponse valable",
                p29.Contains("Une liste VIDE est une réponse clinique parfaitement valable"));

            // (d) Le quotidien : concret, et sans hiérarchie.
            Verifie("les ajustements du quotidien n'ont ni échéance ni degré",
                p29.Contains("Ni échéance ni degré : ce sont des habitudes"));
            Verifie("ils doivent être concrets, avec des exemples",
                p29.Contains("DOIT ÊTRE CONCRET") && p29.Contains("prévenir 5 minutes avant"));
            Verifie("7.4 tient le registre de la communication et de la pédagogie",
                p29.Contains("COMMUNICATION") && p29.Contains("PÉDAGOGIE du"));
            Verifie("7.4 exclut les activités que l'enfant pratique",
                p29.Contains("c'est le PARENT QUI CHANGE SA")
                && p29.Contains("Ne propose aucune activité extrascolaire dans cette section"));
            Verifie("aucune proposition ne doit figurer deux fois dans le projet",
                p29.Contains("AUCUNE PROPOSITION NE DOIT FIGURER DEUX FOIS"));

            // (e) Le ton — cette page ne doit pas se lire comme un reproche.
            Verifie("les stratégies s'écrivent comme des ajustements, pas des reproches",
                p29.Contains("AJUSTEMENTS DE L'ENVIRONNEMENT")
                && p29.Contains("sonne comme un reproche"));
            Verifie("les tournures culpabilisantes sont interdites",
                p29.Contains("Pas de « il faudrait que les parents"));

            // (f) Ce qui a été retiré.
            Verifie("les doublons d'objectifs ont disparu",
                !p29.Contains("axesPrioritaires") && !p29.Contains("objectifsCourtTerme")
                && !p29.Contains("forcesFamiliales"));

            // (g) L'éditeur : trois blocs d'actions, attributs et défauts distincts.
            var blocS4 = dossier29.Blocs.First(b => b.Key == "pt_s4");
            blocS4.ContenuValide = """
                {
                  "intro": "Cette famille s'appuie sur un cadre stable.",
                  "objectifs": ["Réduire les tensions du soir"],
                  "accompagnementParents": [
                    { "quoi": "Guidance parentale en consultation", "porteur": "le médecin", "echeance": "ce trimestre",
                      "degre": "recommandé", "objectif": "outiller la gestion des transitions" }
                  ],
                  "interventionsEducatives": [],
                  "auQuotidien": [
                    { "quoi": "Annoncer les changements à l'avance (minuteur visible, rituel de fin)",
                      "porteur": "les parents", "objectif": "réduire les crises de transition" }
                  ],
                  "reperesEvolution": ["Moins de crises au coucher"]
                }
                """;
            // Rendu capturé AVANT toute manipulation de l'éditeur : les ajouts ci-dessous
            // réécrivent le bloc et rempliraient la liste qu'on veut justement voir vide.
            var htmlS4 = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossier29, "GOBLET Adrien");

            var vmS4 = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocS4, _ => Task.CompletedTask);
            var champsS4 = vmS4.PtFields.ToDictionary(f => f.JsonPath);

            Verifie("les trois blocs sont éditables comme des actions",
                champsS4["accompagnementParents"].IsActions && champsS4["interventionsEducatives"].IsActions
                && champsS4["auQuotidien"].IsActions);
            Verifie("seul « au quotidien » est sans échéance ni degré",
                champsS4["accompagnementParents"].ActionsAvecEcheanceEtDegre
                && champsS4["interventionsEducatives"].ActionsAvecEcheanceEtDegre
                && !champsS4["auQuotidien"].ActionsAvecEcheanceEtDegre);
            Verifie("la guidance est relue avec son porteur et son objectif",
                champsS4["accompagnementParents"].Actions[0].Porteur == "le médecin"
                && champsS4["accompagnementParents"].Actions[0].Objectif.Contains("transitions"));

            // Les défauts traduisent la règle : médecin pour la guidance, pro pour l'éducatif.
            champsS4["accompagnementParents"].AddActionCommand.Execute(null);
            champsS4["interventionsEducatives"].AddActionCommand.Execute(null);
            champsS4["auQuotidien"].AddActionCommand.Execute(null);
            Verifie("une guidance ajoutée est portée par le médecin",
                champsS4["accompagnementParents"].Actions.Last().Porteur == "le médecin");
            Verifie("une intervention éducative ajoutée est à trouver",
                champsS4["interventionsEducatives"].Actions.Last().Porteur == "professionnel à trouver");
            Verifie("un ajustement du quotidien est porté par les parents, sans degré",
                champsS4["auQuotidien"].Actions.Last().Porteur == "les parents"
                && champsS4["auQuotidien"].Actions.Last().Degre == "");

            // (h) Le rendu : l'absence d'intervenant est écrite, pas laissée vide.
            Verifie("la page affiche les trois blocs",
                htmlS4.Contains("ACCOMPAGNEMENT DES PARENTS") && htmlS4.Contains("INTERVENTIONS")
                && htmlS4.Contains("AU QUOTIDIEN"));
            Verifie("l'absence d'intervention éducative est dite explicitement",
                htmlS4.Contains("Aucune intervention"));
        }

        // ── 30. Projet 7.5 : le médecin pose le cadre, le modèle ne propose rien ──
        Console.WriteLine();
        Console.WriteLine("── restitution : école — aucun dispositif suggéré par le modèle ──");
        {
            var lecture30 = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "GOBLET Adrien", PatientJson = "{}" };

            var dossier30 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            foreach (var b in dossier30.Blocs)
                if (b.Key.StartsWith("synthese_diag_", StringComparison.Ordinal)) b.ContenuValide = "Synthèse validée.";

            var moteur30 = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svc30 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur30, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svc30.SuggestPtS5Async(lecture30, _ => { }, dossier30);
            var p30 = moteur30.Prompts.Count > 0 ? moteur30.Prompts[0] : "";

            // (a) L'interdit central.
            Verifie("le modèle ne propose AUCUN dispositif scolaire",
                p30.Contains("TU NE PROPOSES AUCUN DISPOSITIF SCOLAIRE"));
            Verifie("la raison est dite : c'est une décision administrative qui circule",
                p30.Contains("DÉCISION ADMINISTRATIVE") && p30.Contains("remis aux parents")
                && p30.Contains("circule et engage"));
            Verifie("les circuits distincts sont rappelés",
                p30.Contains("médecin scolaire") && p30.Contains("MDPH"));

            // (b) Cadre posé : on décline, on ne transforme pas.
            Verifie("un dispositif posé est conservé à l'identique",
                p30.Contains("N'en ajoute AUCUN") && p30.Contains("un PAP ne devient jamais un PPS"));
            Verifie("les aménagements sont déclinés pour CE dispositif",
                p30.Contains("n'ouvrent pas les mêmes droits"));
            Verifie("un dispositif déjà en place ne se redemande pas",
                p30.Contains("ne se redemande pas") && p30.Contains("S'APPUIE dessus"));

            // (c) Cadre vide : conseils applicables sans procédure, et rien d'autre.
            Verifie("sans cadre, aucun dispositif n'est cité, même au conditionnel",
                p30.Contains("Ne propose AUCUN dispositif, même au conditionnel")
                && p30.Contains("N'en cite aucun nom"));
            Verifie("sans cadre, ce sont des conseils applicables immédiatement",
                p30.Contains("sans aucune procédure administrative")
                && p30.Contains("placement dans la classe"));
            Verifie("l'absence de dispositif est présentée comme une réponse pleine",
                p30.Contains("réponse pleine et entière"));

            // (d) La seule retouche autorisée : la forme.
            Verifie("les sigles sont normalisés",
                p30.Contains("« sessad » → « SESSAD »") && p30.Contains("« pap » → « PAP »"));
            Verifie("une faute évidente est corrigée",
                p30.Contains("Corrige une faute d'orthographe ou un mot manquant évident"));
            Verifie("en cas de doute, on ne devine pas",
                p30.Contains("EN CAS DE DOUTE, LAISSE TEL QUEL")
                && p30.Contains("Tu corriges la FORME, jamais le FOND"));

            // (e) La page ne traite que la scolarité et sera lue par l'école.
            Verifie("les activités extrascolaires restent en 7.3",
                p30.Contains("Les activités extrascolaires appartiennent à 7.3"));
            Verifie("la page est écrite en sachant que l'école la lira",
                p30.Contains("sera lue par l'école") && p30.Contains("ne divulgue que ce qui est nécessaire"));

            // (f) L'éditeur : le cadre s'ouvre en premier, avec son statut.
            var blocS5 = dossier30.Blocs.First(b => b.Key == "pt_s5");
            blocS5.ContenuValide = """
                {
                  "cadreScolaire": [
                    { "quoi": "PAP", "statut": "déjà en place", "porteur": "l'école", "echeance": "à renouveler",
                      "degre": "indispensable", "objectif": "aménagements de temps et de supports" }
                  ],
                  "intro": "Scolarité en CM1.",
                  "amenagements": ["Placer au premier rang, loin de la fenêtre"],
                  "coordination": ["Point d'étape avec l'enseignante en janvier"],
                  "reperesEvolution": ["Devoirs rendus complets"]
                }
                """;
            var htmlS5 = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossier30, "GOBLET Adrien");

            var vmS5 = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocS5, _ => Task.CompletedTask);
            var champsS5 = vmS5.PtFields.ToDictionary(f => f.JsonPath);

            Verifie("le cadre scolaire est le premier champ, et il annonce qu'il se remplit avant",
                vmS5.PtFields[0].JsonPath == "cadreScolaire"
                && vmS5.PtFields[0].Title.Contains("avant de générer"));
            Verifie("seul le cadre scolaire porte un statut",
                champsS5["cadreScolaire"].ActionsAvecStatut);
            Verifie("le statut saisi est relu",
                champsS5["cadreScolaire"].Actions[0].Statut == "déjà en place"
                && champsS5["cadreScolaire"].Actions[0].Quoi == "PAP");

            // Le statut survit à l'aller-retour — c'est lui qui commande le texte.
            champsS5["cadreScolaire"].Actions[0].Statut = "à renouveler";
            var vmS5Relu = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocS5, _ => Task.CompletedTask);
            Verifie("le statut corrigé à la main survit au rechargement",
                vmS5Relu.PtFields.First(f => f.JsonPath == "cadreScolaire").Actions[0].Statut == "à renouveler");

            // (g) Le rendu distingue les deux cas.
            Verifie("avec un cadre, la page parle d'AMÉNAGEMENTS et affiche le statut",
                htmlS5.Contains("CADRE SCOLAIRE") && htmlS5.Contains("AM") && htmlS5.Contains("en place"));

            var dossierSansCadre = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            dossierSansCadre.Blocs.First(b => b.Key == "pt_s5").ContenuValide =
                """{"cadreScolaire":[],"intro":"","amenagements":["Découper les consignes en une étape à la fois"],"coordination":[],"reperesEvolution":[]}""";
            var htmlSansCadre = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossierSansCadre, "X Y");
            Verifie("sans cadre, la page parle de CONSEILS applicables sans démarche",
                htmlSansCadre.Contains("CONSEILS") && htmlSansCadre.Contains("sans d")
                && !htmlSansCadre.Contains("CADRE SCOLAIRE"));
        }

        // ── 31. Feuille de route : les actions du projet, triées et attribuées ──
        Console.WriteLine();
        Console.WriteLine("── restitution : la feuille de route traduit le projet structuré ──");
        {
            var lecture31 = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "GOBLET Adrien", PatientJson = "{}" };

            var dossier31 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();

            // Un projet complet, volontairement écrit dans le désordre des priorités.
            dossier31.Blocs.First(b => b.Key == "pt_s1").ContenuValide = """
                {
                  "bilans": [
                    { "quoi": "Bilan orthophonique", "porteur": "professionnel à trouver", "echeance": "ce trimestre",
                      "degre": "recommandé", "pourTrancher": "préciser le trouble du langage" },
                    { "quoi": "ECG", "porteur": "les parents", "echeance": "sans attendre",
                      "degre": "indispensable", "pourTrancher": "avant introduction du stimulant" }
                  ],
                  "suivi": [
                    { "quoi": "Consultation de titration", "porteur": "le médecin", "echeance": "sous 1 mois", "degre": "indispensable" }
                  ]
                }
                """;
            dossier31.Blocs.First(b => b.Key == "pt_s3").ContenuValide = """
                {
                  "reeducations": [
                    { "quoi": "Psychomotricité", "porteur": "professionnel à trouver", "echeance": "cette année scolaire",
                      "degre": "à réévaluer plus tard", "objectif": "à décider après le bilan" }
                  ],
                  "ressourcesVie": [
                    { "quoi": "Judo", "porteur": "les parents", "objectif": "canaliser l'excès moteur" }
                  ]
                }
                """;
            dossier31.Blocs.First(b => b.Key == "pt_s5").ContenuValide = """
                {
                  "cadreScolaire": [
                    { "quoi": "PAP", "statut": "déjà en place", "porteur": "l'école", "degre": "indispensable",
                      "objectif": "aménagements de supports" },
                    { "quoi": "Demande d'AESH", "statut": "à demander", "porteur": "les parents",
                      "echeance": "ce trimestre", "degre": "recommandé", "objectif": "accompagnement en classe" }
                  ]
                }
                """;

            var moteur31 = new FauxMoteur { Reponse = _ => (true, "1. **Étape :** texte.") };
            var svc31 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur31, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svc31.RedigerFeuilleDeRouteAsync(dossier31);
            var p31 = moteur31.Prompts.Count > 0 ? moteur31.Prompts[0] : "";

            // (a) Plus de JSON brut : les actions sont mises en clair avec leurs attributs.
            Verifie("le projet n'est plus transmis en JSON brut",
                !p31.Contains("\"quoi\"") && !p31.Contains("{"), p31.Length > 0 ? "" : "prompt vide");
            Verifie("chaque action porte son porteur et son échéance en clair",
                p31.Contains("porté par : les parents") && p31.Contains("échéance : sans attendre"));
            Verifie("la finalité de l'action est transmise",
                p31.Contains("But :") && p31.Contains("avant introduction du stimulant"));

            // (b) Le tri : indispensable d'abord, puis le plus proche dans le temps.
            var iEcg    = p31.IndexOf("ECG", StringComparison.Ordinal);
            var iTitr   = p31.IndexOf("Consultation de titration", StringComparison.Ordinal);
            var iOrtho  = p31.IndexOf("Bilan orthophonique", StringComparison.Ordinal);
            var iJudo   = p31.IndexOf("Judo", StringComparison.Ordinal);
            Verifie("les actions indispensables passent devant les recommandées",
                iEcg > 0 && iTitr > 0 && iOrtho > iEcg && iOrtho > iTitr,
                $"ECG={iEcg} titration={iTitr} ortho={iOrtho}");
            Verifie("à priorité égale, le plus urgent passe devant",
                iEcg < iTitr, $"ECG={iEcg} titration={iTitr}");
            Verifie("ce qui n'a pas de degré vient après les actions hiérarchisées",
                iJudo > iOrtho, $"judo={iJudo} ortho={iOrtho}");

            // (c) Ce qui n'est pas une étape à engager est écarté.
            Verifie("un dispositif déjà en place n'est pas une prochaine étape",
                !p31.Contains("PAP"), "le PAP déjà en place ne doit pas figurer");
            Verifie("une rééducation à réévaluer plus tard n'encombre pas la liste",
                p31.IndexOf("Psychomotricité", StringComparison.Ordinal) is var iPsy
                && (iPsy < 0 || iPsy > iJudo), "elle doit être en fin de liste, pas devant");
            Verifie("ce qui reste à demander y figure bien",
                p31.Contains("Demande d'AESH"));

            // (d) La consigne sait maintenant se servir de ces attributs.
            Verifie("l'ordre reçu doit être conservé",
                p31.Contains("GARDE L'ORDRE") && p31.Contains("par quoi commencer"));
            Verifie("chaque porteur a sa formulation pour les parents",
                p31.Contains("vous prendrez rendez-vous") && p31.Contains("je revois votre enfant")
                && p31.Contains("l'école mettra en place"));
            Verifie("« professionnel à trouver » est dit comme tel — c'est ce qui prend du temps",
                p31.Contains("il faudra trouver un orthophoniste"));
            Verifie("les échéances sont dites en langage courant, sans date",
                p31.Contains("d'ici la fin du trimestre") && p31.Contains("Pas de date exacte"));
            Verifie("le vocabulaire de priorité ne sort pas dans le texte des parents",
                p31.Contains("Ne mentionne PAS les priorités telles quelles"));

            // (e) Projet vide : le message d'attente, aucun appel au modèle.
            var dossierVide31 = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
            var moteurVide31 = new FauxMoteur { Reponse = _ => (true, "ne doit pas être appelé") };
            var svcVide31 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurVide31, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            var sortieVide = await svcVide31.RedigerFeuilleDeRouteAsync(dossierVide31);
            Verifie("sans projet, la feuille reste en attente sans appeler le modèle",
                moteurVide31.Prompts.Count == 0
                && sortieVide == MedCompanion.Services.Restitutions.RestitutionSuggesterService.FeuilleDeRouteEnAttente);
        }

        // ── 32. Conclusion : elle ferme la lecture, elle ne la redit pas ──
        Console.WriteLine();
        Console.WriteLine("── restitution : la conclusion rend l'enfant entier et dit ce qui reste ouvert ──");
        {
            var lecture32 = new MedCompanion.Services.Restitutions.DossierReading
            { PatientNomComplet = "GOBLET Adrien", PatientJson = "{}" };

            static MedCompanion.Models.Restitutions.DossierRestitutionInitial DossierComplet32()
            {
                var d = new MedCompanion.Models.Restitutions.DossierRestitutionInitial();
                foreach (var k in new[] { "synthese_diag_s1", "synthese_diag_s3", "synthese_diag_s4", "synthese_diag_s5" })
                    d.Blocs.First(b => b.Key == k).ContenuValide = "Rédigé.";

                // La synthèse pose ses diagnostics AVEC leur degré de certitude.
                d.Blocs.First(b => b.Key == "synthese_diag_s2").ContenuValide = """
                    [
                      { "label": "TDAH presentation combinee", "certitude": "Élevée", "elements": ["Inattention scolaire"] },
                      { "label": "Trouble du langage ecrit", "certitude": "Hypothèse", "elements": ["Lecture laborieuse"] }
                    ]
                    """;

                d.Blocs.First(b => b.Key == "pt_s1").ContenuValide = """
                    {
                      "bilans": [
                        { "quoi": "Bilan orthophonique", "porteur": "professionnel a trouver", "echeance": "ce trimestre",
                          "degre": "recommandé", "pourTrancher": "preciser le trouble du langage ecrit" }
                      ],
                      "suivi": [
                        { "quoi": "Consultation de titration", "porteur": "le médecin", "echeance": "sous 1 mois", "degre": "indispensable" },
                        { "quoi": "Consultation de suivi", "porteur": "le médecin", "echeance": "à 6 mois", "degre": "recommandé" }
                      ]
                    }
                    """;
                d.Blocs.First(b => b.Key == "pt_s2").ContenuValide = """{"indication":{"porteur":"professionnel a trouver","motif":"Estime de soi"}}""";
                d.Blocs.First(b => b.Key == "pt_s3").ContenuValide = """
                    {
                      "reeducations": [
                        { "quoi": "Psychomotricite", "porteur": "professionnel a trouver", "echeance": "cette année scolaire",
                          "degre": "à réévaluer plus tard", "objectif": "a decider apres le bilan" }
                      ],
                      "ressourcesVie": []
                    }
                    """;
                d.Blocs.First(b => b.Key == "pt_s4").ContenuValide = """{"accompagnementParents":[],"interventionsEducatives":[],"auQuotidien":[]}""";
                d.Blocs.First(b => b.Key == "pt_s5").ContenuValide = """{"cadreScolaire":[],"intro":"CM1"}""";
                return d;
            }

            // (a) Le verrou : dernier maillon de la chaîne, la conclusion attend son amont.
            var moteurVide32 = new FauxMoteur { Reponse = _ => (true, "ne doit pas être appelé") };
            var svcVide32 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurVide32, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            var sortieVide32 = "";
            await svcVide32.SuggestConclusionAsync(lecture32, s => sortieVide32 = s,
                new MedCompanion.Models.Restitutions.DossierRestitutionInitial());
            Verifie("sans synthèse ni projet, la conclusion ne part pas et n'appelle pas le modèle",
                moteurVide32.Prompts.Count == 0 && sortieVide32.Contains("En attente de la synthèse et du projet"),
                sortieVide32);
            Verifie("le message d'attente nomme ce qu'il faut générer d'abord",
                sortieVide32.Contains("Compréhension globale") && sortieVide32.Contains("7.1 Prise en charge médicale"),
                sortieVide32);

            // (b) Le dossier complet : les ouvertures viennent de champs DÉJÀ structurés.
            var dossier32 = DossierComplet32();
            var moteur32 = new FauxMoteur { Reponse = _ => (true, "{}") };
            var svc32 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteur32, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svc32.SuggestConclusionAsync(lecture32, _ => { }, dossier32);
            var p32 = moteur32.Prompts.Count > 0 ? moteur32.Prompts[0] : "";

            Verifie("avec la synthèse et le projet, la conclusion part",
                moteur32.Prompts.Count == 1, $"{moteur32.Prompts.Count} appel(s)");
            Verifie("le relevé des ouvertures est transmis",
                p32.Contains("CE QUE CE DOSSIER LAISSE OUVERT"));
            Verifie("un bilan demandé dit la question qu'il tranche",
                p32.Contains("doit trancher : preciser le trouble du langage ecrit"));
            Verifie("ce qui est à réévaluer plus tard figure comme une ouverture",
                p32.Contains("À réévaluer plus tard — Psychomotricite"));
            Verifie("un diagnostic non acquis est une ouverture",
                p32.Contains("Diagnostic non acquis — Trouble du langage ecrit"));
            Verifie("un diagnostic acquis n'en est pas une",
                !p32.Contains("Diagnostic non acquis — TDAH"));
            Verifie("l'incertitude ne s'invente pas",
                p32.Contains("N'INVENTE AUCUNE incertitude"));
            Verifie("un « reste ouvert » vidé à la main est une décision, pas un oubli",
                p32.Contains("est une DÉCISION du médecin") && p32.Contains("laisse-le vide"));

            // (c) Ce que la page ne doit plus produire — page 2 et section 7 le disent déjà.
            Verifie("le format demandé se limite aux trois champs de cette page",
                p32.Contains("resteOuvert") && !p32.Contains("feuilleDeRoute")
                && !p32.Contains("messageParents") && !p32.Contains("prochainsRdv"));
            Verifie("la consigne interdit explicitement de redire ce qui est ailleurs",
                p32.Contains("NI feuille de route, NI liste de rendez-vous, NI messages"));
            Verifie("les forces doivent être ancrées dans une observation",
                p32.Contains("ANCRÉE dans une observation") && p32.Contains("est une flatterie"));
            Verifie("la synthèse validée est lue avant de conclure",
                p32.Contains("SYNTHÈSE DE CE DOSSIER, DÉJÀ RÉDIGÉE ET VALIDÉE"));

            // (d) Déjà écrite : on propage, on n'efface pas.
            var dossierDejaEcrit32 = DossierComplet32();
            dossierDejaEcrit32.Blocs.First(b => b.Key == "conclusion").ContenuValide =
                """{"intro":"Adrien est un enfant curieux.","forces":["Curiosite — questionne beaucoup"],"resteOuvert":[]}""";
            var moteurProp32 = new FauxMoteur { Reponse = _ => (true, """{"intro":"x","forces":[],"resteOuvert":[]}""") };
            var svcProp32 = new MedCompanion.Services.Restitutions.RestitutionSuggesterService(
                moteurProp32, new MedCompanion.Services.Restitutions.DossierReaderService(new PathService()));
            await svcProp32.SuggestConclusionAsync(lecture32, _ => { }, dossierDejaEcrit32);
            var pProp32 = moteurProp32.Prompts.Count > 0 ? moteurProp32.Prompts[0] : "";
            Verifie("une conclusion déjà écrite est propagée, pas régénérée",
                pProp32.Contains("ÉTAT ACTUEL DE LA SECTION") && pProp32.Contains("CONSERVE mot pour mot"));
            Verifie("la propagation rappelle qu'un « reste ouvert » vide se laisse vide",
                pProp32.Contains("laisse-le vide"));

            // (e) Le rendu : quatre blocs, et rien qui double le reste du dossier.
            var dossierRendu32 = DossierComplet32();
            dossierRendu32.Blocs.First(b => b.Key == "conclusion").ContenuValide = """
                {
                  "intro": "Adrien est un enfant curieux qui se fatigue vite en classe.",
                  "forces": ["Curiosite — questionne beaucoup sur le monde", "Attachement — cherche le regard de sa mere"],
                  "resteOuvert": ["La part langagiere reste a preciser — le bilan orthophonique le dira."]
                }
                """;
            var htmlC = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossierRendu32, "GOBLET Adrien");
            var pageC = htmlC.Substring(htmlC.IndexOf("CONCLUSION ET PERSPECTIVES", StringComparison.Ordinal));

            Verifie("la page rend l'enfant entier, ses forces et ce qui reste ouvert",
                pageC.Contains("CE QUE NOUS RETENONS DE") && pageC.Contains("SES FORCES")
                && pageC.Contains("CE QUI RESTE OUVERT"));
            Verifie("la page dit ce qu'est ce document et à qui il appartient",
                pageC.Contains("CE DOCUMENT VOUS APPARTIENT")
                && pageC.Contains("Ce dossier est une photographie")
                && pageC.Contains("Il vous appartient"));
            Verifie("la feuille de route et les messages d'encouragement ont quitté la dernière page",
                !pageC.Contains("NOTRE FEUILLE DE ROUTE") && !pageC.Contains("MESSAGE AUX PARENTS")
                && !pageC.Contains("PROCHAINS RENDEZ-VOUS"));

            // (f) Le prochain rendez-vous est REPRIS du projet, jamais réinventé.
            Verifie("le prochain rendez-vous du médecin vient du projet, le plus proche d'abord",
                pageC.Contains("Prochain rendez-vous") && pageC.Contains("Consultation de titration")
                && !pageC.Contains("Consultation de suivi"));

            // (g) Vider « ce qui reste ouvert » fait disparaître la carte — pas une boîte vide.
            var dossierSansOuvert = DossierComplet32();
            dossierSansOuvert.Blocs.First(b => b.Key == "conclusion").ContenuValide =
                """{"intro":"Adrien est un enfant curieux.","forces":["Curiosite — questionne beaucoup"],"resteOuvert":[]}""";
            var htmlSansOuvert = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossierSansOuvert, "GOBLET Adrien");
            var pageSansOuvert = htmlSansOuvert.Substring(htmlSansOuvert.IndexOf("CONCLUSION ET PERSPECTIVES", StringComparison.Ordinal));
            Verifie("vidé dans l'éditeur, « ce qui reste ouvert » disparaît de la page",
                !pageSansOuvert.Contains("CE QUI RESTE OUVERT"));
            Verifie("le reste de la page tient debout sans lui",
                pageSansOuvert.Contains("SES FORCES") && pageSansOuvert.Contains("CE DOCUMENT VOUS APPARTIENT"));

            // Sans action portée par le médecin, la page ne fabrique pas une date.
            var dossierSansRdv = DossierComplet32();
            dossierSansRdv.Blocs.First(b => b.Key == "pt_s1").ContenuValide = """{"bilans":[],"suivi":[]}""";
            dossierSansRdv.Blocs.First(b => b.Key == "conclusion").ContenuValide =
                """{"intro":"Adrien.","forces":[],"resteOuvert":[]}""";
            var htmlSansRdv = new MedCompanion.Services.Restitutions.RestitutionHtmlPreviewService(new PathService())
                .BuildPreviewHtml(dossierSansRdv, "GOBLET Adrien");
            Verifie("sans rendez-vous dans le projet, la page le dit au lieu d'en inventer un",
                htmlSansRdv.Substring(htmlSansRdv.IndexOf("CONCLUSION ET PERSPECTIVES", StringComparison.Ordinal))
                          .Contains("fixer ensemble"));

            // (h) L'éditeur reflète le même modèle que la page.
            var blocConcl = dossierRendu32.Blocs.First(b => b.Key == "conclusion");
            var vmConcl = new MedCompanion.ViewModels.Restitutions.RestitutionBlocViewModel(blocConcl, _ => Task.CompletedTask);
            var clesConcl = vmConcl.PtFields.Select(f => f.JsonPath).ToList();
            Verifie("l'éditeur n'expose plus que les trois champs de cette page",
                clesConcl.Count == 3 && clesConcl[0] == "intro" && clesConcl[1] == "forces"
                && clesConcl[2] == "resteOuvert",
                string.Join(", ", clesConcl));
            Verifie("le contenu saisi est relu dans l'éditeur",
                vmConcl.PtFields.First(f => f.JsonPath == "resteOuvert").Items.Count == 1
                && vmConcl.PtFields.First(f => f.JsonPath == "forces").Items.Count == 2);
        }

        Console.WriteLine();
        Console.WriteLine(echecs == 0 ? "=== SÉANCE 3 OK ===" : $"=== {echecs} ÉCHEC(S) ===");
        return echecs == 0 ? 0 : 1;
    }
}

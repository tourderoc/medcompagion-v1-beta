using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Carte;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Carte
{
    /// <summary>
    /// Étape 2 de PLAN_CARTE_PATIENT.md : pour les dossiers sans 1ère consultation structurée,
    /// le modèle relit les notes et PROPOSE famille, fratrie, motif, scolarité. Rien n'est écrit
    /// sans validation du médecin, et chaque valeur dit de quelle note elle vient.
    /// </summary>
    public class CarteExtractionService
    {
        private readonly ILLMService _llm;

        public CarteExtractionService(ILLMService llm) => _llm = llm;

        private const string Consigne = """
            Tu relis les notes de consultation d'un pédopsychiatre pour un enfant, afin de remplir sa
            fiche. Relève UNIQUEMENT ce qui est écrit dans les notes. Ne déduis rien, ne complète rien,
            n'invente rien : une information absente des notes vaut "".

            Pour chaque champ, donne la valeur et la date de la note où tu l'as lue (jj/mm/aaaa).
            Si l'information change d'une note à l'autre (la classe, par exemple), prends la plus récente.

            Champs :
            - "situation"   : "Parents ensemble", "Parents séparés", "Parents divorcés", "Famille recomposée", ou "Autre : …"
            - "garde"       : le mode de garde si les parents sont séparés (ex. "principale chez la mère", "alternée")
            - "pere"        : ce qui est dit du père — prénom, âge, métier — en quelques mots
            - "mere"        : idem pour la mère
            - "fratrie"     : frères et sœurs avec prénom et âge (ex. "1 frère (Haiden, 11 ans), 1 sœur (Madele, 3 ans)"), ou "enfant unique" si c'est écrit
            - "motif"       : le motif de la première consultation, 12 mots au plus
            - "oriente_par" : qui a adressé l'enfant (école, médecin traitant, parents, PMI…)
            - "ecole"       : le nom de l'école, du collège ou de l'établissement
            - "classe"      : la classe (CP, CE1, 6ème…)

            Réponds UNIQUEMENT avec cet objet JSON :
            {"situation": {"valeur": "", "note": ""}, "garde": {"valeur": "", "note": ""}, "pere": {"valeur": "", "note": ""}, "mere": {"valeur": "", "note": ""}, "fratrie": {"valeur": "", "note": ""}, "motif": {"valeur": "", "note": ""}, "oriente_par": {"valeur": "", "note": ""}, "ecole": {"valeur": "", "note": ""}, "classe": {"valeur": "", "note": ""}}
            """;

        public async Task<(bool ok, Dictionary<string, ChampCarte> champs, string? erreur)> ExtraireAsync(
            string dossierPatient, CancellationToken ct = default)
        {
            var notes = CartePatientService.TexteDesNotes(dossierPatient);
            if (notes.Length == 0) return (false, new(), "aucune note dans le dossier");

            var (ok, reponse, err) = await _llm.ChatAsync(Consigne,
                new List<(string role, string content)> { ("user", notes) },
                maxTokens: 700, cancellationToken: ct, numCtx: 12288);
            if (!ok) return (false, new(), err ?? "le modèle n'a pas répondu");

            var texte = reponse ?? "";
            int debut = texte.IndexOf('{'), fin = texte.LastIndexOf('}');
            if (debut < 0 || fin <= debut) return (false, new(), "le modèle n'a pas rendu de JSON");

            var champs = new Dictionary<string, ChampCarte>();
            try
            {
                using var doc = JsonDocument.Parse(texte[debut..(fin + 1)]);
                foreach (var (cle, _) in CompletionCarte.Liste)
                {
                    if (!doc.RootElement.TryGetProperty(cle, out var e)) continue;
                    // Le modèle rend parfois une chaîne nue au lieu de l'objet {valeur, note}.
                    var champ = e.ValueKind switch
                    {
                        JsonValueKind.Object => new ChampCarte
                        {
                            Valeur = Lire(e, "valeur"),
                            Note   = Lire(e, "note")
                        },
                        JsonValueKind.String => new ChampCarte { Valeur = e.GetString() ?? "" },
                        _ => null
                    };
                    if (champ != null) champs[cle] = champ;
                }
            }
            catch (Exception ex)
            {
                return (false, new(), $"JSON illisible : {ex.Message}");
            }
            return (true, champs, null);
        }

        private static string Lire(JsonElement e, string nom)
            => e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MedCompanion.ViewModels.Restitutions
{
    /// <summary>Un morceau de texte libre repéré dans un bloc, avec de quoi le remettre à sa place.</summary>
    public sealed class SegmentTexte
    {
        public string BlocKey { get; init; } = "";
        public string BlocTitre { get; init; } = "";

        /// <summary>
        /// Où se trouve ce texte dans le bloc. Vide pour un bloc en texte simple ; sinon le chemin
        /// JSON (« objectifs[1] », « bilans[0].quoi »). Sert à dire au médecin QUOI relire, et à
        /// replacer une reformulation acceptée à l'endroit exact d'où elle vient.
        /// </summary>
        public string Chemin { get; init; } = "";

        public string Texte { get; init; } = "";

        public string Ou => string.IsNullOrEmpty(Chemin) ? BlocTitre : $"{BlocTitre} · {Chemin}";
    }

    /// <summary>
    /// Extraction du texte librement rédigé d'un dossier — et d'ELLE SEULE.
    ///
    /// C'EST LA GARDE DE SÉCURITÉ DE LA PHASE 3. Une couche linguistique reformule ; si elle
    /// reformule une valeur choisie dans une liste fermée, elle casse le dossier en silence :
    ///  • <c>porteur</c> commande les pastilles de couleur, l'annexe contacts (« professionnel à
    ///    trouver » y devient une ligne à compléter) et le regroupement du document ;
    ///  • <c>echeance</c> commande le TRI de la feuille de route ;
    ///  • <c>degre</c> décide si une section est écartée (« non indiqué à ce stade ») ;
    ///  • <c>statut</c> distingue un bilan à demander d'un bilan déjà en place.
    /// « les parents » reformulé en « la famille » ne veut rien dire de moins pour un lecteur — mais
    /// ne correspond à aucune valeur connue, et tout ce qui en dépend cesse de fonctionner.
    ///
    /// La liste des clés interdites est donc DÉRIVÉE des vocabulaires eux-mêmes, jamais recopiée :
    /// un jour où l'on ajoutera une liste fermée, elle sera protégée sans qu'on y pense.
    /// </summary>
    public static class TexteLibreDuDossier
    {
        /// <summary>
        /// Clés dont la valeur est choisie dans une liste fermée. Elles ne sont jamais proposées à
        /// la reformulation, jamais envoyées à un modèle, jamais réécrites.
        /// </summary>
        public static readonly IReadOnlyList<string> ClesFermees =
            new[] { "porteur", "echeance", "degre", "statut" };

        /// <summary>
        /// Valeurs autorisées, tous vocabulaires confondus. Sert au contrôle : si un texte libre est
        /// EXACTEMENT une de ces valeurs, le reformuler serait tout aussi destructeur.
        /// </summary>
        public static IReadOnlyList<string> ValeursFermees { get; } =
            PtActionVm.PorteursPossibles
                .Concat(PtActionVm.EcheancesPossibles)
                .Concat(PtActionVm.DegresPossibles)
                .Concat(PtActionVm.StatutsPossibles)
                .Concat(PtIndicationVm.DegresPossibles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Longueur en deçà de laquelle un texte n'a rien à dire sur sa construction.</summary>
        public const int LongueurMinimale = 40;

        /// <summary>
        /// Tous les segments de texte libre d'un bloc. Un bloc en Markdown rend un seul segment ;
        /// un bloc structuré rend un segment par champ rédigé, les clés fermées écartées.
        /// </summary>
        public static List<SegmentTexte> Extraire(Models.Restitutions.RestitutionBloc bloc)
        {
            var segments = new List<SegmentTexte>();
            var contenu  = bloc.ContenuValide;
            if (string.IsNullOrWhiteSpace(contenu)) return segments;

            var t = contenu.TrimStart();
            if (t.StartsWith("{") || t.StartsWith("["))
            {
                try
                {
                    using var doc = JsonDocument.Parse(contenu);
                    Parcourir(doc.RootElement, "", bloc, segments);
                    return segments;
                }
                catch (JsonException)
                {
                    // Contenu qui commence par une accolade sans être du JSON valide : traité comme
                    // du texte. Mieux vaut le relire que de le perdre.
                }
            }

            segments.Add(new SegmentTexte
            {
                BlocKey = bloc.Key, BlocTitre = bloc.Titre, Chemin = "", Texte = contenu.Trim()
            });
            return segments;
        }

        private static void Parcourir(JsonElement el, string chemin,
                                      Models.Restitutions.RestitutionBloc bloc,
                                      List<SegmentTexte> sortie)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    {
                        // La clé fermée est écartée ICI, avant toute descente : ni lue, ni comptée,
                        // ni transmise.
                        if (ClesFermees.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) continue;
                        Parcourir(p.Value, string.IsNullOrEmpty(chemin) ? p.Name : $"{chemin}.{p.Name}",
                                  bloc, sortie);
                    }
                    break;

                case JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in el.EnumerateArray())
                        Parcourir(item, $"{chemin}[{i++}]", bloc, sortie);
                    break;

                case JsonValueKind.String:
                    var s = (el.GetString() ?? "").Trim();
                    if (s.Length == 0) break;

                    // Ceinture et bretelles : une valeur de vocabulaire rangée sous une clé
                    // inattendue reste écartée. La clé protège d'habitude ; la valeur protège quand
                    // la clé a changé de nom.
                    if (ValeursFermees.Contains(s, StringComparer.OrdinalIgnoreCase)) break;

                    sortie.Add(new SegmentTexte
                    {
                        BlocKey = bloc.Key, BlocTitre = bloc.Titre, Chemin = chemin, Texte = s
                    });
                    break;
            }
        }

        /// <summary>
        /// Les segments qui valent la peine d'être relus : assez longs pour qu'une phrase y soit
        /// construite. Un intitulé d'action de trois mots n'a pas de syntaxe à corriger, et le
        /// signaler ferait du bruit sans action possible derrière.
        /// </summary>
        public static List<SegmentTexte> ARelire(Models.Restitutions.RestitutionBase dossier)
            => dossier.Blocs
                      .SelectMany(Extraire)
                      .Where(s => s.Texte.Length >= LongueurMinimale)
                      .ToList();
    }
}

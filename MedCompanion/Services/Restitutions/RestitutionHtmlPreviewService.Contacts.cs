using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using MedCompanion.Models.Restitutions;

namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Annexe Contacts — dernière page utile du Dossier de Restitution, placée entre la
    /// conclusion et l'annexe méthodologique.
    ///
    /// Elle ne génère rien et ne demande aucune saisie : tout ce qu'elle affiche existe
    /// déjà dans le dossier bleu. Les parents et l'école viennent de patient.json, le
    /// médecin traitant aussi, et les intervenants de intervenants.json — extraits
    /// automatiquement à l'import de chaque bilan, avec le document dont ils viennent.
    ///
    /// Deux règles tenues ici, les mêmes que sur la conclusion :
    /// • un champ vide ne s'écrit pas, une carte vide ne se dessine pas — les parents
    ///   n'ont pas à lire les trous du dossier administratif ;
    /// • rien n'est inventé : la section « à identifier » recopie les actions du projet
    ///   portées par « professionnel à trouver », elle n'en déduit aucun nom.
    /// </summary>
    public partial class RestitutionHtmlPreviewService
    {
        // ── Modèle de la page ───────────────────────────────────────────────

        private sealed class ContactLigne
        {
            public string Libelle { get; init; } = "";
            public string Valeur  { get; init; } = "";
        }

        private sealed class ContactCarte
        {
            public string Icone   { get; init; } = "";
            public string Titre   { get; init; } = "";
            public string Detail  { get; init; } = "";
            public List<ContactLigne> Lignes { get; } = new();

            /// <summary>
            /// Hauteur approximative rendue, en pixels. Sert uniquement à savoir quand ouvrir une
            /// deuxième page : <c>.page</c> est en <c>overflow:hidden</c>, un débordement ne se
            /// verrait pas à l'écran — il disparaîtrait du PDF sans rien dire.
            /// </summary>
            public int HauteurApprox => 56 + Lignes.Count * 18 + (string.IsNullOrWhiteSpace(Detail) ? 0 : 6);

            public void Ajouter(string libelle, string? valeur)
            {
                if (string.IsNullOrWhiteSpace(valeur)) return;
                Lignes.Add(new ContactLigne { Libelle = libelle, Valeur = valeur.Trim() });
            }

            /// <summary>Une carte sans titre ET sans ligne n'a rien à montrer.</summary>
            public bool EstVide => Lignes.Count == 0 && string.IsNullOrWhiteSpace(Detail);
        }

        // ── Lecture du dossier bleu ─────────────────────────────────────────

        /// <summary>
        /// Lit patient.json en acceptant les deux variantes de casse. Les dossiers anciens
        /// sont en PascalCase, les nouveaux en camelCase — le même fichier, deux orthographes.
        /// </summary>
        private sealed class ContactsSource
        {
            private readonly JsonElement _root;
            private readonly bool _ok;

            private ContactsSource(JsonElement root, bool ok) { _root = root; _ok = ok; }

            public static ContactsSource Vide => new(default, false);

            public static ContactsSource Lire(string jsonPath, out JsonDocument? doc)
            {
                doc = null;
                try
                {
                    if (!File.Exists(jsonPath)) return Vide;
                    doc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
                    return new ContactsSource(doc.RootElement, doc.RootElement.ValueKind == JsonValueKind.Object);
                }
                catch { return Vide; }
            }

            /// <summary>Valeur du champ, en camelCase puis en PascalCase. "" si absent.</summary>
            public string this[string camel]
            {
                get
                {
                    if (!_ok) return "";
                    var pascal = char.ToUpperInvariant(camel[0]) + camel.Substring(1);
                    if (_root.TryGetProperty(camel, out var a) && a.ValueKind == JsonValueKind.String)
                        return (a.GetString() ?? "").Trim();
                    if (_root.TryGetProperty(pascal, out var b) && b.ValueKind == JsonValueKind.String)
                        return (b.GetString() ?? "").Trim();
                    return "";
                }
            }
        }

        /// <summary>
        /// Préfixe « Dr » quand le nom saisi ne le porte pas déjà. Le champ du dossier bleu est
        /// libre : certains dossiers contiennent « BERNARD », d'autres « Dr BERNARD ». Ajouter
        /// sans regarder produirait « Dr Dr BERNARD » sur la moitié des dossiers.
        /// </summary>
        private static string AvecTitreDocteur(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return "";
            nom = nom.Trim();

            var deja = nom.StartsWith("Dr",        StringComparison.OrdinalIgnoreCase)
                    || nom.StartsWith("Docteur",   StringComparison.OrdinalIgnoreCase)
                    || nom.IndexOf(" Dr ",         StringComparison.OrdinalIgnoreCase) >= 0
                    || nom.IndexOf("Professeur",   StringComparison.OrdinalIgnoreCase) >= 0
                    || nom.StartsWith("Pr ",       StringComparison.OrdinalIgnoreCase);

            return deja ? nom : "Dr " + nom;
        }

        /// <summary>Assemble « 12 rue des Lilas · 34000 Montpellier » en sautant ce qui manque.</summary>
        private static string JoindreAdresse(params string?[] morceaux) =>
            string.Join(" · ", morceaux.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!.Trim()));

        /// <summary>
        /// Les professionnels que le projet dit rester à trouver. Repris mot pour mot des
        /// actions (`quoi`), jamais reformulés : la page contacts et la section 7 doivent
        /// dire la même chose, sinon les parents lisent deux projets différents.
        /// </summary>
        private static List<string> ProfessionnelsAIdentifier(DossierRestitutionInitial dossier)
        {
            var trouves = new List<string>();

            foreach (var bloc in dossier.Blocs)
            {
                if (bloc.Key != "pt_s1" && bloc.Key != "pt_s2" && bloc.Key != "pt_s3"
                 && bloc.Key != "pt_s4" && bloc.Key != "pt_s5") continue;

                var contenu = string.IsNullOrWhiteSpace(bloc.ContenuValide) ? bloc.ContenuPreremplit : bloc.ContenuValide;
                if (string.IsNullOrWhiteSpace(contenu)) continue;

                var start = contenu.IndexOf('{');
                var end   = contenu.LastIndexOf('}');
                if (start < 0 || end <= start) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(contenu.Substring(start, end - start + 1)); }
                catch { continue; }

                using (doc)
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        CollecterAIdentifier(prop.Value, trouves);
                }
            }

            return trouves;
        }

        /// <summary>
        /// Les actions vivent soit dans un tableau (`actions`), soit dans l'objet `indication`
        /// d'un bilan, qui porte son porteur au même titre. Les deux formes sont lues.
        /// </summary>
        private static void CollecterAIdentifier(JsonElement valeur, List<string> trouves)
        {
            switch (valeur.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in valeur.EnumerateArray()) CollecterAIdentifier(item, trouves);
                    break;

                case JsonValueKind.Object:
                    string Lire(string n) =>
                        valeur.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
                            ? (v.GetString() ?? "").Trim() : "";

                    var porteur = Lire("porteur");
                    if (porteur.IndexOf("trouver", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var quoi = Lire("quoi");
                        if (quoi.Length == 0) quoi = Lire("intitule");
                        if (quoi.Length > 0 && !trouves.Any(t => string.Equals(t, quoi, StringComparison.OrdinalIgnoreCase)))
                            trouves.Add(quoi);
                    }

                    // Un bilan porte son indication dans un sous-objet — on descend d'un cran.
                    if (valeur.TryGetProperty("indication", out var ind))
                        CollecterAIdentifier(ind, trouves);
                    break;
            }
        }

        // ── Construction de la page ─────────────────────────────────────────

        /// <summary>
        /// Rend l'annexe contacts, ou "" s'il n'y a strictement rien à montrer — un dossier
        /// sans aucun contact renseigné n'ouvre pas une page vide.
        /// </summary>
        private string BuildAnnexeContactsPage(string patientNomComplet, DossierRestitutionInitial dossier, CoverFields cover)
        {
            JsonDocument? doc = null;
            ContactsSource p;
            try { p = ContactsSource.Lire(_pathService.GetPatientJsonPath(patientNomComplet), out doc); }
            catch { p = ContactsSource.Vide; }

            List<ContactCarte> cartes;
            try { cartes = ConstruireCartes(p, patientNomComplet); }
            finally { doc?.Dispose(); }

            var aIdentifier = ProfessionnelsAIdentifier(dossier);

            if (cartes.Count == 0 && aIdentifier.Count == 0) return "";

            var prenom = string.IsNullOrWhiteSpace(cover.Prenom) ? "" : cover.Prenom.Trim();
            var titre  = prenom.Length > 0 ? $"Autour de {prenom}" : "Autour de l'enfant";

            var pages = RepartirEnPages(cartes, aIdentifier);

            var sb = new StringBuilder();
            sb.AppendLine(BuildContactsCss());
            for (int i = 0; i < pages.Count; i++)
                sb.Append(RendrePage(pages[i], titre, i + 1, pages.Count));
            return sb.ToString();
        }

        private sealed class ContactsPage
        {
            public List<ContactCarte> Cartes { get; } = new();
            public List<string> AIdentifier { get; } = new();
        }

        /// <summary>
        /// Répartit les cartes sur autant de pages qu'il en faut. Sur un dossier ordinaire — deux
        /// parents, l'école, le médecin traitant, deux ou trois intervenants — tout tient sur une
        /// page et il n'y en a qu'une. La coupe n'existe que pour les dossiers très suivis.
        /// </summary>
        private static List<ContactsPage> RepartirEnPages(List<ContactCarte> cartes, List<string> aIdentifier)
        {
            // Hauteur utile d'une A4 une fois retirés l'en-tête, l'intro et le pied de page.
            const int BudgetPremierePage = 760;
            const int BudgetPageSuivante = 880;

            var pages   = new List<ContactsPage>();
            var courante = new ContactsPage();
            var budget  = BudgetPremierePage;
            var utilise = 0;

            // Les cartes sont sur deux colonnes : une rangée coûte la plus haute des deux.
            for (int i = 0; i < cartes.Count; i += 2)
            {
                var rangee = Math.Max(cartes[i].HauteurApprox,
                                      i + 1 < cartes.Count ? cartes[i + 1].HauteurApprox : 0) + 10;

                if (utilise > 0 && utilise + rangee > budget)
                {
                    pages.Add(courante);
                    courante = new ContactsPage();
                    budget   = BudgetPageSuivante;
                    utilise  = 0;
                }

                courante.Cartes.Add(cartes[i]);
                if (i + 1 < cartes.Count) courante.Cartes.Add(cartes[i + 1]);
                utilise += rangee;
            }

            // « Reste à identifier » ferme le bloc contacts : sur la dernière page s'il y tient,
            // sur une page à lui sinon — jamais coupé en deux.
            if (aIdentifier.Count > 0)
            {
                var hauteurTodo = 80 + aIdentifier.Count * 23;
                if (utilise > 0 && utilise + hauteurTodo > budget)
                {
                    pages.Add(courante);
                    courante = new ContactsPage();
                }
                courante.AIdentifier.AddRange(aIdentifier);
            }

            if (courante.Cartes.Count > 0 || courante.AIdentifier.Count > 0) pages.Add(courante);
            return pages;
        }

        private static string RendrePage(ContactsPage page, string titre, int numero, int total)
        {
            var premiere = numero == 1;
            var sb = new StringBuilder();

            sb.AppendLine("<div class='page ac-page'>");

            sb.AppendLine("  <div class='ac-header'>");
            sb.AppendLine("    <div>");
            sb.AppendLine("      <span class='ac-badge'>Annexe — Contacts</span>");
            sb.AppendLine($"      <h1>{WebUtility.HtmlEncode(titre)}{(premiere ? "" : " (suite)")}</h1>");
            sb.AppendLine("      <div class='ac-header-sub'>Les personnes qui accompagnent l'enfant, et comment les joindre</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='ac-header-right'>");
            sb.AppendLine($"      Coordonnées connues au<br>{DateTime.Now:dd/MM/yyyy}");
            if (total > 1) sb.AppendLine($"      <br>Page {numero} / {total}");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <hr class='ac-hr'>");

            if (premiere)
            {
                sb.AppendLine("  <p class='ac-intro'>");
                sb.AppendLine("    Cette page rassemble ce que le dossier sait déjà. Elle est là pour vous éviter de chercher");
                sb.AppendLine("    un numéro dans un vieux courrier le jour où vous en aurez besoin. Si une coordonnée a changé,");
                sb.AppendLine("    dites-le simplement à la consultation suivante — elle sera corrigée dans le dossier.");
                sb.AppendLine("  </p>");
            }

            if (page.Cartes.Count > 0)
            {
                sb.AppendLine("  <div class='ac-grid'>");
                foreach (var carte in page.Cartes) sb.Append(RendreCarte(carte));
                sb.AppendLine("  </div>");
            }

            if (page.AIdentifier.Count > 0) sb.Append(RendreAIdentifier(page.AIdentifier));

            sb.AppendLine("  <div class='ac-footer'>");
            sb.AppendLine("    Coordonnées issues du dossier médical — à usage des parents et des professionnels qui suivent l'enfant.<br>");
            sb.AppendLine("    Approche systémique et développementale Dr Lassoued Nair, Pédopsychiatre");
            sb.AppendLine("  </div>");

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        private List<ContactCarte> ConstruireCartes(ContactsSource p, string patientNomComplet)
        {
            var cartes = new List<ContactCarte>();

            // ── Les parents ────────────────────────────────────────────────
            var mere = new ContactCarte
            {
                Icone  = "👩",
                Titre  = "Mère",
                Detail = $"{p["merePrenom"]} {p["mereNom"]}".Trim()
            };
            mere.Ajouter("Téléphone", p["mereTelephone"]);
            mere.Ajouter("Email",     p["mereEmail"]);
            if (!mere.EstVide) cartes.Add(mere);

            var pere = new ContactCarte
            {
                Icone  = "👨",
                Titre  = "Père",
                Detail = $"{p["perePrenom"]} {p["pereNom"]}".Trim()
            };
            pere.Ajouter("Téléphone", p["pereTelephone"]);
            pere.Ajouter("Email",     p["pereEmail"]);
            if (!pere.EstVide) cartes.Add(pere);

            // L'accompagnant n'apparaît que s'il est quelqu'un d'autre : sur la plupart des
            // dossiers c'est la mère ou le père, et le répéter ferait croire à un tiers.
            var lien = p["accompagnantLien"];
            var estUnParent = lien.IndexOf("mère", StringComparison.OrdinalIgnoreCase) >= 0
                           || lien.IndexOf("mere", StringComparison.OrdinalIgnoreCase) >= 0
                           || lien.IndexOf("père", StringComparison.OrdinalIgnoreCase) >= 0
                           || lien.IndexOf("pere", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!estUnParent)
            {
                var acc = new ContactCarte
                {
                    Icone  = "🤝",
                    Titre  = lien.Length > 0 ? lien : "Accompagnant",
                    Detail = $"{p["accompagnantPrenom"]} {p["accompagnantNom"]}".Trim()
                };
                acc.Ajouter("Téléphone", p["accompagnantTelephone"]);
                acc.Ajouter("Email",     p["accompagnantEmail"]);
                if (!acc.EstVide) cartes.Add(acc);
            }

            // ── L'école ────────────────────────────────────────────────────
            var ecole = new ContactCarte
            {
                Icone  = "🏫",
                Titre  = "École",
                Detail = p["ecole"]
            };
            var classe = p["classe"];
            if (classe.Length > 0) ecole.Ajouter("Classe", classe);
            ecole.Ajouter("Adresse",   JoindreAdresse(p["ecoleAdresse"], p["ecoleCodePostal"], p["ecoleCommune"]));
            ecole.Ajouter("Téléphone", p["ecoleTelephone"]);
            ecole.Ajouter("Email",     p["ecoleEmail"]);
            if (!ecole.EstVide) cartes.Add(ecole);

            // ── Le médecin traitant ────────────────────────────────────────
            var mt = new ContactCarte
            {
                Icone  = "🩺",
                Titre  = "Médecin traitant",
                Detail = AvecTitreDocteur($"{p["medecinTraitantPrenom"]} {p["medecinTraitantNom"]}".Trim())
            };
            mt.Ajouter("Adresse",   JoindreAdresse(p["medecinTraitantAdresse"], p["medecinTraitantCodePostal"], p["medecinTraitantVille"]));
            mt.Ajouter("Téléphone", p["medecinTraitantTelephone"]);
            if (!mt.EstVide) cartes.Add(mt);

            var ref_ = new ContactCarte
            {
                Icone  = "🏥",
                Titre  = "Médecin référent",
                Detail = $"{p["medecinReferentPrenom"]} {p["medecinReferentNom"]}".Trim()
            };
            ref_.Ajouter("Spécialité", p["medecinReferentSpecialite"]);
            if (!ref_.EstVide) cartes.Add(ref_);

            // ── Les intervenants du dossier bleu ───────────────────────────
            foreach (var i in ChargerIntervenants(patientNomComplet))
            {
                var carte = new ContactCarte
                {
                    Icone  = "📋",
                    Titre  = string.IsNullOrWhiteSpace(i.Profession) ? "Intervenant" : i.Profession!.Trim(),
                    Detail = i.Nom.Trim()
                };
                carte.Ajouter("Adresse",   JoindreAdresse(i.Adresse, i.CodePostal, i.Ville));
                carte.Ajouter("Téléphone", i.Telephone);
                carte.Ajouter("Email",     i.Email);
                if (!string.IsNullOrWhiteSpace(i.SourceDocument))
                    carte.Ajouter("Bilan",  NomLisibleDuDocument(i.SourceDocument!));
                if (!carte.EstVide) cartes.Add(carte);
            }

            return cartes;
        }

        private List<Models.Intervenant> ChargerIntervenants(string patientNomComplet)
        {
            try
            {
                var dir = _pathService.GetInfoPatientDirectory(patientNomComplet);
                return new IntervenantService().Load(dir)
                    .Where(i => !string.IsNullOrWhiteSpace(i.Nom))
                    .ToList();
            }
            catch { return new List<Models.Intervenant>(); }
        }

        /// <summary>
        /// « 2025-03-12_bilan_orthophonique.pdf » → « bilan orthophonique ». Le nom de fichier
        /// brut n'a rien à faire sous les yeux des parents.
        /// </summary>
        private static string NomLisibleDuDocument(string fichier)
        {
            var nom = Path.GetFileNameWithoutExtension(fichier) ?? "";
            nom = nom.Replace('_', ' ').Replace('-', ' ');

            var mots = nom.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Where(m => !m.All(char.IsDigit))   // retire les fragments de date
                          .ToArray();

            return mots.Length == 0 ? "" : string.Join(" ", mots);
        }

        private static string RendreCarte(ContactCarte c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("    <div class='ac-card'>");
            sb.AppendLine($"      <div class='ac-card-head'><span class='ac-ico'>{c.Icone}</span>{WebUtility.HtmlEncode(c.Titre)}</div>");
            if (!string.IsNullOrWhiteSpace(c.Detail))
                sb.AppendLine($"      <div class='ac-card-name'>{WebUtility.HtmlEncode(c.Detail)}</div>");
            if (c.Lignes.Count > 0)
            {
                sb.AppendLine("      <dl class='ac-lines'>");
                foreach (var l in c.Lignes)
                {
                    sb.AppendLine($"        <dt>{WebUtility.HtmlEncode(l.Libelle)}</dt>");
                    sb.AppendLine($"        <dd>{WebUtility.HtmlEncode(l.Valeur)}</dd>");
                }
                sb.AppendLine("      </dl>");
            }
            sb.AppendLine("    </div>");
            return sb.ToString();
        }

        /// <summary>
        /// Les professionnels qu'il reste à trouver, avec une ligne à remplir à la main.
        /// C'est ce qui rend la page utile les semaines d'après : le parent écrit le nom
        /// de l'orthophoniste trouvé là où il le cherchera.
        /// </summary>
        private static string RendreAIdentifier(List<string> actions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("  <div class='ac-todo'>");
            sb.AppendLine("    <div class='ac-todo-title'>Reste à identifier</div>");
            sb.AppendLine("    <p class='ac-todo-intro'>Ces professionnels ne sont pas encore trouvés. C'est souvent ce qui prend le plus de temps — notez leurs coordonnées ici dès que le rendez-vous est pris.</p>");
            foreach (var a in actions)
            {
                sb.AppendLine("    <div class='ac-todo-row'>");
                sb.AppendLine($"      <div class='ac-todo-quoi'>{WebUtility.HtmlEncode(a)}</div>");
                sb.AppendLine("      <div class='ac-todo-line'></div>");
                sb.AppendLine("    </div>");
            }
            sb.AppendLine("  </div>");
            return sb.ToString();
        }

        private static string BuildContactsCss() => @"<style>
.ac-page {
  padding: 26px 40px 36px 40px;
  font-family: 'Segoe UI', Arial, sans-serif;
  font-size: 12px;
  color: #112240;
}
.ac-badge {
  display: inline-block;
  background: #1A3A6A;
  color: white;
  font-size: 8.5px;
  font-weight: 700;
  letter-spacing: 0.8px;
  text-transform: uppercase;
  padding: 2px 8px;
  border-radius: 10px;
  margin-bottom: 4px;
}
.ac-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 6px; }
.ac-header h1 { font-size: 21px; font-weight: 800; color: #112240; margin: 2px 0; line-height: 1.15; }
.ac-header-sub { font-size: 10.5px; color: #64748B; font-style: italic; }
.ac-header-right { text-align: right; font-size: 9.5px; color: #94A3B8; line-height: 1.6; }
.ac-hr { border: none; border-top: 2px solid #00A896; margin: 6px 0 12px 0; }
.ac-intro { font-size: 11px; color: #334155; line-height: 1.55; margin: 0 0 14px 0; }

.ac-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
.ac-card {
  background: #F8FAFC;
  border: 1px solid #E2E8F0;
  border-left: 3px solid #00A896;
  border-radius: 6px;
  padding: 9px 12px 10px 12px;
  break-inside: avoid;
}
.ac-card-head {
  font-size: 9.5px;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #1A3A6A;
  margin-bottom: 3px;
}
.ac-ico { margin-right: 5px; font-size: 11px; }
.ac-card-name { font-size: 13px; font-weight: 700; color: #112240; margin-bottom: 5px; line-height: 1.25; }
.ac-lines { display: grid; grid-template-columns: auto 1fr; gap: 2px 10px; margin: 0; }
.ac-lines dt {
  font-size: 9px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.3px;
  color: #94A3B8;
  padding-top: 1px;
}
.ac-lines dd { margin: 0; font-size: 11px; color: #334155; line-height: 1.4; word-break: break-word; }

.ac-todo {
  margin-top: 16px;
  background: #FEF9E7;
  border: 1px solid #F1C40F;
  border-left: 4px solid #F1C40F;
  border-radius: 6px;
  padding: 10px 14px 12px 14px;
}
.ac-todo-title {
  font-size: 10.5px;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #7D6608;
  margin-bottom: 3px;
}
.ac-todo-intro { font-size: 10.5px; color: #7D6608; line-height: 1.5; margin: 0 0 8px 0; }
.ac-todo-row { display: flex; align-items: baseline; gap: 10px; margin-bottom: 9px; }
.ac-todo-quoi { font-size: 11px; font-weight: 700; color: #112240; flex: 0 0 42%; line-height: 1.35; }
.ac-todo-line { flex: 1; border-bottom: 1px dotted #B7950B; height: 14px; }

.ac-footer {
  position: absolute;
  bottom: 18px;
  left: 40px;
  right: 40px;
  text-align: center;
  font-size: 8.5px;
  color: #94A3B8;
  line-height: 1.5;
  border-top: 1px solid #E8EDF3;
  padding-top: 6px;
}
</style>";
    }
}

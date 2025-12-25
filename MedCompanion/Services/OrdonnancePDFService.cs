using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MedCompanion.Models;

namespace MedCompanion.Services;

/// <summary>
/// Service de génération de PDF d'ordonnances avec format professionnel
/// </summary>
public class OrdonnancePDFService
{
    private readonly AppSettings _settings;

    public OrdonnancePDFService()
    {
        _settings = AppSettings.Load();

        // Configuration QuestPDF (licence communautaire)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Génère un PDF d'ordonnance de médicaments au format professionnel
    /// </summary>
    /// <param name="ordonnance">Ordonnance à générer</param>
    /// <param name="patient">Métadonnées du patient</param>
    /// <param name="outputPath">Chemin de sortie du PDF</param>
    /// <returns>True si succès, False sinon</returns>
    public bool GenerateOrdonnanceMedicamentsPDF(
        OrdonnanceMedicaments ordonnance,
        PatientMetadata patient,
        string outputPath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[PDF] Début génération PDF: {outputPath}");
            System.Diagnostics.Debug.WriteLine($"[PDF] BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.PageColor(Colors.White);

                    // En-tête avec informations du médecin
                    page.Header().Element(c => Header(c, ordonnance.DateCreation));

                    // Contenu principal
                    page.Content().Element(container => Content(container, ordonnance, patient));

                    // Pied de page
                    page.Footer().Element(Footer);
                });
            });

            System.Diagnostics.Debug.WriteLine($"[PDF] Document créé, génération en cours...");
            document.GeneratePdf(outputPath);
            System.Diagnostics.Debug.WriteLine($"[PDF] ✅ PDF généré avec succès: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PDF] ❌ Erreur génération PDF ordonnance: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[PDF] StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// En-tête du document avec informations du médecin et codes-barres
    /// </summary>
    private void Header(IContainer container, DateTime dateCreation)
    {
        container.Row(row =>
        {
            // Colonne gauche : Informations du médecin
            row.RelativeItem(6).Column(column =>
            {
                column.Item().Text("Dr Nair LASSOUED")
                    .FontSize(14)
                    .Bold();

                column.Item().Text("PSYCHIATRE DE L'ENFANT ET DE")
                    .FontSize(9);

                column.Item().Text("L'ADOLESCENT")
                    .FontSize(9);

                column.Item().PaddingTop(8).Text("390 Avenue de la Première Dfl")
                    .FontSize(9);

                column.Item().Text("83220 Le Pradet")
                    .FontSize(9);

                column.Item().PaddingTop(4).Text("Tél: 07 52 75 87 32")
                    .FontSize(9);
            });

            // Colonne droite : Codes-barres (images réelles)
            row.RelativeItem(4).Column(column =>
            {
                column.Item().AlignRight().Row(codeRow =>
                {
                    // Code-barre AM (texte temporaire)
                    codeRow.AutoItem().Column(c =>
                    {
                        c.Item().Text("N° AM :")
                            .FontSize(8);
                        c.Item().Text("801018791")
                            .FontSize(10)
                            .Bold();
                    });

                    // Code-barre RPPS (texte temporaire)
                    codeRow.AutoItem().PaddingLeft(10).Column(c =>
                    {
                        c.Item().Text("N° RPPS :")
                            .FontSize(8);
                        c.Item().Text(_settings.Rpps ?? "")
                            .FontSize(10)
                            .Bold();
                    });
                });

                // Date en bas à droite
                column.Item().PaddingTop(15).AlignRight()
                    .Text($"Le {dateCreation:dd MMMM yyyy}")
                    .FontSize(9);
            });
        });
    }

    /// <summary>
    /// Contenu principal de l'ordonnance
    /// </summary>
    private void Content(IContainer container, OrdonnanceMedicaments ordonnance, PatientMetadata patient)
    {
        container.PaddingTop(20).Column(column =>
        {
            // Informations patient (méthode uniforme)
            column.Item().Element(c => PatientInfoSection(c, patient));

            // Liste des médicaments
            column.Item().PaddingTop(20).Column(medColumn =>
            {
                foreach (var medicament in ordonnance.Medicaments)
                {
                    medColumn.Item().PaddingBottom(15).Column(medItem =>
                    {
                        // Nom du médicament en majuscules et gras
                        medItem.Item().Text(medicament.Medicament.Denomination?.ToUpper() ?? "")
                            .FontSize(11)
                            .Bold();

                        // Posologie
                        if (!string.IsNullOrEmpty(medicament.Posologie))
                        {
                            medItem.Item().PaddingTop(3).Text(medicament.Posologie)
                                .FontSize(10);
                        }

                        // Ligne horizontale avant la durée
                        if (!string.IsNullOrEmpty(medicament.Duree))
                        {
                            medItem.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Black);
                        }

                        // Durée
                        if (!string.IsNullOrEmpty(medicament.Duree))
                        {
                            medItem.Item().PaddingTop(5).Text($"Quantité suffisante pour {medicament.Duree}")
                                .FontSize(10);
                        }
                    });
                }
            });

            // Zone de signature (méthode uniforme)
            column.Item().PaddingTop(30).Element(c => SignatureBox(c, ordonnance.DateCreation));
        });
    }

    /// <summary>
    /// Pied de page avec mentions légales
    /// </summary>
    private void Footer(IContainer container)
    {
        container.AlignBottom().Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            column.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text(
                    "Membre d'une association de gestion agréée. Le règlement des honoraires par chèque ou carte bancaire est accepté. " +
                    "En cas d'urgence, contacter le 15.")
                    .FontSize(7)
                    .LineHeight(1.3f);

                row.AutoItem().AlignRight().Border(1).BorderColor(Colors.Black)
                    .Padding(8)
                    .Text("1").FontSize(12).Bold();
            });
        });
    }

    /// <summary>
    /// Calcule l'âge à partir de la date de naissance
    /// </summary>
    private int CalculateAge(DateTime birthDate)
    {
        var today = DateTime.Today;
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>
    /// Section d'informations patient uniforme pour toutes les ordonnances
    /// </summary>
    private void PatientInfoSection(IContainer container, PatientMetadata patient)
    {
        container.Column(column =>
        {
            // Informations patient
            column.Item().AlignLeft().Text(text =>
            {
                var sexe = patient.Sexe?.ToUpper() == "M" ? "M." :
                           patient.Sexe?.ToUpper() == "F" ? "Mme" : "";

                text.Span($"{sexe} {patient.Nom?.ToUpper()} {patient.Prenom}, ")
                    .FontSize(11);
                text.Span($"né(e) {patient.Nom?.ToUpper()} {patient.Prenom}")
                    .FontSize(11);
            });

            // Date de naissance et âge
            var ageText = $"Né(e) le {patient.DobFormatted}";
            if (!string.IsNullOrEmpty(patient.Dob) && DateTime.TryParse(patient.Dob, out var dobDate))
            {
                var age = CalculateAge(dobDate);
                ageText += $" ({age} ans";

                // Ajouter les mois pour les enfants de moins de 3 ans
                if (age < 3)
                {
                    var months = ((DateTime.Today.Year - dobDate.Year) * 12) + DateTime.Today.Month - dobDate.Month;
                    if (DateTime.Today.Day < dobDate.Day) months--;
                    var remainingMonths = months % 12;
                    if (remainingMonths > 0)
                    {
                        ageText += $" {remainingMonths} mois";
                    }
                }
                ageText += ")";
            }
            column.Item().AlignLeft().Text(ageText).FontSize(10);

            column.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    /// <summary>
    /// Cartouche de signature uniforme pour toutes les ordonnances
    /// </summary>
    private void SignatureBox(IContainer container, DateTime dateCreation)
    {
        container.AlignRight().Width(150).Column(signatureColumn =>
        {
            // Date de création
            signatureColumn.Item().AlignCenter().Text($"Le {dateCreation:dd/MM/yyyy}")
                .FontSize(10)
                .Bold();

            // Signature texte simple
            signatureColumn.Item().PaddingTop(10).AlignCenter().Text("Dr Nair LASSOUED")
                .FontSize(11)
                .Bold();

            signatureColumn.Item().AlignCenter().Text("Psychiatre de l'enfant et de l'adolescent")
                .FontSize(8);
        });
    }

    /// <summary>
    /// Génère un PDF d'ordonnance biologique
    /// </summary>
    public bool GenerateOrdonnanceBiologiePDF(
        OrdonnanceBiologie ordonnance,
        PatientMetadata patient,
        string outputPath)
    {
        try
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.PageColor(Colors.White);

                    page.Header().Element(c => Header(c, ordonnance.DateCreation));
                    page.Content().Element(container => ContentBiologie(container, ordonnance, patient));
                    page.Footer().Element(Footer);
                });
            });

            document.GeneratePdf(outputPath);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur génération PDF ordonnance biologie: {ex.Message}");
            return false;
        }
    }

    private void ContentBiologie(IContainer container, OrdonnanceBiologie ordonnance, PatientMetadata patient)
    {
        container.PaddingTop(20).Column(column =>
        {
            // Informations patient (méthode uniforme)
            column.Item().Element(c => PatientInfoSection(c, patient));

            // Titre
            column.Item().PaddingTop(20).Text("ORDONNANCE DE BIOLOGIE")
                .FontSize(14)
                .Bold();

            // Note additionnelle EN HAUT si présente (ex: "Bilan à jeun")
            if (!string.IsNullOrWhiteSpace(ordonnance.Note))
            {
                column.Item().PaddingTop(15).Text(ordonnance.Note)
                    .FontSize(11)
                    .Bold();
            }

            // Type de bilan
            column.Item().PaddingTop(15).Text($"Type de bilan : {ordonnance.PresetNom}")
                .FontSize(11)
                .Bold();

            // Examens demandés
            column.Item().PaddingTop(15).Column(examensColumn =>
            {
                examensColumn.Item().Text("Examens prescrits :")
                    .FontSize(11)
                    .Bold();

                examensColumn.Item().PaddingTop(8).Column(listColumn =>
                {
                    foreach (var examen in ordonnance.ExamensCoches)
                    {
                        listColumn.Item().PaddingVertical(2).Text($"• {examen.Nom}")
                            .FontSize(10);
                    }
                });
            });

            // Zone de signature (méthode uniforme)
            column.Item().PaddingTop(30).Element(c => SignatureBox(c, ordonnance.DateCreation));
        });
    }

    /// <summary>
    /// Génère un PDF d'ordonnance IDE (soins infirmiers à domicile)
    /// </summary>
    public bool GenerateOrdonnanceIDEPDF(
        OrdonnanceIDE ordonnance,
        PatientMetadata patient,
        string outputPath)
    {
        try
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.PageColor(Colors.White);

                    page.Header().Element(c => Header(c, ordonnance.DateCreation));
                    page.Content().Element(container => ContentIDE(container, ordonnance, patient));
                    page.Footer().Element(Footer);
                });
            });

            document.GeneratePdf(outputPath);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur génération PDF ordonnance IDE: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Contenu pour l'ordonnance IDE avec format uniforme
    /// </summary>
    private void ContentIDE(IContainer container, OrdonnanceIDE ordonnance, PatientMetadata patient)
    {
        container.PaddingTop(20).Column(column =>
        {
            // Informations patient (méthode uniforme)
            column.Item().Element(c => PatientInfoSection(c, patient));

            // Titre
            column.Item().PaddingTop(20).Text("ORDONNANCE DE SOINS INFIRMIERS À DOMICILE")
                .FontSize(14)
                .Bold();

            // Objet
            column.Item().PaddingTop(15).Text("Objet : Prescription de soins infirmiers à domicile – Administration de traitements et surveillance hémodynamique.")
                .FontSize(11);

            // Corps principal
            column.Item().PaddingTop(15).Text("Je soussigné(e), médecin prescripteur, demande la mise en place des soins infirmiers suivants :")
                .FontSize(11);

            // Soins prescrits (format liste à puces)
            column.Item().PaddingTop(15).Column(soinsColumn =>
            {
                // Parser les soins prescrits pour créer des listes à puces
                var soinsText = ordonnance.SoinsPrescrits.Trim();
                var soinsLines = new List<string>();

                // D'abord essayer de séparer par retours à la ligne
                var linesSplit = soinsText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in linesSplit)
                {
                    var trimmedLine = line.Trim().TrimStart('•', '-', '*').Trim();
                    if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

                    // Si la ligne est très longue (>200 caractères), essayer de la séparer intelligemment
                    if (trimmedLine.Length > 200)
                    {
                        // Séparer par des mots-clés courants dans les prescriptions IDE
                        var keywords = new[] {
                            ". Fréquence", ". Surveillance", ". Observation", ". Transmission",
                            ". Coordination", ". En cas"
                        };

                        // Trouver tous les indices de mots-clés
                        var splitIndices = new List<int> { 0 };

                        foreach (var keyword in keywords)
                        {
                            int index = 0;
                            while ((index = trimmedLine.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) != -1)
                            {
                                splitIndices.Add(index + 1); // +1 pour garder le point avec le segment précédent
                                index += keyword.Length;
                            }
                        }

                        splitIndices.Add(trimmedLine.Length);
                        splitIndices = splitIndices.Distinct().OrderBy(x => x).ToList();

                        // Découper le texte en segments
                        for (int i = 0; i < splitIndices.Count - 1; i++)
                        {
                            var startIndex = splitIndices[i];
                            var length = splitIndices[i + 1] - startIndex;
                            var segment = trimmedLine.Substring(startIndex, length).Trim();

                            if (!string.IsNullOrWhiteSpace(segment))
                            {
                                soinsLines.Add(segment);
                            }
                        }
                    }
                    else
                    {
                        // Ligne courte, l'ajouter telle quelle
                        soinsLines.Add(trimmedLine);
                    }
                }

                // Afficher les lignes avec des puces et un bon espacement
                foreach (var soin in soinsLines)
                {
                    if (!string.IsNullOrWhiteSpace(soin))
                    {
                        soinsColumn.Item().PaddingVertical(3).Text($"• {soin}")
                            .FontSize(10)
                            .LineHeight(1.4f);
                    }
                }
            });

            // Séparateur
            column.Item().PaddingTop(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            // Durée
            column.Item().PaddingTop(15).Text(text =>
            {
                text.Span("Durée : ").Bold().FontSize(11);
                text.Span(ordonnance.Duree).FontSize(11);

                if (!string.IsNullOrEmpty(ordonnance.Renouvelable))
                {
                    text.Span($", renouvelable {ordonnance.Renouvelable}").FontSize(11);
                }
            });

            // Documents joints
            column.Item().PaddingTop(8).Text("Documents joints : copie de l'ordonnance médicamenteuse en cours et coordonnées du médecin prescripteur.")
                .FontSize(10);

            // Zone de signature (méthode uniforme)
            column.Item().PaddingTop(30).Element(c => SignatureBox(c, ordonnance.DateCreation));
        });
    }
}

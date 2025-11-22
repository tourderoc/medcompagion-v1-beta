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
        _settings = new AppSettings();

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

            document.GeneratePdf(outputPath);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur génération PDF ordonnance: {ex.Message}");
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

            // Colonne droite : Codes-barres (simulation avec texte)
            row.RelativeItem(4).Column(column =>
            {
                column.Item().AlignRight().Row(codeRow =>
                {
                    codeRow.AutoItem().Column(c =>
                    {
                        c.Item().Text("N° AM :")
                            .FontSize(8);
                        c.Item().Border(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4)
                            .Text("|| || ||| || ||")
                            .FontFamily("Courier New")
                            .FontSize(10);
                        c.Item().AlignCenter().Text("") // NumeroAM non configuré
                            .FontSize(7);
                    });

                    codeRow.AutoItem().PaddingLeft(10).Column(c =>
                    {
                        c.Item().Text("N° RPPS :")
                            .FontSize(8);
                        c.Item().Border(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4)
                            .Text("||| || || |||")
                            .FontFamily("Courier New")
                            .FontSize(10);
                        c.Item().AlignCenter().Text(_settings.Rpps ?? "")
                            .FontSize(7);
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
            // Informations patient
            column.Item().AlignRight().Text(text =>
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
                ageText += $" ({age} ans)";
            }
            column.Item().AlignRight().Text(ageText).FontSize(10);

            column.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

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

            // Zone de signature
            column.Item().PaddingTop(30).AlignRight().Width(200).Column(signatureColumn =>
            {
                signatureColumn.Item()
                    .Border(2)
                    .BorderColor("#4A90E2") // Bleu
                    .Padding(15)
                    .MinHeight(60)
                    .AlignMiddle()
                    .AlignCenter()
                    .Text("Nair LASSOUED")
                    .FontSize(11)
                    .Italic();
            });
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
            // Informations patient
            column.Item().AlignRight().Text(text =>
            {
                var sexe = patient.Sexe?.ToUpper() == "M" ? "M." :
                           patient.Sexe?.ToUpper() == "F" ? "Mme" : "";

                text.Span($"{sexe} {patient.Nom?.ToUpper()} {patient.Prenom}, ")
                    .FontSize(11);
                text.Span($"né(e) {patient.Nom?.ToUpper()} {patient.Prenom}")
                    .FontSize(11);
            });

            var ageText2 = $"Né(e) le {patient.DobFormatted}";
            if (!string.IsNullOrEmpty(patient.Dob) && DateTime.TryParse(patient.Dob, out var dobDate2))
            {
                var age2 = CalculateAge(dobDate2);
                ageText2 += $" ({age2} ans)";
            }
            column.Item().AlignRight().Text(ageText2).FontSize(10);

            column.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

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

            // Zone de signature
            column.Item().PaddingTop(30).AlignRight().Width(250).Column(signatureColumn =>
            {
                signatureColumn.Item()
                    .Border(2)
                    .BorderColor("#4A90E2")
                    .Padding(15)
                    .MinHeight(60)
                    .AlignMiddle()
                    .AlignCenter()
                    .Text("Nair LASSOUED")
                    .FontSize(11)
                    .Italic();
            });
        });
    }
}

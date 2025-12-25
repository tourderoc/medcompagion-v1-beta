using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de conversion directe Markdown vers PDF professionnel pour ordonnances IA
    /// </summary>
    public class MarkdownToPdfService
    {
        public MarkdownToPdfService()
        {
            // Configuration QuestPDF (licence communautaire)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>
        /// Convertit directement du Markdown en PDF professionnel
        /// </summary>
        public async Task<(bool success, string? pdfPath, string? error)> ConvertMarkdownToPdfAsync(
            string markdown,
            string outputPath,
            PatientMetadata? patientMetadata = null,
            OrdonnanceType type = OrdonnanceType.Medicaments
        )
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownToPdfService] Début conversion Markdown→PDF: {outputPath}");

                var document = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1.5f, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                        CreateOrdonnanceContent(page, markdown, patientMetadata, type);
                    });
                });

                document.GeneratePdf(outputPath);

                System.Diagnostics.Debug.WriteLine($"[MarkdownToPdfService] ✅ PDF généré avec succès: {outputPath}");
                return (true, outputPath, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownToPdfService] ❌ Erreur conversion: {ex.Message}");
                return (false, null, $"Erreur lors de la conversion Markdown→PDF: {ex.Message}");
            }
        }

        private static void CreateOrdonnanceContent(
            PageDescriptor page, 
            string markdown, 
            PatientMetadata? patientMetadata, 
            OrdonnanceType type)
        {
            page.Header().Element(container => AddHeader(container, patientMetadata));

            page.Content().PaddingVertical(1, Unit.Centimetre).Column(column =>
            {
                var title = ExtractTitleFromMarkdown(markdown);
                if (!string.IsNullOrEmpty(title))
                {
                    column.Item().AlignCenter().Text(title).FontSize(14).Bold();
                    column.Item().PaddingBottom(10);
                }

                AddMarkdownContent(column, markdown);

                column.Item().PaddingTop(20).Element(container => AddSignature(container, patientMetadata));
            });

            page.Footer().Element(container => AddFooter(container, patientMetadata));
        }

        private static void AddHeader(IContainer container, PatientMetadata? patientMetadata)
        {
            var settings = AppSettings.Load();
            
            container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignCenter().Height(40).Element(TryAddLogo);
                });

                row.RelativeItem().AlignRight().Column(col =>
                {
                    col.Item().Text(settings.Medecin ?? "Médecin").FontSize(10).Bold();
                    col.Item().Text("Pédopsychiatre").FontSize(9);
                    col.Item().Text($"RPPS : {settings.Rpps}").FontSize(8);
                    col.Item().Text($"Tél : {settings.Telephone}").FontSize(8);
                    col.Item().Text(settings.Email).FontSize(8);
                });
            });
        }

        private static void TryAddLogo(IContainer container)
        {
            try
            {
                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.png");
                if (File.Exists(logoPath))
                {
                    container.Image(logoPath, ImageScaling.FitArea);
                }
                else
                {
                    container.AlignCenter().Text("🦋").FontSize(20);
                }
            }
            catch
            {
                container.AlignCenter().Text("🦋").FontSize(20);
            }
        }

        private static void AddSignature(IContainer container, PatientMetadata? patientMetadata)
        {
            var settings = AppSettings.Load();

            container.AlignRight().Column(col =>
            {
                col.Item().Text(settings.Medecin ?? "Médecin").FontSize(12).Bold();
                col.Item().Text("Pédopsychiatre").FontSize(10);
                col.Item().Text($"Fait au {settings.Ville}, le {DateTime.Now:dd/MM/yyyy}").FontSize(10);
            });
        }

        private static void AddFooter(IContainer container, PatientMetadata? patientMetadata)
        {
            var settings = AppSettings.Load();
            container.AlignCenter().Text(settings.Adresse ?? "").FontSize(8).FontColor(Colors.Grey.Medium);
        }

        private static void AddMarkdownContent(ColumnDescriptor column, string markdown)
        {
            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    column.Item().PaddingBottom(5);
                    continue;
                }

                if (line.StartsWith("# "))
                {
                    column.Item().Text(line.Substring(2).Trim()).FontSize(16).Bold();
                }
                else if (line.StartsWith("## "))
                {
                    column.Item().Text(line.Substring(3).Trim()).FontSize(12).Bold();
                }
                else
                {
                    var text = line.StartsWith("- ") ? line : line.Trim();
                    column.Item().Text(text);
                }
            }
        }

        private static string ExtractTitleFromMarkdown(string markdown)
        {
            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.StartsWith("# ") && !string.IsNullOrWhiteSpace(line.Substring(2).Trim()))
                    return line.Substring(2).Trim();
            }
            return "Ordonnance";
        }
    }

    public enum OrdonnanceType
    {
        Medicaments,
        Biologie,
        IDE,
        SoinsInfirmiers
    }
}

using System;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MedCompanion.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de génération de documents DOCX professionnels pour les ordonnances
    /// </summary>
    public class OrdonnanceDocxService
    {
        private readonly AppSettings _settings;

        public OrdonnanceDocxService()
        {
            _settings = AppSettings.Load();
        }

        /// <summary>
        /// Génère un fichier DOCX pour une ordonnance de médicaments
        /// </summary>
        public bool GenerateOrdonnanceMedicamentsDocx(
            OrdonnanceMedicaments ordonnance,
            PatientMetadata patient,
            string outputPath)
        {
            try
            {
                // Créer le document Word
                using (WordprocessingDocument wordDocument = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document))
                {
                    // Ajouter la partie principale du document
                    MainDocumentPart mainPart = wordDocument.AddMainDocumentPart();
                    mainPart.Document = new Document();
                    Body body = new Body();

                    // En-tête avec infos médecin et codes-barres
                    AddHeader(body, mainPart, ordonnance.DateCreation);

                    // Espacement
                    body.AppendChild(new Paragraph(new Run(new Text(""))));

                    // Informations patient
                    AddPatientInfo(body, patient);

                    // Espacement
                    body.AppendChild(new Paragraph(new Run(new Text(""))));

                    // Liste des médicaments
                    AddMedicaments(body, ordonnance);

                    // Espacement
                    body.AppendChild(new Paragraph(new Run(new Text(""))));
                    body.AppendChild(new Paragraph(new Run(new Text(""))));

                    // Signature (nom du docteur + date + image de cartouche)
                    AddSignature(body, ordonnance.DateCreation);
                    AddSignatureImage(wordDocument, body);

                    // Configurer les marges de page (réduire les marges)
                    SectionProperties sectionProps = new SectionProperties();
                    PageMargin pageMargin = new PageMargin()
                    {
                        Top = 400,      // Réduit (était ~1440 par défaut = 1 inch)
                        Right = 720,    // Réduit à 0.5 inch
                        Bottom = 1440,  // 1 inch
                        Left = 720,     // Réduit à 0.5 inch
                        Header = 720,
                        Footer = 720,
                        Gutter = 0
                    };
                    sectionProps.Append(pageMargin);
                    body.Append(sectionProps);

                    mainPart.Document.Append(body);
                    mainPart.Document.Save();
                }

                System.Diagnostics.Debug.WriteLine($"[OrdonnanceDocxService] DOCX créé: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrdonnanceDocxService] Erreur: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ajoute l'en-tête avec infos médecin et codes-barres (3 colonnes)
        /// </summary>
        private void AddHeader(Body body, MainDocumentPart mainPart, DateTime dateCreation)
        {
            // Créer un tableau pour l'en-tête (3 colonnes)
            Table table = new Table();

            // Propriétés du tableau
            TableProperties tblProp = new TableProperties(
                new TableWidth() { Width = "5000", Type = TableWidthUnitValues.Pct }
            );
            table.AppendChild(tblProp);

            TableRow row = new TableRow();

            // Colonne 1 (large): Infos médecin
            TableCell infoCell = new TableCell();

            // Nom du médecin
            infoCell.Append(new Paragraph(new Run(new Text("Dr Nair LASSOUED") { Space = SpaceProcessingModeValues.Preserve }) { RunProperties = new RunProperties(new Bold(), new FontSize() { Val = "24" }) }));

            // Spécialité en majuscules
            infoCell.Append(new Paragraph(new Run(new Text("PSYCHIATRE DE L'ENFANT ET DE L'ADOLESCENT"))));

            // Adresse ligne 1
            infoCell.Append(new Paragraph(new Run(new Text("390 Avenue de la Première Dfl"))));

            // Adresse ligne 2: Code postal + Ville
            infoCell.Append(new Paragraph(new Run(new Text("83220 Le Pradet"))));

            // Téléphone avec espaces
            infoCell.Append(new Paragraph(new Run(new Text("Tel: 07 52 75 87 32"))));

            // Colonne 2 (petite): Code-barre AM - aligné à gauche
            TableCell amCell = new TableCell();

            // N° AM aligné à gauche
            var amPara = new Paragraph(new ParagraphProperties(new Justification() { Val = JustificationValues.Left }));
            amPara.Append(new Run(new Text("N° AM :")) { RunProperties = new RunProperties(new FontSize() { Val = "16" }) });
            amCell.Append(amPara);

            // Image code-barre AM (aligné à gauche)
            var barcodeAmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "barcode_am.png");
            if (File.Exists(barcodeAmPath))
            {
                var imgPara = new Paragraph(new ParagraphProperties(new Justification() { Val = JustificationValues.Left }));
                AddImageToParagraph(imgPara, mainPart, barcodeAmPath, 150, 75);
                amCell.Append(imgPara);
            }
            else
            {
                amCell.Append(new Paragraph(new Run(new Text("831018791")) { RunProperties = new RunProperties(new Bold()) }));
            }

            // Colonne 3 (petite): Code-barre RPPS - aligné à gauche
            TableCell rppsCell = new TableCell();

            // N° RPPS aligné à gauche
            var rppsPara = new Paragraph(new ParagraphProperties(new Justification() { Val = JustificationValues.Left }));
            rppsPara.Append(new Run(new Text("N° RPPS :")) { RunProperties = new RunProperties(new FontSize() { Val = "16" }) });
            rppsCell.Append(rppsPara);

            // Image code-barre RPPS (aligné à gauche)
            var barcodeRppsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "barcode_rpps.png");
            if (File.Exists(barcodeRppsPath))
            {
                var imgPara = new Paragraph(new ParagraphProperties(new Justification() { Val = JustificationValues.Left }));
                AddImageToParagraph(imgPara, mainPart, barcodeRppsPath, 150, 75);
                rppsCell.Append(imgPara);
            }
            else
            {
                rppsCell.Append(new Paragraph(new Run(new Text(_settings.Rpps)) { RunProperties = new RunProperties(new Bold()) }));
            }

            // Propriétés des cellules (largeurs + alignement vertical en haut)
            infoCell.Append(new TableCellProperties(
                new TableCellWidth() { Type = TableWidthUnitValues.Pct, Width = "2500" },
                new TableCellVerticalAlignment() { Val = TableVerticalAlignmentValues.Top }
            )); // 50%
            amCell.Append(new TableCellProperties(
                new TableCellWidth() { Type = TableWidthUnitValues.Pct, Width = "1250" },
                new TableCellVerticalAlignment() { Val = TableVerticalAlignmentValues.Top }
            ));   // 25%
            rppsCell.Append(new TableCellProperties(
                new TableCellWidth() { Type = TableWidthUnitValues.Pct, Width = "1250" },
                new TableCellVerticalAlignment() { Val = TableVerticalAlignmentValues.Top }
            )); // 25%

            row.Append(infoCell, amCell, rppsCell);
            table.Append(row);

            body.AppendChild(table);

            // Date en dessous du tableau
            body.AppendChild(new Paragraph(new Run(new Text("")))); // Espacement
            var datePara = new Paragraph(new ParagraphProperties(new Justification() { Val = JustificationValues.Right }));
            datePara.Append(new Run(new Text(FormatDateAbrege(dateCreation))));
            body.AppendChild(datePara);
        }

        /// <summary>
        /// Ajoute les informations du patient
        /// </summary>
        private void AddPatientInfo(Body body, PatientMetadata patient)
        {
            var sexe = patient.Sexe?.ToUpper() == "M" ? "M." :
                       patient.Sexe?.ToUpper() == "F" ? "Mme" : "";

            var namePara = new Paragraph();
            namePara.Append(new Run(new Text($"{sexe} {patient.Nom?.ToUpper()} {patient.Prenom}, né(e) {patient.Nom?.ToUpper()} {patient.Prenom}") { Space = SpaceProcessingModeValues.Preserve }));
            body.AppendChild(namePara);

            // Date de naissance et âge - utiliser les propriétés calculées du modèle
            System.Diagnostics.Debug.WriteLine($"[OrdonnanceDocxService] patient.Dob = '{patient.Dob}', DobFormatted = '{patient.DobFormatted}', Age = {patient.Age}");

            if (!string.IsNullOrEmpty(patient.DobFormatted) && patient.Age.HasValue)
            {
                var ageText = $"Né(e) le {patient.DobFormatted} ({patient.Age} ans";

                // Ajouter les mois pour les enfants de moins de 3 ans
                if (patient.Age.Value < 3 && !string.IsNullOrEmpty(patient.Dob) && DateTime.TryParse(patient.Dob, out var dobDate))
                {
                    var today = DateTime.Today;
                    var months = ((today.Year - dobDate.Year) * 12) + today.Month - dobDate.Month;
                    if (today.Day < dobDate.Day) months--;
                    var remainingMonths = months % 12;
                    if (remainingMonths > 0)
                    {
                        ageText += $" {remainingMonths} mois";
                    }
                }
                ageText += ")";

                body.AppendChild(new Paragraph(new Run(new Text(ageText))));
                System.Diagnostics.Debug.WriteLine($"[OrdonnanceDocxService] ✓ Date de naissance ajoutée: {ageText}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[OrdonnanceDocxService] ⚠️ Date de naissance non disponible (Dob={patient.Dob})");
            }
        }

        /// <summary>
        /// Ajoute la liste des médicaments
        /// </summary>
        private void AddMedicaments(Body body, OrdonnanceMedicaments ordonnance)
        {
            foreach (var med in ordonnance.Medicaments)
            {
                // Nom du médicament en majuscules et gras
                var namePara = new Paragraph();
                namePara.Append(new Run(new Text(med.Medicament.Denomination?.ToUpper() ?? "")) { RunProperties = new RunProperties(new Bold(), new FontSize() { Val = "22" }) });
                body.AppendChild(namePara);

                // Posologie
                if (!string.IsNullOrEmpty(med.Posologie))
                {
                    body.AppendChild(new Paragraph(new Run(new Text(med.Posologie))));
                }

                // Ligne horizontale
                if (!string.IsNullOrEmpty(med.Duree))
                {
                    body.AppendChild(new Paragraph(new Run(new Text("_____________________________________________________________________________________"))));
                }

                // Durée et quantité
                if (!string.IsNullOrEmpty(med.Duree))
                {
                    var dureeText = $"Quantité suffisante pour {med.Duree}";
                    
                    // Ajouter le nombre de boîtes si > 1
                    if (med.Quantite > 1)
                    {
                        dureeText += $" ({med.Quantite} boîtes)";
                    }
                    
                    body.AppendChild(new Paragraph(new Run(new Text(dureeText))));
                }

                // Renouvellement
                if (med.Renouvelable && med.NombreRenouvellements > 0)
                {
                    var renouvText = med.NombreRenouvellements == 1
                        ? "À renouveler 1 fois"
                        : $"À renouveler {med.NombreRenouvellements} fois";
                    
                    var renouvPara = new Paragraph();
                    renouvPara.Append(new Run(new Text(renouvText)) { RunProperties = new RunProperties(new Bold(), new Color() { Val = "2E7D32" }) });
                    body.AppendChild(renouvPara);
                    
                    System.Diagnostics.Debug.WriteLine($"[OrdonnanceDocxService] ✅ Renouvellement affiché: {renouvText}");
                }

                // Espacement entre médicaments
                body.AppendChild(new Paragraph(new Run(new Text(""))));
            }
        }

        /// <summary>
        /// Ajoute la signature complète (nom, date, image) alignée à droite
        /// </summary>
        private void AddSignature(Body body, DateTime dateCreation)
        {
            // Paragraphe vide (supprimé - géré par l'appelant)
        }

        /// <summary>
        /// Ajoute la signature avec image de cartouche dans le document
        /// </summary>
        private void AddSignatureImage(WordprocessingDocument document, Body body)
        {
            // Récupérer l'image de signature
            var signatureImagePath = GetSignatureImagePath();
            Drawing? imageElement = null;

            if (!string.IsNullOrEmpty(signatureImagePath) && System.IO.File.Exists(signatureImagePath))
            {
                try
                {
                    var mainPart = document.MainDocumentPart;
                    if (mainPart != null)
                    {
                        // Ajouter l'image au document
                        ImagePart imagePart = mainPart.AddImagePart(ImagePartType.Png);
                        using (var stream = new System.IO.FileStream(signatureImagePath, System.IO.FileMode.Open))
                        {
                            imagePart.FeedData(stream);
                        }

                        string relationshipId = mainPart.GetIdOfPart(imagePart);

                        // Obtenir les dimensions de l'image
                        long width, height;
                        using (var stream = new System.IO.FileStream(signatureImagePath, System.IO.FileMode.Open))
                        using (var img = System.Drawing.Image.FromStream(stream))
                        {
                            // Largeur cible: ~4cm (1440000 EMUs = ~4cm)
                            const long targetWidth = 1440000;
                            double ratio = (double)img.Height / img.Width;
                            width = targetWidth;
                            height = (long)(targetWidth * ratio);
                        }

                        // Créer l'élément image (Drawing)
                        imageElement = new Drawing(
                            new DW.Inline(
                                new DW.Extent() { Cx = width, Cy = height },
                                new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                                new DW.DocProperties() { Id = 1U, Name = "Signature" },
                                new DW.NonVisualGraphicFrameDrawingProperties(
                                    new A.GraphicFrameLocks() { NoChangeAspect = true }
                                ),
                                new A.Graphic(
                                    new A.GraphicData(
                                        new PIC.Picture(
                                            new PIC.NonVisualPictureProperties(
                                                new PIC.NonVisualDrawingProperties() { Id = 0U, Name = "signature.png" },
                                                new PIC.NonVisualPictureDrawingProperties()
                                            ),
                                            new PIC.BlipFill(
                                                new A.Blip() { Embed = relationshipId },
                                                new A.Stretch(new A.FillRectangle())
                                            ),
                                            new PIC.ShapeProperties(
                                                new A.Transform2D(
                                                    new A.Offset() { X = 0L, Y = 0L },
                                                    new A.Extents() { Cx = width, Cy = height }
                                                ),
                                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }
                                            )
                                        )
                                    )
                                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                                )
                            )
                            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U }
                        );
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OrdonnanceDocxService] ❌ Erreur préparation image signature: {ex.Message}");
                }
            }

            // Créer un paragraphe aligné à droite avec tout le contenu
            // Nom du docteur
            var namePara = new Paragraph(new ParagraphProperties(
                new Justification() { Val = JustificationValues.Right },
                new SpacingBetweenLines() { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }
            ));
            namePara.Append(new Run(new Text("Dr Lassoued Nair")) { RunProperties = new RunProperties(new Bold(), new FontSize() { Val = "22" }) });
            body.AppendChild(namePara);

            // Date
            var datePara = new Paragraph(new ParagraphProperties(
                new Justification() { Val = JustificationValues.Right },
                new SpacingBetweenLines() { After = "60", Line = "240", LineRule = LineSpacingRuleValues.Auto }
            ));
            datePara.Append(new Run(new Text(FormatDateAbrege(DateTime.Now))) { RunProperties = new RunProperties(new FontSize() { Val = "20" }) });
            body.AppendChild(datePara);

            // Image de signature
            if (imageElement != null)
            {
                var imgPara = new Paragraph(new ParagraphProperties(
                    new Justification() { Val = JustificationValues.Right },
                    new SpacingBetweenLines() { Before = "0", After = "0" }
                ));
                imgPara.Append(new Run(imageElement));
                body.AppendChild(imgPara);

                System.Diagnostics.Debug.WriteLine("[OrdonnanceDocxService] ✅ Signature complète ajoutée (nom + date + image, aligné à droite)");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[OrdonnanceDocxService] ⚠️ Image signature non trouvée, texte seul affiché");
            }
        }

        /// <summary>
        /// Récupère le chemin de l'image de signature
        /// </summary>
        private string GetSignatureImagePath()
        {
            // Chercher dans le dossier Assets de l'application
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(exeDir))
            {
                var signaturePath = System.IO.Path.Combine(exeDir, "Assets", "cartouche_signature.png");
                if (System.IO.File.Exists(signaturePath))
                    return signaturePath;
            }

            return string.Empty;
        }

        /// <summary>
        /// Ajoute une image à un paragraphe (pour contrôler l'alignement)
        /// </summary>
        private void AddImageToParagraph(Paragraph paragraph, MainDocumentPart mainPart, string imagePath, int widthEmus, int heightEmus)
        {
            ImagePart imagePart = mainPart.AddImagePart(ImagePartType.Png);

            using (FileStream stream = new FileStream(imagePath, FileMode.Open))
            {
                imagePart.FeedData(stream);
            }

            var element = new Drawing(
                new DW.Inline(
                    new DW.Extent() { Cx = widthEmus * 9525, Cy = heightEmus * 9525 },
                    new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties() { Id = (UInt32Value)1U, Name = "Picture" },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties() { Id = (UInt32Value)0U, Name = "Image" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip() { Embed = mainPart.GetIdOfPart(imagePart) },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset() { X = 0L, Y = 0L },
                                        new A.Extents() { Cx = widthEmus * 9525, Cy = heightEmus * 9525 }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                        ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
                {
                    DistanceFromTop = (UInt32Value)0U,
                    DistanceFromBottom = (UInt32Value)0U,
                    DistanceFromLeft = (UInt32Value)0U,
                    DistanceFromRight = (UInt32Value)0U
                });

            paragraph.Append(new Run(element));
        }

        /// <summary>
        /// Ajoute une image au document
        /// </summary>
        private void AddImage(TableCell cell, MainDocumentPart mainPart, string imagePath, int widthEmus, int heightEmus)
        {
            ImagePart imagePart = mainPart.AddImagePart(ImagePartType.Png);

            using (FileStream stream = new FileStream(imagePath, FileMode.Open))
            {
                imagePart.FeedData(stream);
            }

            var element = new Drawing(
                new DW.Inline(
                    new DW.Extent() { Cx = widthEmus * 9525, Cy = heightEmus * 9525 },
                    new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties() { Id = (UInt32Value)1U, Name = "Picture" },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties() { Id = (UInt32Value)0U, Name = "Image" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip() { Embed = mainPart.GetIdOfPart(imagePart) },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset() { X = 0L, Y = 0L },
                                        new A.Extents() { Cx = widthEmus * 9525, Cy = heightEmus * 9525 }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                        ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
                {
                    DistanceFromTop = (UInt32Value)0U,
                    DistanceFromBottom = (UInt32Value)0U,
                    DistanceFromLeft = (UInt32Value)0U,
                    DistanceFromRight = (UInt32Value)0U
                });

            cell.Append(new Paragraph(new Run(element)));
        }

        /// <summary>
        /// Formate une date au format abrégé français (ex: "Le 18 déc. 2025")
        /// </summary>
        private string FormatDateAbrege(DateTime date)
        {
            var moisAbreges = new[] { "", "janv.", "fév.", "mars", "avr.", "mai", "juin",
                                     "juil.", "août", "sept.", "oct.", "nov.", "déc." };
            return $"Le {date.Day} {moisAbreges[date.Month]} {date.Year}";
        }
    }
}

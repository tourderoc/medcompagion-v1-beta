using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MedCompanion.Dialogs
{
    public partial class AttestationInfoDialog : Window
    {
        private readonly List<string> _requiredFields;
        private readonly List<string> _optionalFields;
        private readonly Dictionary<string, TextBox> _fieldTextBoxes;
        private readonly Dictionary<string, List<CheckBox>> _fieldCheckBoxes;
        private readonly Dictionary<string, RadioButton> _genderRadioButtons; // Pour le sexe

        public Dictionary<string, string>? CollectedInfo { get; private set; }

        public AttestationInfoDialog(List<string> requiredFields, List<string> optionalFields)
        {
            InitializeComponent();
            
            _requiredFields = requiredFields ?? new List<string>();
            _optionalFields = optionalFields ?? new List<string>();
            _fieldTextBoxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
            _fieldCheckBoxes = new Dictionary<string, List<CheckBox>>(StringComparer.OrdinalIgnoreCase);
            _genderRadioButtons = new Dictionary<string, RadioButton>(StringComparer.OrdinalIgnoreCase);
            
            BuildForm();
        }

        /// <summary>
        /// Construit le formulaire dynamiquement en fonction des champs requis et optionnels
        /// </summary>
        private void BuildForm()
        {
            // Ajouter les champs requis
            foreach (var field in _requiredFields)
            {
                AddField(field, isRequired: true);
            }

            // Ajouter les champs optionnels
            foreach (var field in _optionalFields)
            {
                AddField(field, isRequired: false);
            }

            // Si aucun champ, afficher un message
            if (_requiredFields.Count == 0 && _optionalFields.Count == 0)
            {
                var noFieldsText = new TextBlock
                {
                    Text = "Aucune information supplémentaire nécessaire.",
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D"))
                };
                FieldsPanel.Children.Add(noFieldsText);
            }
        }

        /// <summary>
        /// Ajoute un champ de saisie au formulaire
        /// </summary>
        private void AddField(string fieldName, bool isRequired)
        {
            var fieldContainer = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

            // Label avec * si requis
            var label = new TextBlock
            {
                Text = GetFieldLabel(fieldName) + (isRequired ? " *" : ""),
                FontSize = 12,
                FontWeight = isRequired ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50")),
                Margin = new Thickness(0, 0, 0, 5)
            };
            fieldContainer.Children.Add(label);

            // ✅ CAS SPÉCIAL : Sexe → RadioButtons
            if (fieldName.Equals("Sexe", StringComparison.OrdinalIgnoreCase))
            {
                // Sous-titre
                var subtitleLabel = new TextBlock
                {
                    Text = "Sélectionnez le sexe du patient :",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                fieldContainer.Children.Add(subtitleLabel);

                // Groupe de RadioButtons (même groupe = exclusifs)
                var groupName = "GenderGroup";

                // RadioButton Masculin
                var maleRadio = new RadioButton
                {
                    Content = "🚹 Masculin",
                    GroupName = groupName,
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
                };
                fieldContainer.Children.Add(maleRadio);

                // RadioButton Féminin
                var femaleRadio = new RadioButton
                {
                    Content = "🚺 Féminin",
                    GroupName = groupName,
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 10),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
                };
                fieldContainer.Children.Add(femaleRadio);

                // Stocker les RadioButtons
                _genderRadioButtons["Male"] = maleRadio;
                _genderRadioButtons["Female"] = femaleRadio;

                FieldsPanel.Children.Add(fieldContainer);
            }
            // CAS SPÉCIAL : Accompagnateur → CheckBoxes + nom de famille
            else if (fieldName.Equals("Accompagnateur", StringComparison.OrdinalIgnoreCase))
            {
                // Sous-titre
                var subtitleLabel = new TextBlock
                {
                    Text = "Qui accompagne l'enfant ? (optionnel)",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                fieldContainer.Children.Add(subtitleLabel);

                // 4 checkboxes pour le lien de parenté
                var accompagnateurs = new List<string>
                {
                    "Les parents",
                    "La mère",
                    "Le père",
                    "Tuteur légal"
                };

                var checkBoxList = new List<CheckBox>();
                foreach (var accompagnateur in accompagnateurs)
                {
                    var cb = new CheckBox
                    {
                        Content = accompagnateur,
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
                    };
                    fieldContainer.Children.Add(cb);
                    checkBoxList.Add(cb);
                }

                _fieldCheckBoxes[fieldName] = checkBoxList;

                // Champ texte pour le nom de famille
                var nomLabel = new TextBlock
                {
                    Text = "Nom de famille :",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 10, 0, 5)
                };
                fieldContainer.Children.Add(nomLabel);

                var nomTextBox = new TextBox
                {
                    Height = 30,
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 12,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BDC3C7")),
                    BorderThickness = new Thickness(1)
                };

                // Placeholder
                nomTextBox.Tag = "Ex: MARTIN";
                nomTextBox.GotFocus += TextBox_GotFocus;
                nomTextBox.LostFocus += TextBox_LostFocus;
                nomTextBox.Text = "Ex: MARTIN";
                nomTextBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6"));

                fieldContainer.Children.Add(nomTextBox);

                // Stocker le TextBox pour récupérer le nom
                _fieldTextBoxes[fieldName + "_Nom"] = nomTextBox;

                FieldsPanel.Children.Add(fieldContainer);
            }
            // CAS SPÉCIAL : Liste_Amenagements → CheckBoxes + champ libre
            else if (fieldName.Equals("Liste_Amenagements", StringComparison.OrdinalIgnoreCase))
            {
                // Sous-titre pour les aménagements prédéfinis
                var predefinedLabel = new TextBlock
                {
                    Text = "Cochez les aménagements à recommander :",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                fieldContainer.Children.Add(predefinedLabel);

                // 6 checkboxes pour les aménagements courants
                var amenagements = new List<string>
                {
                    "Tiers-temps aux évaluations",
                    "Accès à l'ordinateur",
                    "Pause si besoin",
                    "Supports visuels/schémas",
                    "Clarté et simplicité des consignes",
                    "Valorisation et encouragements réguliers"
                };

                var checkBoxList = new List<CheckBox>();
                foreach (var amenagement in amenagements)
                {
                    var cb = new CheckBox
                    {
                        Content = amenagement,
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
                    };
                    fieldContainer.Children.Add(cb);
                    checkBoxList.Add(cb);
                }

                _fieldCheckBoxes[fieldName] = checkBoxList;

                // Champ texte libre pour aménagements personnalisés
                var customLabel = new TextBlock
                {
                    Text = "Autres aménagements (optionnel) :",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 10, 0, 5)
                };
                fieldContainer.Children.Add(customLabel);

                var customTextBox = new TextBox
                {
                    Height = 60,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(8),
                    FontSize = 12,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BDC3C7")),
                    BorderThickness = new Thickness(1)
                };

                fieldContainer.Children.Add(customTextBox);

                // Stocker le TextBox pour récupérer les aménagements personnalisés
                _fieldTextBoxes[fieldName + "_Custom"] = customTextBox;

                FieldsPanel.Children.Add(fieldContainer);
            }
            // TextBox multiligne pour Motif_Arret
            else if (fieldName.Equals("Motif_Arret", StringComparison.OrdinalIgnoreCase))
            {
                var textBox = new TextBox
                {
                    Height = 80,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(8),
                    FontSize = 12,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BDC3C7")),
                    BorderThickness = new Thickness(1)
                };

                // Placeholder
                textBox.Tag = GetFieldPlaceholder(fieldName);
                textBox.GotFocus += TextBox_GotFocus;
                textBox.LostFocus += TextBox_LostFocus;
                
                if (!string.IsNullOrEmpty(textBox.Tag as string))
                {
                    textBox.Text = textBox.Tag as string;
                    textBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6"));
                }

                fieldContainer.Children.Add(textBox);
                _fieldTextBoxes[fieldName] = textBox;
                FieldsPanel.Children.Add(fieldContainer);
            }
            // TextBox simple pour les autres champs
            else
            {
                var textBox = new TextBox
                {
                    Height = 30,
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 12,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BDC3C7")),
                    BorderThickness = new Thickness(1)
                };

                // Placeholder
                textBox.Tag = GetFieldPlaceholder(fieldName);
                textBox.GotFocus += TextBox_GotFocus;
                textBox.LostFocus += TextBox_LostFocus;
                
                if (!string.IsNullOrEmpty(textBox.Tag as string))
                {
                    textBox.Text = textBox.Tag as string;
                    textBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6"));
                }

                fieldContainer.Children.Add(textBox);
                _fieldTextBoxes[fieldName] = textBox;
                FieldsPanel.Children.Add(fieldContainer);
            }
        }

        /// <summary>
        /// Convertit le nom de champ en label lisible
        /// </summary>
        private string GetFieldLabel(string fieldName)
        {
            return fieldName switch
            {
                "Date_Debut" => "Date de début",
                "Date_Fin" => "Date de fin",
                "Date_Debut_Suivi" => "Date de début du suivi",
                "Frequence_Suivi" => "Fréquence du suivi",
                "Motif_Arret" => "Motif de l'arrêt",
                "Liste_Amenagements" => "Liste des aménagements",
                "Duree_Amenagements" => "Durée des aménagements",
                _ => fieldName.Replace("_", " ")
            };
        }

        /// <summary>
        /// Retourne un placeholder adapté au champ
        /// </summary>
        private string GetFieldPlaceholder(string fieldName)
        {
            return fieldName switch
            {
                "Date_Debut" => "Ex: 20/10/2025",
                "Date_Fin" => "Ex: 27/10/2025",
                "Date_Debut_Suivi" => "Ex: 01/09/2024",
                "Frequence_Suivi" => "Ex: 1 fois par mois",
                "Motif_Arret" => "Ex: Fatigue importante, besoin de repos",
                "Liste_Amenagements" => "Ex:\n- Tiers-temps aux évaluations\n- Accès à l'ordinateur\n- Pause si besoin",
                "Duree_Amenagements" => "Ex: 1 an, jusqu'à la fin de l'année scolaire",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Gère le focus sur un TextBox (supprime le placeholder)
        /// </summary>
        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Tag is string placeholder)
            {
                if (textBox.Text == placeholder)
                {
                    textBox.Text = string.Empty;
                    textBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"));
                }
            }
        }

        /// <summary>
        /// Gère la perte de focus sur un TextBox (remet le placeholder si vide)
        /// </summary>
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Tag is string placeholder)
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = placeholder;
                    textBox.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6"));
                }
            }
        }

        /// <summary>
        /// Valide et collecte les informations saisies
        /// </summary>
        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            var collectedInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var missingRequired = new List<string>();

            // ✅ Gérer le sexe (RadioButtons)
            if (_genderRadioButtons.Count > 0)
            {
                var maleRadio = _genderRadioButtons["Male"];
                var femaleRadio = _genderRadioButtons["Female"];

                if (maleRadio.IsChecked == true)
                {
                    collectedInfo["Sexe"] = "H";
                }
                else if (femaleRadio.IsChecked == true)
                {
                    collectedInfo["Sexe"] = "F";
                }
                else if (_requiredFields.Contains("Sexe", StringComparer.OrdinalIgnoreCase))
                {
                    // Sexe requis mais non sélectionné
                    missingRequired.Add("Sexe");
                }
            }

            // Gérer les CheckBoxes (Liste_Amenagements et Accompagnateur)
            foreach (var kvp in _fieldCheckBoxes)
            {
                var fieldName = kvp.Key;
                var checkBoxes = kvp.Value;
                var selectedOptions = new List<string>();

                foreach (var cb in checkBoxes)
                {
                    if (cb.IsChecked == true)
                    {
                        selectedOptions.Add(cb.Content.ToString() ?? "");
                    }
                }

                // CAS SPÉCIAL : Accompagnateur → Formater avec civilité + nom
                if (fieldName.Equals("Accompagnateur", StringComparison.OrdinalIgnoreCase))
                {
                    if (selectedOptions.Count > 0)
                    {
                        // Récupérer le nom de famille
                        var nomKey = fieldName + "_Nom";
                        string nom = string.Empty;
                        if (_fieldTextBoxes.ContainsKey(nomKey))
                        {
                            var nomText = _fieldTextBoxes[nomKey].Text.Trim();
                            var nomPlaceholder = _fieldTextBoxes[nomKey].Tag as string ?? string.Empty;
                            if (nomText != nomPlaceholder && !string.IsNullOrWhiteSpace(nomText))
                            {
                                nom = nomText.ToUpper(); // Nom en majuscules
                            }
                        }

                        // Formater selon le lien sélectionné
                        var lien = selectedOptions[0]; // Prendre le premier coché
                        string formatted = lien switch
                        {
                            "Les parents" => $"M. et Mme {nom} (parents)",
                            "La mère" => $"Mme {nom} (mère)",
                            "Le père" => $"M. {nom} (père)",
                            "Tuteur légal" => $"M. ou Mme {nom} (tuteur légal)",
                            _ => $"{nom}"
                        };

                        collectedInfo[fieldName] = formatted;
                    }
                    // Sinon, rien n'est ajouté (champ optionnel vide)
                    continue;
                }

                // Ajouter les aménagements du champ libre s'ils existent
                var customKey = fieldName + "_Custom";
                if (_fieldTextBoxes.ContainsKey(customKey))
                {
                    var customText = _fieldTextBoxes[customKey].Text.Trim();
                    if (!string.IsNullOrEmpty(customText))
                    {
                        // Séparer par lignes ou virgules
                        var customItems = customText.Split(new[] { '\n', '\r', ',' }, 
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var item in customItems)
                        {
                            if (!string.IsNullOrWhiteSpace(item))
                            {
                                selectedOptions.Add(item.Trim());
                            }
                        }
                    }
                }

                // Formater en liste numérotée si au moins un aménagement
                if (selectedOptions.Count > 0)
                {
                    var formatted = new System.Text.StringBuilder();
                    for (int i = 0; i < selectedOptions.Count; i++)
                    {
                        formatted.AppendLine($"{i + 1}. {selectedOptions[i]}");
                    }
                    collectedInfo[fieldName] = formatted.ToString().TrimEnd();
                }
                else if (_requiredFields.Contains(fieldName))
                {
                    // Champ requis mais aucune case cochée
                    missingRequired.Add(GetFieldLabel(fieldName));
                }
            }

            // Collecter toutes les valeurs des TextBoxes
            foreach (var kvp in _fieldTextBoxes)
            {
                var fieldName = kvp.Key;
                
                // Ignorer les champs "_Custom" (déjà traités avec les checkboxes)
                if (fieldName.EndsWith("_Custom", StringComparison.OrdinalIgnoreCase))
                    continue;

                var textBox = kvp.Value;
                var value = textBox.Text.Trim();
                var placeholder = textBox.Tag as string ?? string.Empty;

                // Ignorer si c'est le placeholder
                if (value == placeholder)
                    value = string.Empty;

                // Vérifier si champ requis est vide
                if (_requiredFields.Contains(fieldName) && string.IsNullOrWhiteSpace(value))
                {
                    missingRequired.Add(GetFieldLabel(fieldName));
                }
                else if (!string.IsNullOrWhiteSpace(value))
                {
                    collectedInfo[fieldName] = value;
                }
            }

            // Si des champs requis sont manquants
            if (missingRequired.Count > 0)
            {
                var message = "Les champs suivants sont obligatoires :\n\n" + 
                              string.Join("\n", missingRequired.Select(f => $"• {f}"));
                
                MessageBox.Show(message, "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Tout est OK
            CollectedInfo = collectedInfo;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Annule la saisie
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CollectedInfo = null;
            DialogResult = false;
            Close();
        }
    }
}

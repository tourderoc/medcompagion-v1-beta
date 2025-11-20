using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MedCompanion.Dialogs
{
    public class MissingFieldInfo
    {
        public string FieldName { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
    }

    public partial class MissingInfoDialog : Window
    {
        private readonly Dictionary<string, TextBox> _fieldTextBoxes = new();
        private readonly Dictionary<string, DatePicker> _fieldDatePickers = new();
        private readonly Dictionary<string, List<CheckBox>> _fieldCheckBoxes = new();
        
        public Dictionary<string, string>? CollectedInfo { get; private set; }

        public MissingInfoDialog(List<MissingFieldInfo> missingFields)
        {
            InitializeComponent();
            BuildFields(missingFields);
        }

        private void BuildFields(List<MissingFieldInfo> missingFields)
        {
            foreach (var field in missingFields)
            {
                // Label
                var label = new TextBlock
                {
                    Text = field.IsRequired ? $"{field.Prompt} *" : field.Prompt,
                    FontSize = 13,
                    FontWeight = field.IsRequired ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80)),
                    Margin = new Thickness(0, 0, 0, 5)
                };

                FieldsContainer.Children.Add(label);

                // CAS SPÉCIAL : Destinataire → CheckBoxes
                if (field.FieldName.Equals("Destinataire", System.StringComparison.OrdinalIgnoreCase))
                {
                    var checkboxPanel = new StackPanel
                    {
                        Margin = new Thickness(0, 5, 0, 15)
                    };

                    var chefCheckBox = new CheckBox
                    {
                        Content = "Chef d'établissement",
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80))
                    };

                    var enseignantCheckBox = new CheckBox
                    {
                        Content = "Enseignant référent",
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80))
                    };

                    checkboxPanel.Children.Add(chefCheckBox);
                    checkboxPanel.Children.Add(enseignantCheckBox);

                    FieldsContainer.Children.Add(checkboxPanel);

                    _fieldCheckBoxes[field.FieldName] = new List<CheckBox> { chefCheckBox, enseignantCheckBox };
                }
                // CAS SPÉCIAL : Liste des aménagements → CheckBoxes + champ libre
                else if (field.FieldName.Contains("Aménagement", System.StringComparison.OrdinalIgnoreCase) ||
                         field.FieldName.Contains("Amenagement", System.StringComparison.OrdinalIgnoreCase) ||
                         field.FieldName.Equals("Liste_Amenagements", System.StringComparison.OrdinalIgnoreCase))
                {
                    var mainPanel = new StackPanel
                    {
                        Margin = new Thickness(0, 5, 0, 15)
                    };

                    // Sous-titre pour les aménagements prédéfinis
                    var predefinedLabel = new TextBlock
                    {
                        Text = "Cochez les aménagements à recommander :",
                        FontSize = 12,
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141)),
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    mainPanel.Children.Add(predefinedLabel);

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
                            FontSize = 13,
                            Margin = new Thickness(0, 0, 0, 8),
                            Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80))
                        };
                        mainPanel.Children.Add(cb);
                        checkBoxList.Add(cb);
                    }

                    _fieldCheckBoxes[field.FieldName] = checkBoxList;

                    // Champ texte libre pour aménagements personnalisés
                    var customLabel = new TextBlock
                    {
                        Text = "Autres aménagements (optionnel) :",
                        FontSize = 12,
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141)),
                        Margin = new Thickness(0, 10, 0, 5)
                    };
                    mainPanel.Children.Add(customLabel);

                    var customTextBox = new TextBox
                    {
                        Height = 60,
                        FontSize = 13,
                        Padding = new Thickness(8),
                        TextWrapping = TextWrapping.Wrap,
                        AcceptsReturn = true,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                        BorderThickness = new Thickness(1)
                    };

                    var customBorder = new Border
                    {
                        Child = customTextBox,
                        CornerRadius = new CornerRadius(6),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                        BorderThickness = new Thickness(1)
                    };
                    mainPanel.Children.Add(customBorder);

                    FieldsContainer.Children.Add(mainPanel);

                    // Stocker le TextBox pour récupérer les aménagements personnalisés
                    _fieldTextBoxes[field.FieldName + "_Custom"] = customTextBox;
                }
                // CAS SPÉCIAL : Champ Date → DatePicker
                else if (field.FieldName.Contains("Date", System.StringComparison.OrdinalIgnoreCase) || 
                         field.FieldName.Contains("RDV", System.StringComparison.OrdinalIgnoreCase))
                {
                    var datePicker = new DatePicker
                    {
                        Height = 35,
                        FontSize = 13,
                        Padding = new Thickness(8),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 0, 15),
                        SelectedDateFormat = DatePickerFormat.Short,
                        DisplayDate = System.DateTime.Today
                    };

                    FieldsContainer.Children.Add(datePicker);
                    _fieldDatePickers[field.FieldName] = datePicker;
                }
                else
                {
                    // TextBox normal pour les autres champs
                    var textBox = new TextBox
                    {
                        Height = 35,
                        FontSize = 13,
                        Padding = new Thickness(8),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 0, 15)
                    };

                    // Style arrondi pour TextBox
                    var border = new Border
                    {
                        Child = textBox,
                        CornerRadius = new CornerRadius(6),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, 0, 15)
                    };

                    FieldsContainer.Children.Add(border);
                    _fieldTextBoxes[field.FieldName] = textBox;
                }
            }

            // Note sur champs obligatoires
            if (missingFields.Exists(f => f.IsRequired))
            {
                var note = new TextBlock
                {
                    Text = "* Champs obligatoires",
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141)),
                    Margin = new Thickness(0, 10, 0, 0)
                };
                FieldsContainer.Children.Add(note);
            }
        }

        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            // Collecter les valeurs (UNIQUEMENT les champs remplis)
            var collected = new Dictionary<string, string>();

            // Gérer les CheckBoxes (Destinataire et Aménagements)
            foreach (var kvp in _fieldCheckBoxes)
            {
                var checkBoxes = kvp.Value;
                var selectedOptions = new List<string>();

                foreach (var cb in checkBoxes)
                {
                    if (cb.IsChecked == true)
                    {
                        selectedOptions.Add(cb.Content.ToString() ?? "");
                    }
                }

                // CAS SPÉCIAL : Aménagements → Format liste numérotée
                if (kvp.Key.Contains("Aménagement", System.StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Contains("Amenagement", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Ajouter les aménagements du champ libre s'ils existent
                    var customKey = kvp.Key + "_Custom";
                    if (_fieldTextBoxes.ContainsKey(customKey))
                    {
                        var customText = _fieldTextBoxes[customKey].Text.Trim();
                        if (!string.IsNullOrEmpty(customText))
                        {
                            // Séparer par lignes ou virgules
                            var customItems = customText.Split(new[] { '\n', '\r', ',' }, 
                                System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
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
                        collected[kvp.Key] = formatted.ToString().TrimEnd();
                    }
                }
                else
                {
                    // Pour les autres CheckBoxes (Destinataire), joindre par virgules
                    if (selectedOptions.Count > 0)
                    {
                        collected[kvp.Key] = string.Join(", ", selectedOptions);
                    }
                }
            }

            // Gérer les DatePickers (champs date)
            foreach (var kvp in _fieldDatePickers)
            {
                // Si une date est sélectionnée, l'ajouter
                if (kvp.Value.SelectedDate != null)
                {
                    collected[kvp.Key] = kvp.Value.SelectedDate.Value.ToString("dd/MM/yyyy");
                }
                // Sinon, on ignore (pas ajouté à collected)
            }

            // Gérer les TextBoxes (autres champs)
            foreach (var kvp in _fieldTextBoxes)
            {
                var value = kvp.Value.Text.Trim();
                
                // Si le champ contient une valeur, l'ajouter
                if (!string.IsNullOrEmpty(value))
                {
                    collected[kvp.Key] = value;
                }
                // Sinon, on ignore (pas ajouté à collected)
            }

            // Toujours valider, même si collected est vide
            // L'IA se débrouillera pour reformuler le courrier avec les infos disponibles
            CollectedInfo = collected;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CollectedInfo = null;
            DialogResult = false;
            Close();
        }
    }
}

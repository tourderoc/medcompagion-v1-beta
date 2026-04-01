using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models.StateMachine;
using MedCompanion.Services;
using Microsoft.Win32;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// ViewModel pour afficher un état dans la liste
    /// </summary>
    public class StateDisplayItem
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string InitialLetter => Name.Length > 0 ? Name[0].ToString().ToUpper() : "?";
        public string MediaStatus { get; set; } = "Aucune video configuree";
        public Visibility ClearButtonVisibility { get; set; } = Visibility.Collapsed;
    }

    /// <summary>
    /// Dialog de configuration de l'avatar Med - version State Machine unifiée
    /// </summary>
    public partial class MedAvatarConfigDialog : Window
    {
        private MedAvatarService? _avatarService;
        private List<StateDisplayItem> _stateItems = new();

        public MedAvatarConfigDialog()
        {
            InitializeComponent();

            try
            {
                _avatarService = MedAvatarService.Instance;
                SizeSlider.Value = _avatarService.AvatarSize;

                RefreshStatesList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AvatarConfig] Erreur init: {ex.Message}");
            }
        }

        /// <summary>
        /// Rafraîchit la liste des états
        /// </summary>
        private void RefreshStatesList()
        {
            if (_avatarService == null) return;

            _stateItems.Clear();

            foreach (var state in _avatarService.CurrentProfile.States)
            {
                var hasMedia = state.MediaSequence.Any(m => m.FileExists);
                var mediaCount = state.MediaSequence.Count(m => m.FileExists);

                _stateItems.Add(new StateDisplayItem
                {
                    Name = state.Name,
                    Description = state.Description,
                    MediaStatus = hasMedia
                        ? $"{mediaCount} video(s) configuree(s)"
                        : "Aucune video configuree",
                    ClearButtonVisibility = hasMedia ? Visibility.Visible : Visibility.Collapsed
                });
            }

            StatesItemsControl.ItemsSource = null;
            StatesItemsControl.ItemsSource = _stateItems;
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _avatarService == null) return;

            try
            {
                _avatarService.AvatarSize = (int)e.NewValue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AvatarConfig] Erreur size: {ex.Message}");
            }
        }

        /// <summary>
        /// Sélectionne une vidéo MP4 pour un état
        /// </summary>
        private void SelectMedia_Click(object sender, RoutedEventArgs e)
        {
            if (_avatarService == null) return;

            if (sender is Button btn && btn.Tag is string stateName)
            {
                try
                {
                    var openDialog = new OpenFileDialog
                    {
                        Title = $"Choisir une video MP4 pour l'etat {stateName}",
                        Filter = "Fichiers Video (*.mp4)|*.mp4|Tous les fichiers (*.*)|*.*",
                        DefaultExt = ".mp4"
                    };

                    if (openDialog.ShowDialog() == true)
                    {
                        var sourcePath = openDialog.FileName;

                        // Importer et définir le média
                        var (success, error) = _avatarService.SetMediaForState(stateName, sourcePath, loop: true);

                        if (success)
                        {
                            RefreshStatesList();
                            MessageBox.Show($"Video importee avec succes pour l'etat {stateName}.",
                                "Import reussi", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"Erreur lors de l'import: {error}",
                                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Supprime les médias d'un état
        /// </summary>
        private void ClearMedia_Click(object sender, RoutedEventArgs e)
        {
            if (_avatarService == null) return;

            if (sender is Button btn && btn.Tag is string stateName)
            {
                try
                {
                    var result = MessageBox.Show(
                        $"Voulez-vous supprimer la video pour l'etat {stateName} ?",
                        "Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Trouver l'état et supprimer ses médias
                        var state = _avatarService.CurrentProfile.States
                            .FirstOrDefault(s => s.Name.Equals(stateName, StringComparison.OrdinalIgnoreCase));

                        if (state != null)
                        {
                            // Supprimer tous les médias de l'état
                            var mediaIds = state.MediaSequence.Select(m => m.Id).ToList();
                            foreach (var id in mediaIds)
                            {
                                _avatarService.RemoveMediaFromState(stateName, id);
                            }
                        }

                        RefreshStatesList();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenEditor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var editor = new MedCompanion.Views.StateMachineEditorWindow();
                editor.Owner = this;
                editor.ShowDialog();

                // Rafraîchir après fermeture de l'éditeur
                RefreshStatesList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

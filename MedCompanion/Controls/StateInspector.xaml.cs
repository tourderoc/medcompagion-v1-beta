using System;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models.StateMachine;
using Microsoft.Win32;

namespace MedCompanion.Controls
{
    public partial class StateInspector : UserControl
    {
        public StateInspector()
        {
            InitializeComponent();
        }

        private void OnAddMediaClick(object sender, RoutedEventArgs e)
        {
            var state = DataContext as AvatarState;
            if (state == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Fichiers Video (*.mp4)|*.mp4|Tous les fichiers (*.*)|*.*",
                Title = "Ajouter une video MP4",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    state.MediaSequence.Add(new MediaItem
                    {
                        FilePath = file,
                        Loop = state.IsLooping,
                        DisplayName = System.IO.Path.GetFileNameWithoutExtension(file)
                    });
                }
            }
        }

        private void OnRemoveMediaClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MediaItem item)
            {
                var state = DataContext as AvatarState;
                if (state != null)
                {
                    state.MediaSequence.Remove(item);
                }
            }
        }

        private void OnLoopChanged(object sender, RoutedEventArgs e)
        {
            var state = DataContext as AvatarState;
            if (state == null) return;

            // Propager le changement de loop à tous les médias de l'état
            foreach (var media in state.MediaSequence)
            {
                media.Loop = state.IsLooping;
            }

            System.Diagnostics.Debug.WriteLine($"[StateInspector] Loop changed to {state.IsLooping} for state {state.Name}");
        }
    }
}

using System;
using System.Windows;
using MedCompanion.Models.StateMachine;
using MedCompanion.Services;

namespace MedCompanion.Views
{
    public partial class StateMachineEditorWindow : Window
    {
        private readonly MedAvatarService _service;

        public StateMachineEditorWindow()
        {
            InitializeComponent();
            _service = MedAvatarService.Instance;

            if (_service.CurrentProfile != null)
            {
                EditorControl.LoadProfile(_service.CurrentProfile);
                TransitionInspectorControl.SetProfile(_service.CurrentProfile);
            }

            EditorControl.StateSelected += OnStateSelected;
            EditorControl.TransitionSelected += OnTransitionSelected;
            TransitionInspectorControl.DeleteRequested += OnTransitionDeleteRequested;
        }

        private void OnStateSelected(object? sender, AvatarState e)
        {
            // Afficher le StateInspector
            StateInspectorControl.DataContext = e;
            StateInspectorControl.Visibility = Visibility.Visible;
            TransitionInspectorControl.Visibility = Visibility.Collapsed;
            NoSelectionPanel.Visibility = Visibility.Collapsed;
        }

        private void OnTransitionSelected(object? sender, AvatarTransition e)
        {
            // Afficher le TransitionInspector
            TransitionInspectorControl.SetTransition(e);
            TransitionInspectorControl.Visibility = Visibility.Visible;
            StateInspectorControl.Visibility = Visibility.Collapsed;
            NoSelectionPanel.Visibility = Visibility.Collapsed;
        }

        private void OnTransitionDeleteRequested(object? sender, EventArgs e)
        {
            var transition = TransitionInspectorControl.CurrentTransition;
            if (transition != null && _service.CurrentProfile != null)
            {
                _service.CurrentProfile.Transitions.Remove(transition);
                TransitionInspectorControl.SetTransition(null);

                // Revenir au panel "rien sélectionné"
                TransitionInspectorControl.Visibility = Visibility.Collapsed;
                NoSelectionPanel.Visibility = Visibility.Visible;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_service.SaveProfile())
            {
                MessageBox.Show("Profil sauvegarde avec succes!", "Succes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Erreur lors de la sauvegarde du profil.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

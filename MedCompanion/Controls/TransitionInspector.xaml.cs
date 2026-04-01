using System;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models.StateMachine;

namespace MedCompanion.Controls
{
    public partial class TransitionInspector : UserControl
    {
        private AvatarTransition? _currentTransition;
        private StateMachineProfile? _profile;
        private bool _isUpdating;

        public event EventHandler? DeleteRequested;

        public TransitionInspector()
        {
            InitializeComponent();
        }

        public void SetProfile(StateMachineProfile profile)
        {
            _profile = profile;
        }

        public void SetTransition(AvatarTransition? transition)
        {
            _isUpdating = true;

            _currentTransition = transition;

            if (transition == null)
            {
                ClearDisplay();
                _isUpdating = false;
                return;
            }

            TriggerTextBox.Text = transition.Trigger;
            TransitionSummary.Text = $"ID: {transition.Id}";

            // Trouver les noms des états source et cible
            if (_profile != null)
            {
                var source = FindStateById(transition.SourceStateId);
                var target = FindStateById(transition.TargetStateId);

                SourceStateLabel.Text = source?.Name ?? "(Inconnu)";
                TargetStateLabel.Text = target?.Name ?? "(Inconnu)";
            }
            else
            {
                SourceStateLabel.Text = transition.SourceStateId.ToString();
                TargetStateLabel.Text = transition.TargetStateId.ToString();
            }

            _isUpdating = false;
        }

        private AvatarState? FindStateById(Guid id)
        {
            if (_profile == null) return null;

            foreach (var state in _profile.States)
            {
                if (state.Id == id) return state;
            }
            return null;
        }

        private void ClearDisplay()
        {
            TriggerTextBox.Text = "";
            TransitionSummary.Text = "";
            SourceStateLabel.Text = "";
            TargetStateLabel.Text = "";
        }

        private void OnTriggerTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating || _currentTransition == null) return;

            _currentTransition.Trigger = TriggerTextBox.Text;
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_currentTransition == null) return;

            var result = MessageBox.Show(
                $"Supprimer la transition '{_currentTransition.Trigger}' ?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DeleteRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public AvatarTransition? CurrentTransition => _currentTransition;
    }
}

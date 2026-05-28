using System;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models.Evaluations;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Consultation.Evaluation
{
    public partial class EvaluationPhaseControl : UserControl
    {
        public EvaluationPhaseControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Clic sur un des 4 radio (état d'un axe). Le Tag du RadioButton porte le code 0/1/2/3.
        /// </summary>
        private void AxisState_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not EvaluationAxis axis) return;
            if (btn.Tag is not string tag || !int.TryParse(tag, out var code)) return;

            axis.State = (AxisExplorationState)Math.Clamp(code, 0, 2);
        }
    }
}

using System.Collections.Generic;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Wraps a FeuilleEnvironnement for the FeuilleEvaluationDialog popup.
    /// Provides Title and a flat AllNervures list (centrale first, then secondaires).
    /// </summary>
    public class FeuilleEvaluationViewModel
    {
        public string Title { get; }
        public FeuilleEnvironnement Feuille { get; }
        public List<Nervure> AllNervures { get; }

        public FeuilleEvaluationViewModel(FeuilleEnvironnement feuille)
        {
            Feuille = feuille;
            Title   = $"🍃 {feuille.Label} — {feuille.SousTitre}";
            AllNervures = new List<Nervure> { feuille.NervureCentrale };
            foreach (var n in feuille.NervuresSecondaires)
                AllNervures.Add(n);
        }
    }
}

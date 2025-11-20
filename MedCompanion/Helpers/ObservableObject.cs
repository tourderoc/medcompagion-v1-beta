using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Helpers;

/// <summary>
/// Classe de base pour les objets observables implémentant INotifyPropertyChanged
/// Permet aux propriétés de notifier l'UI lors de changements
/// </summary>
public class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Notifie l'UI qu'une propriété a changé
    /// </summary>
    /// <param name="propertyName">Nom de la propriété (rempli automatiquement)</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Met à jour une propriété et notifie si la valeur change
    /// </summary>
    /// <typeparam name="T">Type de la propriété</typeparam>
    /// <param name="field">Référence au champ backing</param>
    /// <param name="value">Nouvelle valeur</param>
    /// <param name="propertyName">Nom de la propriété</param>
    /// <returns>True si la valeur a changé, false sinon</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

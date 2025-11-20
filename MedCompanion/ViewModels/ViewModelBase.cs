using MedCompanion.Helpers;

namespace MedCompanion.ViewModels;

/// <summary>
/// Classe de base pour tous les ViewModels
/// Hérite de ObservableObject pour bénéficier de INotifyPropertyChanged
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Indique si le ViewModel est en train de traiter une opération
    /// Utile pour afficher des indicateurs de chargement
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// Message de statut à afficher à l'utilisateur
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// Méthode virtuelle appelée lors de l'initialisation du ViewModel
    /// Peut être surchargée dans les ViewModels enfants
    /// </summary>
    public virtual void Initialize()
    {
    }

    /// <summary>
    /// Méthode virtuelle appelée lors de la fermeture/nettoyage
    /// Peut être surchargée pour libérer des ressources
    /// </summary>
    public virtual void Cleanup()
    {
    }
}

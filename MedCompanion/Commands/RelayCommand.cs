using System;
using System.Windows;
using System.Windows.Input;

namespace MedCompanion.Commands;

/// <summary>
/// Implémentation simple de ICommand pour WPF
/// Permet de lier des méthodes aux boutons et autres contrôles
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// Événement déclenché quand CanExecute change
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// Constructeur avec action à exécuter et condition optionnelle
    /// </summary>
    /// <param name="execute">Action à exécuter</param>
    /// <param name="canExecute">Fonction déterminant si la commande peut s'exécuter (optionnel)</param>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// Constructeur simplifié sans paramètre
    /// </summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(
            _ => execute(),
            canExecute != null ? _ => canExecute() : null)
    {
    }

    /// <summary>
    /// Détermine si la commande peut être exécutée
    /// </summary>
    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute(parameter);
    }

    /// <summary>
    /// Exécute la commande
    /// </summary>
    public void Execute(object? parameter)
    {
        _execute(parameter);
    }

    /// <summary>
    /// Force la réévaluation de CanExecute sur le thread UI
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        // Force la réévaluation IMMÉDIATE sur le thread UI (synchrone)
        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            // Déjà sur le thread UI → Appel direct
            CommandManager.InvalidateRequerySuggested();
        }
        else
        {
            // Pas sur le thread UI → Dispatch synchrone
            Application.Current?.Dispatcher.Invoke(() =>
            {
                CommandManager.InvalidateRequerySuggested();
            });
        }
    }
}

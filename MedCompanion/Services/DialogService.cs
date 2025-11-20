using System;
using System.Windows;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service centralisé pour la gestion des dialogs
    /// Utilisable par tous les ViewModels pour afficher des messages et dialogs personnalisés
    /// </summary>
    public class DialogService
    {
        /// <summary>
        /// Affiche une boîte de dialogue de confirmation (Oui/Non)
        /// </summary>
        /// <returns>true si l'utilisateur clique sur Oui, false si Non, null si annulation</returns>
        public bool? ShowConfirmation(string title, string message)
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            return result == MessageBoxResult.Yes ? true : (bool?)false;
        }

        /// <summary>
        /// Affiche une boîte de dialogue d'erreur
        /// </summary>
        public void ShowError(string title, string message)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        /// <summary>
        /// Affiche une boîte de dialogue d'information
        /// </summary>
        public void ShowInfo(string title, string message)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        /// <summary>
        /// Affiche une boîte de dialogue d'avertissement
        /// </summary>
        public void ShowWarning(string title, string message)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        /// <summary>
        /// Affiche un dialog personnalisé et retourne le résultat
        /// </summary>
        /// <typeparam name="TDialog">Type du dialog à afficher</typeparam>
        /// <typeparam name="TResult">Type du résultat attendu</typeparam>
        /// <param name="dialog">Instance du dialog</param>
        /// <param name="resultExtractor">Fonction pour extraire le résultat du dialog</param>
        /// <returns>Le résultat du dialog ou null si annulation</returns>
        public TResult? ShowCustomDialog<TDialog, TResult>(TDialog dialog, Func<TDialog, TResult> resultExtractor) 
            where TDialog : Window
            where TResult : class
        {
            if (dialog == null)
                throw new ArgumentNullException(nameof(dialog));

            var result = dialog.ShowDialog();

            if (result == true)
            {
                return resultExtractor(dialog);
            }

            return null;
        }

        /// <summary>
        /// Affiche un dialog personnalisé simple (retourne bool?)
        /// </summary>
        /// <param name="dialog">Instance du dialog</param>
        /// <returns>true si OK/Valider, false si Annuler, null si fermé</returns>
        public bool? ShowCustomDialog(Window dialog)
        {
            if (dialog == null)
                throw new ArgumentNullException(nameof(dialog));

            return dialog.ShowDialog();
        }

        /// <summary>
        /// Affiche une boîte de dialogue de confirmation avec boutons personnalisés
        /// </summary>
        public MessageBoxResult ShowCustomConfirmation(
            string title, 
            string message, 
            MessageBoxButton buttons = MessageBoxButton.YesNoCancel,
            MessageBoxImage image = MessageBoxImage.Question)
        {
            return MessageBox.Show(message, title, buttons, image);
        }

        /// <summary>
        /// Affiche une boîte de dialogue de confirmation simple (OK/Cancel)
        /// </summary>
        public bool ShowOkCancel(string title, string message)
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question
            );

            return result == MessageBoxResult.OK;
        }
    }
}

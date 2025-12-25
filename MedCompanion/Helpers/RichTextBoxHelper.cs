using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MedCompanion.Helpers
{
    /// <summary>
    /// Helper pour permettre le binding du FlowDocument dans un RichTextBox
    /// </summary>
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.RegisterAttached(
                "Document",
                typeof(FlowDocument),
                typeof(RichTextBoxHelper),
                new PropertyMetadata(null, OnDocumentChanged));

        public static FlowDocument GetDocument(DependencyObject obj)
        {
            return (FlowDocument)obj.GetValue(DocumentProperty);
        }

        public static void SetDocument(DependencyObject obj, FlowDocument value)
        {
            obj.SetValue(DocumentProperty, value);
        }

        private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RichTextBox rtb && e.NewValue is FlowDocument doc)
            {
                // Forcer le rafraîchissement en réinitialisant le document
                rtb.Document = new FlowDocument(); // Réinitialiser d'abord
                rtb.Document = doc; // Puis assigner le nouveau document

                System.Diagnostics.Debug.WriteLine($"[RichTextBoxHelper] Document mis à jour - Blocks: {doc.Blocks.Count}");
            }
        }
    }
}

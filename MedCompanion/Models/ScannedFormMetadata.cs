using System;
using System.Collections.Generic;
using System.Windows;

namespace MedCompanion.Models
{
    /// <summary>
    /// Represents a single editable text zone on a scanned PDF form
    /// </summary>
    public class ScannedFormMetadata
    {
        /// <summary>
        /// Page number (1-indexed)
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Rectangle coordinates (X, Y, Width, Height) for the text zone
        /// </summary>
        public Rect Rectangle { get; set; }

        /// <summary>
        /// Placeholder or hint text for this zone
        /// </summary>
        public string PlaceholderText { get; set; } = string.Empty;

        /// <summary>
        /// Actual filled text content (user input or AI-generated)
        /// </summary>
        public string FilledText { get; set; } = string.Empty;

        /// <summary>
        /// Font size for the text in this zone
        /// </summary>
        public double FontSize { get; set; } = 36.0;
    }

    /// <summary>
    /// Container for all text zones in a scanned form
    /// </summary>
    public class ScannedFormMetadataContainer
    {
        /// <summary>
        /// List of all defined text zones
        /// </summary>
        public List<ScannedFormMetadata> Zones { get; set; } = new List<ScannedFormMetadata>();
        
        /// <summary>
        /// Horizontal offset in pixels for calibration (compensates for PDF coordinate misalignment)
        /// </summary>
        public double OffsetX { get; set; } = 0;
        
        /// <summary>
        /// Vertical offset in pixels for calibration (compensates for PDF coordinate misalignment)
        /// </summary>
        public double OffsetY { get; set; } = 0;
    }
}

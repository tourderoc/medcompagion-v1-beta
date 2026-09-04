using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Année scolaire et bascule de rentrée.
    ///
    /// L'année scolaire se CALCULE, elle ne se saisit pas ni ne s'extrait du texte : elle découle
    /// de la date, et la faire deviner par une expression régulière dans d'anciennes notes ramenait
    /// l'année de l'an dernier. C'était le défaut qui imprimait « 2025-2026 » sur une couverture
    /// éditée en septembre 2026.
    ///
    /// L'ÉCOLE ET LA CLASSE, elles, ne se calculent pas. Une rentrée ne veut pas dire CE2 → CM1 :
    /// il y a le redoublement, le changement d'établissement, l'orientation ULIS ou IME, le
    /// déménagement. Les incrémenter automatiquement écrirait une contre-vérité dans la fiche
    /// administrative — qui alimente la couverture du dossier remis à la famille et transmis à
    /// l'école. D'où une confirmation par le médecin, jamais un calcul.
    /// </summary>
    public static class Scolarite
    {
        /// <summary>Mois de la rentrée. Bascule au 1er septembre.</summary>
        public const int MoisRentree = 9;

        /// <summary>« 2026-2027 » pour une date de septembre 2026 à août 2027.</summary>
        public static string AnneeScolaireDe(DateTime date)
            => date.Month >= MoisRentree
                ? $"{date.Year}-{date.Year + 1}"
                : $"{date.Year - 1}-{date.Year}";

        /// <summary>Le 1er septembre le plus récent, à la date donnée incluse.</summary>
        public static DateTime DerniereRentree(DateTime date)
            => new DateTime(date.Month >= MoisRentree ? date.Year : date.Year - 1, MoisRentree, 1);

        /// <summary>
        /// Faut-il redemander l'école et la classe ?
        ///
        /// La condition n'est PAS « on est après le 1er septembre » — la question reviendrait à
        /// chaque séance toute l'année. C'est : la dernière confirmation est antérieure à la
        /// dernière rentrée. La question se pose ainsi exactement une fois par année scolaire et
        /// par patient, à la première séance après la bascule.
        ///
        /// Jamais confirmé (null) compte comme à confirmer : ce sont précisément les dossiers dont
        /// la scolarité n'a jamais été revue.
        /// </summary>
        public static bool DoitConfirmer(DateTime? derniereConfirmation, DateTime aujourdhui)
            => !derniereConfirmation.HasValue
            || derniereConfirmation.Value.Date < DerniereRentree(aujourdhui);
    }
}

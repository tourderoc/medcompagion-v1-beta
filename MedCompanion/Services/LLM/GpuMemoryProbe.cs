using System;
using System.Diagnostics;
using System.Linq;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Lecture de l'occupation VRAM d'un processus via les compteurs Windows.
    ///
    /// Deux mesures, et il faut les DEUX pour conclure : « Dedicated » est la VRAM réelle,
    /// « Shared » ce qui est adressé en mémoire système. Un Shared élevé n'est PAS à lui seul le
    /// signe d'un débordement subi — mesuré sur Gemma 4 : 998 Mo de partagé alors qu'il restait
    /// 6,4 Go de VRAM libre, sans aucune pression (tampons de transfert liés à --no-mmap). On ne
    /// signale donc un débordement que si le partagé est important ET la mémoire dédiée quasi pleine.
    /// </summary>
    public static class GpuMemoryProbe
    {
        private const string Category = "GPU Process Memory";

        /// <summary>Au-delà, le partagé cesse d'être du bruit de fonctionnement.</summary>
        private const long SharedWarnBytes = 400L * 1024 * 1024;

        public readonly record struct Reading(long DedicatedBytes, long SharedBytes, bool Available)
        {
            public double DedicatedMb => DedicatedBytes / 1024.0 / 1024.0;
            public double SharedMb    => SharedBytes    / 1024.0 / 1024.0;
        }

        /// <summary>
        /// Occupation GPU d'un processus. <c>Available = false</c> si les compteurs sont
        /// indisponibles (catégorie absente, droits insuffisants, processus déjà terminé).
        /// </summary>
        public static Reading Read(int processId)
        {
            try
            {
                if (!PerformanceCounterCategory.Exists(Category))
                    return new Reading(0, 0, false);

                var category  = new PerformanceCounterCategory(Category);
                var prefix    = $"pid_{processId}_";
                var instances = category.GetInstanceNames()
                                        .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                                        .ToArray();

                if (instances.Length == 0) return new Reading(0, 0, false);

                long dedicated = 0, shared = 0;
                foreach (var instance in instances)
                {
                    dedicated += ReadCounter("Dedicated Usage", instance);
                    shared    += ReadCounter("Shared Usage", instance);
                }
                return new Reading(dedicated, shared, true);
            }
            catch
            {
                return new Reading(0, 0, false);
            }
        }

        private static long ReadCounter(string counter, string instance)
        {
            try
            {
                // RawValue et non NextValue : ce sont des jauges instantanées, pas des taux — une
                // seconde lecture pour établir un delta serait inutile et fausserait la valeur.
                using var c = new PerformanceCounter(Category, counter, instance, readOnly: true);
                return c.RawValue;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Débordement probable : beaucoup de mémoire partagée ET plus assez de VRAM libre pour
        /// l'expliquer autrement. <paramref name="freeDedicatedBytes"/> vient de la carte, pas du
        /// processus (voir <see cref="TryReadAdapterMemory"/>).
        /// </summary>
        public static bool LooksLikeOverflow(Reading reading, long freeDedicatedBytes)
            => reading.Available
               && reading.SharedBytes > SharedWarnBytes
               && freeDedicatedBytes < SharedWarnBytes;

        /// <summary>
        /// Mémoire totale et libre de la carte, via nvidia-smi. Retourne false si l'outil est absent
        /// (carte non NVIDIA, pilote non installé) — l'appelant se rabat alors sur les seules
        /// mesures par processus.
        /// </summary>
        public static bool TryReadAdapterMemory(out long totalBytes, out long freeBytes)
        {
            totalBytes = 0;
            freeBytes  = 0;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "nvidia-smi",
                    Arguments              = "--query-gpu=memory.total,memory.free --format=csv,noheader,nounits",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;

                var output = p.StandardOutput.ReadLine();
                if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return false; }
                if (string.IsNullOrWhiteSpace(output)) return false;

                var parts = output.Split(',');
                if (parts.Length < 2) return false;
                if (!long.TryParse(parts[0].Trim(), out var totalMb)) return false;
                if (!long.TryParse(parts[1].Trim(), out var freeMb))  return false;

                totalBytes = totalMb * 1024 * 1024;
                freeBytes  = freeMb  * 1024 * 1024;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

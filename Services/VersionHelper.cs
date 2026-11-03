namespace eMRBCS.Services
{
    public static class VersionHelper
    {
        /// <summary>
        /// Returns true only when <paramref name="candidate"/> is strictly greater
        /// than <paramref name="baseline"/> AND the candidate is a full release
        /// (no pre‑release suffix).
        /// </summary>
        public static bool IsGreater(string? candidate, string? baseline)
        {
            // Pre‑release versions (e.g. "1.0.1-1") are never considered for update.
            if (candidate != null && candidate.Contains('-'))
                return false;

            var c = ParseSafe(candidate);
            var b = ParseSafe(baseline);
            if (c is null || b is null) return false;
            return c > b;
        }

        private static Version? ParseSafe(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var core = input.Split('-', '+')[0];

            // System.Version requires at least 2 parts; pad "1" → "1.0".
            if (core.Count(ch => ch == '.') == 0) core += ".0";

            return Version.TryParse(core, out var v) ? v : null;
        }
    }
}

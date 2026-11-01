namespace eMRBCS.Models
{
    public class VersionManifest
    {
        public string VersionName { get; set; } = string.Empty;
        public int VersionCode { get; set; }
        public string ApkFileName { get; set; } = string.Empty;
        public string? ReleaseNotes { get; set; }
    }
}

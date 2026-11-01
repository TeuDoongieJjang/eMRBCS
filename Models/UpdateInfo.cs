namespace eMRBCS.Models
{
    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public int VersionCode { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;
        public string? ReleaseNotes { get; set; }
    }
}

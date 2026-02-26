namespace invoice_v1.src.Application.DTOs
{
    public class DriveFileResult
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string WebViewLink { get; set; } = string.Empty;
        public DateTime? ModifiedTime { get; set; }
    }
}

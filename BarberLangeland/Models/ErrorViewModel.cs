namespace BarberLangeland.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public int StatusCode { get; set; } = 500;

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}

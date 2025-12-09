namespace ARCA_razon_social.Models;

public class AfipCredentials
{
    public string Token { get; set; } = string.Empty;
    public string Sign { get; set; } = string.Empty;
    public DateTime ExpirationTime { get; set; }
}

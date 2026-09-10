using System.Text;
using System.Text.Json;

namespace HR_api.Services;

// Gọi AI backend của bên dev: POST {BaseUrl}/csr/chat  body { question, language } -> { answer }.
// HR_api CHƯA từng gọi HTTP ra ngoài — service này là chỗ duy nhất, bọc kín mọi lỗi để luồng
// chat không bao giờ vỡ (AI lỗi/timeout -> trả (false, "") cho controller tự ghi tin fallback).
public class AiCsrService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseUrl;
    private readonly int _timeoutSec;

    public AiCsrService(IHttpClientFactory httpFactory, IConfiguration cfg)
    {
        _httpFactory = httpFactory;
        _baseUrl     = (cfg["AiCsr:BaseUrl"] ?? "http://192.168.1.90:8002").TrimEnd('/');
        _timeoutSec  = int.TryParse(cfg["AiCsr:TimeoutSec"], out var t) && t > 0 ? t : 20;
    }

    public async Task<(bool ok, string answer)> AskAiAsync(string question, string language)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(_timeoutSec);

            var payload = JsonSerializer.Serialize(new { question, language = string.IsNullOrEmpty(language) ? "vi" : language });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await client.PostAsync($"{_baseUrl}/csr/chat", content);
            if (!resp.IsSuccessStatusCode) return (false, "");

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("answer", out var ansEl))
            {
                var ans = ansEl.GetString();
                if (!string.IsNullOrWhiteSpace(ans)) return (true, ans.Trim());
            }
            return (false, "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiCsrService] AskAiAsync error: {ex.Message}");
            return (false, "");
        }
    }
}

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VehicleService.Infrastructure.Ai;

public interface ILlmClient
{
    bool IsConfigured { get; }
    Task<string?> GenerateResponseAsync(string systemPrompt, string userPrompt);
}

public class LlmClient : ILlmClient
{
    private readonly LlmSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<LlmClient> _logger;

    public LlmClient(IOptions<LlmSettings> options, HttpClient httpClient, ILogger<LlmClient> logger)
    {
        _settings = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey) && _settings.Provider != "BuiltIn";

    public async Task<string?> GenerateResponseAsync(string systemPrompt, string userPrompt)
    {
        if (!IsConfigured) return null;

        try
        {
            if (_settings.Provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                return await CallGeminiAsync(systemPrompt, userPrompt);
            }
            else if (_settings.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return await CallOpenAiAsync(systemPrompt, userPrompt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External LLM call failed. Falling back to built-in RAG synthesizer.");
        }

        return null;
    }

    private async Task<string?> CallGeminiAsync(string systemPrompt, string userPrompt)
    {
        string model = string.IsNullOrWhiteSpace(_settings.Model) ? "gemini-1.5-flash" : _settings.Model;
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_settings.ApiKey}";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = userPrompt } } }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _httpClient.PostAsync(url, content);

        if (!resp.IsSuccessStatusCode) return null;

        var respStr = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respStr);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
    }

    private async Task<string?> CallOpenAiAsync(string systemPrompt, string userPrompt)
    {
        string model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-4o-mini" : _settings.Model;
        string url = "https://api.openai.com/v1/chat/completions";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var payload = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _httpClient.SendAsync(request);

        if (!resp.IsSuccessStatusCode) return null;

        var respStr = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respStr);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }
}

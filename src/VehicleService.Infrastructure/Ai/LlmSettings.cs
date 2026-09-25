namespace VehicleService.Infrastructure.Ai;

public class LlmSettings
{
    public const string SectionName = "AiSettings";

    public string Provider { get; set; } = "BuiltIn"; // "BuiltIn", "Gemini", "OpenAI"
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-1.5-flash";
    public string Endpoint { get; set; } = string.Empty;
}

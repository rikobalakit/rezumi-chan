namespace RezumiChan.Data;

public class AppSettings
{
    public OpenRouterSettings OpenRouter { get; set; }
}

public class OpenRouterSettings
{
    public string ApiKey { get; set; }
}

using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace Aishi;
public sealed class AnomalyRevealConfig
{
    public string RequiredEquippedItem { get; set; } = "2b20c487a99515b22b11ff78";
}

public sealed class AishiDebugConfig
{
    public bool RemoveKTErestrictions { get; set; }
    public bool UnlockAllClothes { get; set; }
    public bool InfiniteAssortStock { get; set; }
    public bool RemoveAllFleaBlacklist { get; set; }
    public double SetInsuranceReturnRate { get; set; } = 80d;
}

[Injectable]
public sealed class AishiAnomalyConfig
{
    public AnomalyRevealConfig Reveal { get; }
    public AishiDebugConfig Debug { get; }

    public AishiAnomalyConfig()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(AishiAnomalyConfig).Assembly.Location)!, "config.jsonc");
        using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        Debug = json.RootElement.TryGetProperty("debug", out var debug) ? debug.Deserialize<AishiDebugConfig>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new() : new();
        if (!double.IsFinite(Debug.SetInsuranceReturnRate) || Debug.SetInsuranceReturnRate < 0d || Debug.SetInsuranceReturnRate > 100d)
            throw new InvalidDataException("Aishi debug.setInsuranceReturnRate must be a percentage between 0 and 100.");
        Reveal = json.RootElement.GetProperty("anomalousItems").Deserialize<AnomalyRevealConfig>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Aishi anomalousItems configuration is missing.");
        if (string.IsNullOrWhiteSpace(Reveal.RequiredEquippedItem) || Reveal.RequiredEquippedItem.Length != 24 || !Reveal.RequiredEquippedItem.All(Uri.IsHexDigit))
            throw new InvalidDataException("Aishi anomalousItems.requiredEquippedItem must be a valid 24-character item template ID.");
    }
}

[Injectable]
public sealed class AishiAnomalyRouter(JsonUtil json, AishiAnomalyReveal reveal) : StaticRouter(json, [new RouteAction("/aishi/anomalous-items/config", async (url, info, sessionId, output, cancellationToken) => JsonSerializer.Serialize(await reveal.Settings(sessionId, cancellationToken))), new RouteAction("/aishi/anomalous-items/reveal", async (url, info, sessionId, output, cancellationToken) => await reveal.Reveal(sessionId, info, cancellationToken), typeof(AishiAnomalyReveal.Request))]);

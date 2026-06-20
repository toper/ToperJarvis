using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace ToperJarvis.Core.Agent;

/// <summary>
/// Planer agenta — rozbija cel użytkownika na maks. kilka niezależnych kroków, korzystając z LLM.
/// </summary>
public sealed partial class Planner(IChatClient chat)
{
    private const string PlannerPrompt =
        "Jesteś modułem planującym asystenta. Rozbij cel użytkownika na sekwencję maksymalnie 5 " +
        "krótkich, niezależnych kroków. Każdy krok to jedno zwięzłe polecenie po polsku. " +
        "Zwróć WYŁĄCZNIE tablicę JSON stringów, bez komentarzy, np. [\"krok 1\", \"krok 2\"].";

    [GeneratedRegex(@"```(?:json)?|```")]
    private static partial Regex CodeFence();

    public async Task<IReadOnlyList<string>> PlanAsync(string goal, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, PlannerPrompt),
            new(ChatRole.User, goal),
        };

        var response = await chat.GetResponseAsync(messages, cancellationToken: ct);
        var steps = ParsePlan(response.Text);
        return steps.Count > 0 ? steps : new[] { goal };
    }

    /// <summary>Parsuje odpowiedź LLM na listę kroków (tablica JSON stringów lub obiektów).</summary>
    internal static IReadOnlyList<string> ParsePlan(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var text = CodeFence().Replace(raw, string.Empty).Trim();

        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
            return Array.Empty<string>();

        var json = text[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var steps = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var step = item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString(),
                    JsonValueKind.Object => ExtractStepText(item),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(step))
                    steps.Add(step!.Trim());
            }

            return steps.Take(5).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? ExtractStepText(JsonElement obj)
    {
        foreach (var name in new[] { "step", "description", "opis", "krok", "task" })
            if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }
}

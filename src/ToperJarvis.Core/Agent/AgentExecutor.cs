using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Agent;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Core.Agent;

/// <summary>
/// Wykonawca planu agenta — dla każdego kroku zleca jego realizację modelowi LLM (z dostępem do
/// narzędzi). Zbiera krótkie podsumowanie efektów.
/// </summary>
public sealed class AgentExecutor
{
    private readonly Planner _planner;
    private readonly IChatClient _chat;
    private readonly ChatOptions _chatOptions;
    private readonly ILogger<AgentExecutor> _logger;

    public AgentExecutor(
        Planner planner,
        IChatClient chat,
        IEnumerable<IJarvisTool> tools,
        ILogger<AgentExecutor> logger)
    {
        _planner = planner;
        _chat = chat;
        _logger = logger;
        _chatOptions = new ChatOptions
        {
            Tools = tools.Select(t => (AITool)t.AsAIFunction()).ToList(),
        };
    }

    public async Task<string> ExecuteAsync(AgentTask task, CancellationToken ct = default)
    {
        var plan = await _planner.PlanAsync(task.Goal, ct);
        _logger.LogInformation("Plan dla '{Goal}': {Count} kroków.", task.Goal, plan.Count);

        var summary = new StringBuilder();
        for (var i = 0; i < plan.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = plan[i];
            try
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System,
                        "Wykonaj polecenie użytkownika, korzystając z dostępnych narzędzi. " +
                        "Odpowiedz jednym zwięzłym zdaniem o efekcie."),
                    new(ChatRole.User, step),
                };
                var response = await _chat.GetResponseAsync(messages, _chatOptions, ct);
                summary.AppendLine($"{i + 1}. {step} → {response.Text.Trim()}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Błąd kroku {Step}.", i + 1);
                summary.AppendLine($"{i + 1}. {step} → błąd: {ex.Message}");
            }
        }

        return summary.ToString().TrimEnd();
    }
}

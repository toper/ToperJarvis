namespace ToperJarvis.Abstractions.Agent;

public enum AgentTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}

/// <summary>Priorytet zadania — niższa wartość = wyższy priorytet (port z oryginału).</summary>
public enum AgentTaskPriority
{
    High = 1,
    Normal = 2,
    Low = 3,
}

/// <summary>Wielokrokowe zadanie agenta (cel rozbijany na kroki przez planera).</summary>
public sealed class AgentTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public required string Goal { get; init; }
    public AgentTaskPriority Priority { get; init; } = AgentTaskPriority.Normal;

    /// <summary>Kolejność zgłoszenia — tie-breaker przy równym priorytecie (FIFO).</summary>
    public long Sequence { get; set; }

    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;
    public string Result { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

/// <summary>Usługa agenta — przyjmuje cele i wykonuje je wielokrokowo w tle.</summary>
public interface IAgentService
{
    /// <summary>Zgłasza cel do wykonania; zwraca identyfikator zadania.</summary>
    string Submit(string goal, AgentTaskPriority priority = AgentTaskPriority.Normal);

    /// <summary>Zgłaszane przy zmianie statusu zadania.</summary>
    event EventHandler<AgentTask>? TaskChanged;
}

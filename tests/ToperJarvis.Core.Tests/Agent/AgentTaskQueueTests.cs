using ToperJarvis.Abstractions.Agent;
using ToperJarvis.Core.Agent;

namespace ToperJarvis.Core.Tests.Agent;

public class AgentTaskQueueTests
{
    [Fact]
    public void SelectNext_wybiera_najwyzszy_priorytet()
    {
        var tasks = new[]
        {
            new AgentTask { Goal = "low", Priority = AgentTaskPriority.Low, Sequence = 1 },
            new AgentTask { Goal = "high", Priority = AgentTaskPriority.High, Sequence = 2 },
            new AgentTask { Goal = "normal", Priority = AgentTaskPriority.Normal, Sequence = 3 },
        };

        Assert.Equal("high", AgentTaskQueue.SelectNext(tasks)!.Goal);
    }

    [Fact]
    public void SelectNext_przy_remisie_priorytetu_FIFO()
    {
        var tasks = new[]
        {
            new AgentTask { Goal = "drugi", Priority = AgentTaskPriority.Normal, Sequence = 5 },
            new AgentTask { Goal = "pierwszy", Priority = AgentTaskPriority.Normal, Sequence = 2 },
        };

        Assert.Equal("pierwszy", AgentTaskQueue.SelectNext(tasks)!.Goal);
    }

    [Fact]
    public void SelectNext_pusta_kolejka_zwraca_null()
    {
        Assert.Null(AgentTaskQueue.SelectNext(Array.Empty<AgentTask>()));
    }
}

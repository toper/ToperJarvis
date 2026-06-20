using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Agent;

namespace ToperJarvis.Core.Agent;

/// <summary>
/// Kolejka zadań agenta z priorytetami, wykonywanych pojedynczo w tle (port <c>task_queue.py</c>).
/// Wyższy priorytet (niższa wartość) i wcześniejsze zgłoszenie są obsługiwane jako pierwsze.
/// </summary>
public sealed class AgentTaskQueue : IAgentService, IDisposable
{
    private readonly AgentExecutor _executor;
    private readonly ILogger<AgentTaskQueue> _logger;

    private readonly List<AgentTask> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private long _sequence;

    public AgentTaskQueue(AgentExecutor executor, ILogger<AgentTaskQueue> logger)
    {
        _executor = executor;
        _logger = logger;
        _worker = Task.Run(WorkerLoopAsync);
    }

    public event EventHandler<AgentTask>? TaskChanged;

    public string Submit(string goal, AgentTaskPriority priority = AgentTaskPriority.Normal)
    {
        var task = new AgentTask
        {
            Goal = goal,
            Priority = priority,
            Sequence = Interlocked.Increment(ref _sequence),
        };

        lock (_lock)
            _pending.Add(task);

        _signal.Release();
        Raise(task);
        return task.Id;
    }

    private async Task WorkerLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            AgentTask? task;
            lock (_lock)
            {
                task = SelectNext(_pending);
                if (task is not null)
                    _pending.Remove(task);
            }

            if (task is null)
                continue;

            await RunAsync(task);
        }
    }

    private async Task RunAsync(AgentTask task)
    {
        task.Status = AgentTaskStatus.Running;
        Raise(task);
        try
        {
            task.Result = await _executor.ExecuteAsync(task, _cts.Token);
            task.Status = AgentTaskStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            task.Status = AgentTaskStatus.Failed;
            task.Error = "Anulowano.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zadanie agenta {Id} nie powiodło się.", task.Id);
            task.Status = AgentTaskStatus.Failed;
            task.Error = ex.Message;
        }

        Raise(task);
    }

    /// <summary>
    /// Wybiera następne zadanie jednoprzebiegowo (O(n)): najwyższy priorytet, a przy remisie
    /// najwcześniejsze (FIFO po <see cref="AgentTask.Sequence"/>).
    /// </summary>
    internal static AgentTask? SelectNext(IEnumerable<AgentTask> tasks)
    {
        AgentTask? best = null;
        foreach (var t in tasks)
        {
            if (best is null
                || (int)t.Priority < (int)best.Priority
                || ((int)t.Priority == (int)best.Priority && t.Sequence < best.Sequence))
            {
                best = t;
            }
        }
        return best;
    }

    private void Raise(AgentTask task) => TaskChanged?.Invoke(this, task);

    public void Dispose()
    {
        _cts.Cancel();
        // Czekamy na faktyczne zakończenie workera PRZED zwolnieniem zasobów — inaczej grozi to
        // użyciem zdisposowanego _cts/_signal w tle.
        try { _worker.Wait(); } catch { /* anulowanie/agregacja — ignorujemy */ }
        _cts.Dispose();
        _signal.Dispose();
    }
}

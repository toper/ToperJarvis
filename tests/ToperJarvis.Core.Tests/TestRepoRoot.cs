namespace ToperJarvis.Core.Tests;

/// <summary>
/// Wspólny helper testowy: znajduje korzeń repozytorium (katalog z <c>.git</c> lub plikiem
/// <c>*.sln</c>), idąc w górę od katalogu wyjściowego testhosta (<see cref="AppContext.BaseDirectory"/>
/// wskazuje na <c>bin/Debug/net10.0</c>, nie na cwd repo). Używane przez testy modeli ONNX,
/// które muszą trafić do <c>assets/...</c> względem korzenia repo.
/// </summary>
internal static class TestRepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                dir.GetFiles("*.sln").Length > 0)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        // Fallback: cwd (zachowanie sprzed fixa).
        return Directory.GetCurrentDirectory();
    }
}

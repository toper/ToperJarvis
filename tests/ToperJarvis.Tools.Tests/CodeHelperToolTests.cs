using ToperJarvis.Tools.Dev;

namespace ToperJarvis.Tools.Tests;

public class CodeHelperToolTests
{
    [Theory]
    [InlineData("```python\nprint(1)\n```", "print(1)")]
    [InlineData("```\nx = 1\n```", "x = 1")]
    [InlineData("kod bez płotków", "kod bez płotków")]
    [InlineData("  ```js\nconst a=1;\n```  ", "const a=1;")]
    public void CleanCode_usuwa_plotki_markdown(string input, string expected)
    {
        Assert.Equal(expected, CodeHelperTool.CleanCode(input));
    }

    [Theory]
    [InlineData("python", ".py")]
    [InlineData("javascript", ".js")]
    [InlineData("rust", ".rs")]
    [InlineData("nieznany", ".py")] // fallback
    public void ResolveSavePath_domyslna_nazwa_wg_jezyka(string language, string expectedExt)
    {
        var path = CodeHelperTool.ResolveSavePath(null, language);

        Assert.EndsWith($"jarvis_code{expectedExt}", path);
    }

    [Fact]
    public void ResolveSavePath_absolutna_sciezka_bez_zmian()
    {
        var abs = OperatingSystem.IsWindows() ? @"C:\tmp\x.py" : "/tmp/x.py";

        Assert.Equal(abs, CodeHelperTool.ResolveSavePath(abs, "python"));
    }

    [Theory]
    [InlineData("Traceback (most recent call last): NameError", true)]
    [InlineData("Exception in thread", true)]
    [InlineData("Wszystko OK, gotowe", false)]
    [InlineData("Hello world", false)]
    public void HasError_wykrywa_sygnaly_bledu(string output, bool expected)
    {
        Assert.Equal(expected, CodeHelperTool.HasError(output));
    }

    [Theory]
    // (description, filePath, code, expected)
    [InlineData("zoptymalizuj ten kod", null, "print(1)", "optimize")]
    [InlineData("napisz kalkulator", null, null, "write")]
    [InlineData("zbuduj grę w snake", null, null, "build")]
    [InlineData("wyjaśnij ten kod", null, "print(1)", "explain")]
    public void DetectIntent_routuje_zamiar(string desc, string? filePath, string? code, string expected)
    {
        Assert.Equal(expected, CodeHelperTool.DetectIntent(desc, filePath, code));
    }
}

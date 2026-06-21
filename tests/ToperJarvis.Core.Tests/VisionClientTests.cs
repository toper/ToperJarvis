using System.Text.Json;
using ToperJarvis.Abstractions.Vision;
using ToperJarvis.Llm;

namespace ToperJarvis.Core.Tests;

public class VisionClientTests
{
    private static readonly VisionImage SampleImage = new([1, 2, 3, 4], "image/png");

    [Fact]
    public void BuildRequestJson_zawiera_model_prompt_i_parametry()
    {
        var json = VisionClient.BuildRequestJson("qwen-vl", "Co widzisz?", [SampleImage], 512);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("qwen-vl", root.GetProperty("model").GetString());
        Assert.Equal(512, root.GetProperty("max_tokens").GetInt32());
        // Myślenie musi być wyłączone — bez tego model rozumujący zwraca puste content.
        Assert.False(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    }

    [Fact]
    public void BuildRequestJson_buduje_tresc_multimodalna_tekst_plus_obraz()
    {
        var json = VisionClient.BuildRequestJson("m", "Opisz", [SampleImage], 256);

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("Opisz", content[0].GetProperty("text").GetString());
        Assert.Equal("image_url", content[1].GetProperty("type").GetString());

        var expectedUri = "data:image/png;base64," + Convert.ToBase64String(SampleImage.Data);
        Assert.Equal(expectedUri, content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public void BuildRequestJson_dodaje_kazdy_obraz_jako_osobna_czesc()
    {
        var images = new[] { SampleImage, new VisionImage([9, 9], "image/jpeg") };

        var json = VisionClient.BuildRequestJson("m", "x", images, 100);

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal(3, content.GetArrayLength()); // tekst + 2 obrazy
    }

    [Fact]
    public void ParseContent_wyciaga_tresc_odpowiedzi()
    {
        const string response =
            """{"choices":[{"message":{"role":"assistant","content":"  czerwony kwadrat  "}}]}""";

        Assert.Equal("czerwony kwadrat", VisionClient.ParseContent(response));
    }

    [Fact]
    public void ParseContent_zwraca_null_gdy_content_jest_null()
    {
        // Przypadek modelu rozumującego, który zużył budżet na reasoning.
        const string response =
            """{"choices":[{"message":{"role":"assistant","content":null,"reasoning":"myślę..."}}]}""";

        Assert.Null(VisionClient.ParseContent(response));
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":""}}]}""")]
    [InlineData("""{"error":{"message":"bad request"}}""")]
    public void ParseContent_zwraca_null_dla_braku_tresci(string response)
    {
        Assert.Null(VisionClient.ParseContent(response));
    }
}

using ToperJarvis.Llm;

namespace ToperJarvis.Core.Tests;

public class LlmHealthCheckTests
{
    [Theory]
    [InlineData("http://192.168.7.30:8000/v1", "http://192.168.7.30:8000/v1/models")]
    [InlineData("http://192.168.7.30:8000/v1/", "http://192.168.7.30:8000/v1/models")] // ucięcie końcowego /
    public void BuildModelsUrl_dokleja_models(string baseUrl, string expected)
    {
        Assert.Equal(expected, LlmHealthCheck.BuildModelsUrl(baseUrl));
    }
}

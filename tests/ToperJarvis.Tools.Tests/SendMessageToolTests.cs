using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Tools.System;

namespace ToperJarvis.Tools.Tests;

public class SendMessageToolTests
{
    [Theory]
    [InlineData("whatsapp", "WhatsApp")]
    [InlineData("wp", "WhatsApp")]
    [InlineData("tg", "Telegram")]
    [InlineData("Telegram", "Telegram")]
    [InlineData("signal", "Signal")]
    [InlineData("Discord", "Discord")]
    public void ResolvePlatform_aplikacje_desktopowe(string input, string target)
    {
        var spec = SendMessageTool.ResolvePlatform(input);

        Assert.Equal(SendMessageTool.Channel.DesktopApp, spec.Channel);
        Assert.Equal(target, spec.Target);
    }

    [Theory]
    [InlineData("instagram", "https://www.instagram.com/direct/new/")]
    [InlineData("ig", "https://www.instagram.com/direct/new/")]
    [InlineData("messenger", "https://www.messenger.com/")]
    [InlineData("fb", "https://www.messenger.com/")]
    public void ResolvePlatform_komunikatory_webowe(string input, string url)
    {
        var spec = SendMessageTool.ResolvePlatform(input);

        Assert.Equal(SendMessageTool.Channel.Browser, spec.Channel);
        Assert.Equal(url, spec.Target);
    }

    [Fact]
    public void ResolvePlatform_nieznana_platforma_jako_aplikacja()
    {
        var spec = SendMessageTool.ResolvePlatform("slack");

        Assert.Equal(SendMessageTool.Channel.DesktopApp, spec.Channel);
        Assert.Equal("Slack", spec.Target);
    }

    [Fact]
    public void Name_jest_send_message()
    {
        var tool = new SendMessageTool(NullLogger<SendMessageTool>.Instance);

        Assert.Equal("send_message", tool.Name);
    }
}

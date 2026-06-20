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
    [InlineData("instagram")]
    [InlineData("ig")]
    [InlineData("insta")]
    public void ResolvePlatform_instagram(string input)
    {
        var spec = SendMessageTool.ResolvePlatform(input);

        Assert.Equal(SendMessageTool.Channel.Instagram, spec.Channel);
        Assert.Equal("https://www.instagram.com/direct/new/", spec.Target);
    }

    [Theory]
    [InlineData("messenger")]
    [InlineData("fb")]
    [InlineData("facebook")]
    public void ResolvePlatform_messenger(string input)
    {
        var spec = SendMessageTool.ResolvePlatform(input);

        Assert.Equal(SendMessageTool.Channel.Messenger, spec.Channel);
        Assert.Equal("https://www.messenger.com/", spec.Target);
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

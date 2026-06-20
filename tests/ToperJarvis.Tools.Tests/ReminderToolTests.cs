using ToperJarvis.Tools.System;

namespace ToperJarvis.Tools.Tests;

public class ReminderToolTests
{
    [Fact]
    public void TryParseDue_poprawny_format()
    {
        Assert.True(ReminderTool.TryParseDue("2026-12-31", "18:30", out var due));
        Assert.Equal(new DateTime(2026, 12, 31, 18, 30, 0), due);
    }

    [Theory]
    [InlineData("31-12-2026", "18:30")]   // zła kolejność daty
    [InlineData("2026-12-31", "6:30 PM")] // zły format godziny
    [InlineData("2026-13-01", "10:00")]   // nieistniejący miesiąc
    [InlineData("", "10:00")]             // brak daty
    public void TryParseDue_niepoprawny_format(string date, string time)
    {
        Assert.False(ReminderTool.TryParseDue(date, time, out _));
    }
}

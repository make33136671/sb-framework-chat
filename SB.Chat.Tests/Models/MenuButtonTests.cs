using SB.Chat.Models;

namespace SB.Chat.Tests.Models;

public class MenuButtonTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var btn = new MenuButton("Click Me", "action:click");

        Assert.Equal("Click Me", btn.Text);
        Assert.Equal("action:click", btn.CallbackData);
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var btn = new MenuButton("A", "B");
        btn.Text = "Updated";
        btn.CallbackData = "new:data";

        Assert.Equal("Updated", btn.Text);
        Assert.Equal("new:data", btn.CallbackData);
    }
}

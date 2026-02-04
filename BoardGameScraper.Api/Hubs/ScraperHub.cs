using Microsoft.AspNetCore.SignalR;

namespace BoardGameScraper.Api.Hubs;

public class ScraperHub : Hub
{
    public async Task SendLog(string message)
    {
        await Clients.All.SendAsync("ReceiveLog", message, DateTime.UtcNow);
    }
}

using System.Collections.Generic;

namespace NitroSniper
{
    public struct SlaveToken
    {
        public bool IsBotAccount { get; set; }
        public string Token { get; set; }
    }
    public class Config
    {
        public string Master { get; set; }
        public IList<SlaveToken> Slaves { get; set; }
        public string UserAgent { get; set; }
        public string Webhook { get; set; }
    }

    public class DiscordResponse
    {
        public bool Consumed { get; set; }
        public string Message { get; set; }
        public IDictionary<string, string> Subscription_Plan { get; set; }
    }

    public class WebhookBody
    {
        public string avatar_url;
        public IList<object> embeds;
        public string username;
        public WebhookBody(string username, string avatarUrl, string description, int color)
        {
            this.username = username;
            avatar_url = avatarUrl;
            embeds = new List<object> { new { description, color } };
        }
    }
}
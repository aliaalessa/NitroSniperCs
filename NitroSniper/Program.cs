using DSharpPlus;
using DSharpPlus.EventArgs;
using Newtonsoft.Json;
using Pastel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NitroSniper
{
    public class Program
    {
        private const string banner = @"
        ███╗   ██╗██╗████████╗██████╗  ██████╗ ███████╗███╗   ██╗██╗██████╗ ███████╗██████╗
        ████╗  ██║██║╚══██╔══╝██╔══██╗██╔═══██╗██╔════╝████╗  ██║██║██╔══██╗██╔════╝██╔══██╗
        ██╔██╗ ██║██║   ██║   ██████╔╝██║   ██║███████╗██╔██╗ ██║██║██████╔╝█████╗  ██████╔╝
        ██║╚██╗██║██║   ██║   ██╔══██╗██║   ██║╚════██║██║╚██╗██║██║██╔═══╝ ██╔══╝  ██╔══██╗
        ██║ ╚████║██║   ██║   ██║  ██║╚██████╔╝███████║██║ ╚████║██║██║     ███████╗██║  ██║
        ╚═╝  ╚═══╝╚═╝   ╚═╝   ╚═╝  ╚═╝ ╚═════╝ ╚══════╝╚═╝  ╚═══╝╚═╝╚═╝     ╚══════╝╚═╝  ╚═╝
";

        private const int COLOR_CYAN = 0x00ffff;
        private const int COLOR_GREEN = 0x39ff14;
        private const int COLOR_RED = 0xff073a;
        private const string CONFIG_FILE_NAME = "config.json";
        private const string LOG_FILE_NAME = "attempted_codes.log";
        private readonly Regex _ansiMatcher = new Regex(@"\x1B\[[^@-~]*[@-~]", RegexOptions.Compiled);
        private readonly IList<string> _attemptedCodes;
        private readonly HttpClient _client;
        private readonly Config _config;
        private readonly Regex _hyperlinkMatcher = new Regex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
        private readonly Regex _nitroMatcher = new Regex(@"(discord(app)?\.com/gifts/|discord\.gift/)(?<code>[a-zA-Z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly StringContent _requestBody = new StringContent("{\"channel_id\":null,\"payment_source_id\":null}", Encoding.UTF8, "application/json");
        private readonly HttpClient _webhookClient;

        public Program()
        {
            _attemptedCodes = File.Exists(LOG_FILE_NAME) ? File.ReadAllLines(LOG_FILE_NAME).ToList() : new List<string>();
            _config = File.Exists(CONFIG_FILE_NAME) ? File.ReadAllText(CONFIG_FILE_NAME).GetAsJson<Config>() : null;

            if (_config is null || _config.Slaves.Count == 0 || string.IsNullOrEmpty(_config.Master) || string.IsNullOrEmpty(_config.UserAgent))
            {
                Exit($"Invalid config. Make sure the file {CONFIG_FILE_NAME} exists and has values for master, userAgent and at least one slave token", 1);
            }

            _client = new HttpClient
            {
                BaseAddress = new Uri("https://discord.com/api/v8/entitlements/gift-codes/")
            };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(_config.Master);
            _client.DefaultRequestHeaders.Add("User-Agent", _config.UserAgent);

            if (!string.IsNullOrEmpty(_config.Webhook))
            {
                _webhookClient = new HttpClient
                {
                    BaseAddress = new Uri(_config.Webhook)
                };
            }
        }

        public static void Main()
        {
            Console.WriteLine(banner.Pastel(Color.CornflowerBlue));

            new Program().InitAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public async Task InitAsync()
        {
            foreach (var slave in _config.Slaves)
            {
                try
                {
                    var client = new DiscordClient(new DiscordConfiguration()
                    {
                        Token = slave.Token,
                        TokenType = slave.IsBotAccount ? TokenType.Bot : TokenType.User
                    });

                    await client.ConnectAsync().ConfigureAwait(false);

                    client.Ready += ReadyHandler;
                    client.MessageCreated += MessageHandler;
                }
                catch
                {
                    Exit("INVALID SLAVE TOKEN ".Pastel(Color.Red) + slave.Token.Pastel(Color.CornflowerBlue), 1, false);
                }
            }
            await Task.Delay(-1).ConfigureAwait(false);
        }

        private void Exit(string message = "", int exitCode = 0, bool colourRed = true)
        {
            if (!string.IsNullOrEmpty(message))
            {
                if (colourRed)
                {
                    message = message.Pastel(Color.Red);
                }
                Console.WriteLine(message);
            }

            Console.WriteLine("\nPress any key to close this window...");
            Console.ReadKey();
            Environment.Exit(exitCode);
        }

        private async Task LogAsync(string text, BaseDiscordClient client, int hex)
        {
            Console.WriteLine($"{DateTime.Now.ToString("T").Pastel(Color.LightBlue)} - {_hyperlinkMatcher.Replace(text, "$1")}");

            if (_webhookClient != null)
            {
                try
                {
                    var body = new WebhookBody(client.CurrentUser.GetTag(), client.CurrentUser.GetAvatarUrl(ImageFormat.WebP), _ansiMatcher.Replace(text, ""), hex);
                    var strBody = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                    await _webhookClient.PostAsync("", strBody).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        private Task MessageHandler(MessageCreateEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                foreach (Match match in _nitroMatcher.Matches(e.Message.Content))
                {
                    string code = match.Groups["code"].Value;

                    if (_attemptedCodes.Contains(code))
                    {
                        await LogAsync($"Found duplicate code {code}. Skipping".Pastel(Color.LightSkyBlue), e.Client, COLOR_CYAN);
                        continue;
                    }

                    var req = new HttpRequestMessage(HttpMethod.Post, $"{code}/redeem")
                    {
                        Content = _requestBody
                    };

                    var res = await _client.SendAsync(req).ConfigureAwait(false);

                    _attemptedCodes.Add(code);
                    using (var w = File.AppendText(LOG_FILE_NAME))
                    {
                        w.WriteLine(code);
                    }

                    try
                    {
                        string rawJson = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var json = rawJson.GetAsJson<DiscordResponse>();

                        string channel = $" [{(e.Guild == null ? e.Author.GetTag() : $"{e.Guild.Name} - { e.Channel.Name}")}]";

                        string output = "Found code ".Pastel(Color.CornflowerBlue) + code.Pastel(Color.Cyan) + channel.Pastel(Color.LightSkyBlue);
                        if (json.Consumed)
                        {
                            await LogAsync(output + $" Enjoy your {json.Subscription_Plan["name"]} :DD".Pastel(Color.LightGreen), e.Client, COLOR_GREEN).ConfigureAwait(false);
                        }
                        else
                        {
                            await LogAsync(output + $" Failed to redeem: {json.Message}".Pastel(Color.Red), e.Client, COLOR_RED).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Something went wrong while redeeming code ".Pastel(Color.Red) + code.Pastel(Color.CornflowerBlue) + "\nPlease open an issue on https://github.com/Vendicated/NitroSniperCs/issues with a screenshot of this.");
                        Console.WriteLine(ex);
                    }
                }
            });

            return Task.CompletedTask;
        }

        private async Task ReadyHandler(ReadyEventArgs e)
        {
            await LogAsync("Successfully connected to slave ".Pastel(Color.CornflowerBlue) + e.Client.CurrentUser.Username.Pastel(Color.Cyan) + $" with {e.Client.Guilds.Count} servers".Pastel(Color.CornflowerBlue), e.Client, COLOR_CYAN).ConfigureAwait(false);
        }
    }
}
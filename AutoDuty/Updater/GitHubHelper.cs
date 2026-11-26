using ECommons.DalamudServices;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dalamud.Networking.Http;
using AutoDuty.Logging;

namespace AutoDuty.Updater
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Threading.Tasks;

    internal static class GitHubHelper
    {
        const string CLIENT_ID = "Iv23liWV5R21nasKaQjP";

        private static readonly SocketsHttpHandler _handler = new() { AutomaticDecompression = DecompressionMethods.All, ConnectCallback = new HappyEyeballsCallback().ConnectCallback };

        private static readonly HttpClient _client = new(_handler) { Timeout = TimeSpan.FromSeconds(20) };

        public static readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true, IgnoreReadOnlyProperties = true, IncludeFields = true };

        internal static async Task<bool> DownloadFileAsync(string url, string localPath)
        {
            try
            {
                HttpResponseMessage response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync();
                await File.WriteAllTextAsync(localPath, content);
                return true;
            }
            catch (Exception ex)
            {
                ADLogging.Error($"{ex}");
                return false;
            }
        }

        internal static async Task<(Dictionary<string, string>?, Dictionary<string, string>?)> GetPathUpdateInfoAsync()
        {
            try
            {
                // Temporary handler and client, to avoid default headers below
                using SocketsHttpHandler handler = new();
                handler.AutomaticDecompression = DecompressionMethods.All;
                handler.ConnectCallback = new HappyEyeballsCallback().ConnectCallback;
                using HttpClient client = new(handler);
                client.Timeout = TimeSpan.FromSeconds(20);

                Dictionary<string, string>? md5List = await client.GetFromJsonAsync<Dictionary<string, string>>("https://raw.githubusercontent.com/erdelf/AutoDuty/refs/heads/master/AutoDuty/Resources/md5s.json");
                Dictionary<string, string>? delList = await client.GetFromJsonAsync<Dictionary<string, string>>("https://raw.githubusercontent.com/erdelf/AutoDuty/refs/heads/master/AutoDuty/Resources/md5s_Removed.json");
                return (md5List ?? [], delList ?? []);
            }
            catch (Exception ex)
            {
                ADLogging.Error($"{ex}");
                return (null, null);
            }
        }

        internal static async Task<UserCode?> GetUserCode()
        {
            try
            {
                Uri?                   uri        = new Uri("https://github.com/login/device/code");
                FormUrlEncodedContent? parameters = new FormUrlEncodedContent([new KeyValuePair<string, string>("client_id", CLIENT_ID)]);
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage? response   = await _client.PostAsync(uri, parameters);
                string?              jsonString = await response.Content.ReadAsStringAsync();
                _client.Dispose();
                return JsonSerializer.Deserialize<UserCode>(jsonString, jsonSerializerOptions);
            }
            catch (Exception ex)
            {
                ADLogging.Error($"{ex}");
                return null;
            }
        }

        internal static async Task<PollResponseClass?> PollResponse(UserCode userCode)
        {
            try
            {
                Uri? uri = new Uri("https://github.com/login/oauth/access_token");
                FormUrlEncodedContent? parameters = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("client_id", CLIENT_ID),
                    new KeyValuePair<string, string>("device_code", userCode.Device_Code),
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
                ]);
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage? response   = await _client.PostAsync(uri, parameters);
                string?              jsonString = await response.Content.ReadAsStringAsync();
                _client.Dispose();
                return JsonSerializer.Deserialize<PollResponseClass>(jsonString, jsonSerializerOptions);
            }
            catch (Exception ex)
            {
                ADLogging.Error($"{ex}");
                return null;
            }
        }

        internal static async Task<string?> FileIssue(string title, string whatHappened, string reproSteps, string accessToken)
        {
            try
            {
                string? body = $"What Happened?\n\n{whatHappened}\n\nVersion Number\n\n{GitHubIssue.Version}\n\nSteps to reproduce the error\n\n{reproSteps}\n\nRelevant log output\n\n{GitHubIssue.LogFile}\n\nOther relevant plugins installed\n\n{GitHubIssue.InstalledPlugins}\n\nConfig file\n\n{GitHubIssue.ConfigFile}";

                GitHubIssue? issue = new GitHubIssue()
                                     {
                                         Title = title,
                                         Body = body
                                     };

                string? json = JsonSerializer.Serialize(issue, jsonSerializerOptions);
                ADLogging.Info(json);
                _client.DefaultRequestHeaders.Add("User-Agent", "AutoDuty");
                _client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
                _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

                StringContent? content = new StringContent(json, Encoding.UTF8, "application/vnd.github+json");

                string?              url      = $"https://api.github.com/repos/erdelf/AutoDuty/issues";
                HttpResponseMessage? response = await _client.PostAsync(url, content);

                string? responseString = await response.Content.ReadAsStringAsync();
                return responseString;
            }
            catch (Exception ex)
            {
                ADLogging.Error($"{ex}");
                return null;
            }
        }

        internal static        void                  Dispose() => _client.Dispose();
    }
}

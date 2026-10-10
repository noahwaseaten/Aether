using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Core.Helpers
{
    public sealed class ServerManager
    {
        public enum Command
        {
            ALIVE,
            HELLO,
            DONE,
            CHECK,
            PUSH,
            ELEVATE,
            DAMAGE,
            PULL
        }

        public int IsServerOline = 0;

        private string Version
        {
            get
            {
                return "1.2-1";
            }
        }
        private static string commandToStr(Command cmd)
        {
            if (cmd == Command.ALIVE)
            {
                return "alive";
            }
            else if (cmd == Command.HELLO)
            {
                return "hello";
            }else if (cmd == Command.DONE)
            {
                return "done";
            }
            else if (cmd == Command.CHECK)
            {
                return "check";
            }
            else if (cmd == Command.PUSH)
            {
                return "push";
            }
            else if (cmd == Command.PULL)
            {
                return "pull";
            }
            else if (cmd == Command.ELEVATE)
            {
                return "elevate";
            }
            else if (cmd == Command.DAMAGE)
            {
                return "damage";
            }
            return "";
        }

        private static readonly ServerManager instance = new ServerManager();

        // One client for every request: a new one per request (about one a second in a party) opened a new connection
        // each time. The short timeout keeps one stuck reply from stalling party sync for the default 100 s.
        static readonly HttpClient s_Client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        public static Dictionary<Command, long[]> Stats { get; set; }

        static ServerManager()
        {
            if (ConfigHelper.Main.Values.IgnoreHttpsErrors)
            {
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            }

            Stats = new Dictionary<Command, long[]>();
            foreach (var cmd in (Command[]) Enum.GetValues(typeof(Command)))
            {
                long[] s = new long[5];
                for (int i = 0; i < 5; i++)
                {
                    s[i] = 0;
                }
                Stats[cmd] = s;
            }
        }

        public void ResetStats()
        {
            foreach (var cmd in (Command[])Enum.GetValues(typeof(Command)))
            {
                for (int i = 0; i < 5; i++)
                {
                    Stats[cmd][i] = 0;
                }
            }
        }

        private string ConvertSize(long bytes)
        {
            string tmp = "byte";
            if (bytes >= 1024)
            {
                bytes /= 1024;
                tmp = "KB";
            }
            if (bytes >= 1024)
            {
                bytes /= 1024;
                tmp = "MB";
            }
            if (bytes >= 1024)
            {
                bytes /= 1024;
                tmp = "GB";
            }
            return $"{bytes} {tmp}";
        }

        public void PrintStats()
        {
            long total = 0;
            long failed = 0;
            long sent = 0;
            long received = 0;
            long ping = 0;
            foreach (var cmd in (Command[])Enum.GetValues(typeof(Command)))
            {
                total += Stats[cmd][0];
                failed += Stats[cmd][1];
                sent += Stats[cmd][2];
                received += Stats[cmd][3];
                ping += Stats[cmd][4];
                if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                {
                    Log.WriteLine($"Sent {Stats[cmd][0]} {commandToStr(cmd).ToUpper()}, failed {Stats[cmd][1]}, sent {ConvertSize(Stats[cmd][2])}, received {ConvertSize(Stats[cmd][3])} with an average response time of {Stats[cmd][4] / (Stats[cmd][0] > 0 ? Stats[cmd][0] : 1)} ms");
                }
            }
            Log.WriteLine($"Total network operations {total}, failed {failed}, sent {ConvertSize(sent)}, received {ConvertSize(received)} with an average response time of {ping / (total > 0 ? total : 1)} ms");
        }

        private ServerManager()
        {

        }

        public static ServerManager Instance
        {
            get
            {
                return instance;
            }
        }

        DateTime m_NextAliveCheck = DateTime.MinValue;
        bool m_VersionRejected;

        // Asks the server if it's up: at startup, then once a minute while it isn't. A server or connection that was
        // down when Aether started used to leave party sync off until a restart, and turning sync on needed one too.
        public void CheckAlive()
        {
            m_NextAliveCheck = DateTime.Now.AddSeconds(60);
            Log.WriteLine("Checking the sync server...");
            RequestCommadWithHandler(Command.ALIVE, null, null, false, 0, null, (result, ping) =>
            {
                if (result != null && result["status"]?.ToString() == "ok")
                {
                    Log.WriteLine($"Sync server is up ({ping} ms)");
                    IsServerOline = 1;
                }
                else
                {
                    string reason = result?["result"]?.ToString();
                    if (reason == "v")
                    {
                        Log.WriteLine("The sync server no longer accepts this version. Party sync is off until Aether updates.");
                    }
                    else if (reason == "dev")
                    {
                        Log.WriteLine("The sync server is down for maintenance. Party sync is off for now.");
                    }
                    else
                    {
                        Problems.Report("sync", "Couldn't reach the party sync server, so party sync is off for now. Aether tries again every minute; everything else works without it.");
                    }
                    IsServerOline = -1;
                }
                ResetStats();
            }, (error) =>
            {
                Problems.Report("sync", "Couldn't reach the party sync server, so party sync is off for now. Aether tries again every minute; everything else works without it.");
                IsServerOline = -1;
                ResetStats();
            });
        }

        public void RetryIfDown()
        {
            if (IsServerOline != 1 && !m_VersionRejected && DateTime.Now >= m_NextAliveCheck)
            {
                CheckAlive();
            }
        }

        public async void RequestCommadWithHandler(Command cmd, string key, string player, bool isHost, int damage, string data, Action<JObject, long> callback = null, Action<Exception> onError = null)
        {
            try
            {
                var client = s_Client;
                Dictionary<string, string> parameters = new Dictionary<string, string>();
                string command = commandToStr(cmd);
                parameters.Add("command", command);
                parameters.Add("key", key);
                parameters.Add("player", player);
                parameters.Add("host", isHost ? "true" : "false");
                parameters.Add("damage", damage.ToString());
                parameters.Add("data", data);
                parameters.Add("version", Version);

                var stringContent = new StringContent(JsonConvert.SerializeObject(parameters), Encoding.UTF8, "application/json");

                Stats[cmd][0]++;
                Stats[cmd][2] += (long)stringContent.Headers.ContentLength;


                // Pull, push and damage go out about once a second: logging each one pushed everything else out of the log.
                // The per-lobby summary counts them, and failures are still logged.
                bool routine = cmd == Command.PULL || cmd == Command.PUSH || cmd == Command.DAMAGE;
                if (ConfigHelper.Main.Values.Debug.ShowServerLogs && !routine)
                {
                    switch (cmd)
                    {
                        case Command.PUSH:
                            Log.WriteLine($"Sending {command.ToUpper()} with data of size {stringContent.Headers.ContentLength} byte");
                            break;

                        case Command.ALIVE:
                            Log.WriteLine($"Sending {command.ToUpper()} to {ConfigHelper.Main.Values.ServerUrl} with data of size {stringContent.Headers.ContentLength} byte");
                            break;

                        default:
                            Log.WriteLine($"Sending {command.ToUpper()} with parameters {await stringContent.ReadAsStringAsync()}");
                            break;
                    }
                }

                Stopwatch stpw = new Stopwatch();
                stpw.Start();
                HttpResponseMessage response = await client.PostAsync(ConfigHelper.Main.Values.ServerUrl, stringContent);

                stpw.Stop();
                Stats[cmd][4] += stpw.ElapsedMilliseconds;

                if (response.IsSuccessStatusCode)
                {
                    Stats[cmd][3] += (long)response.Content.Headers.ContentLength;
                    string r = await response.Content.ReadAsStringAsync();
                    if (ConfigHelper.Main.Values.Debug.ShowServerLogs && (!routine || r.Contains("\"error\"")))
                    {
                        Log.WriteLine(cmd == Command.PULL
                            ? $"Received {command.ToUpper()} with response of size {response.Content.Headers.ContentLength} byte"
                            : $"Received {command.ToUpper()} with response {r}");
                    }
                    var json = JObject.Parse(r);
                    string status = json["status"]?.ToString(), result = json["result"]?.ToString();
                    if (status == "error" && result == "v")
                    {
                        m_VersionRejected = true;
                        Problems.Report("sync", "The party sync server no longer accepts this version of Aether. Party sync is off until Aether updates.");
                    }
                    else if (status == "error" && result == "dev")
                    {
                        Problems.Report("sync", "The party sync server is down for maintenance. Party sync is off for now; everything else works.");
                    }
                    else
                    {
                        Problems.Clear("sync");
                    }
                    if (callback != null)
                    {
                        callback(json, stpw.ElapsedMilliseconds);
                    }
                }
                else
                {
                    Stats[cmd][1]++;
                    Problems.Report("sync", $"The party sync server isn't answering properly (error {(int)response.StatusCode}), so parts and teammates' damage may not update. Aether keeps trying.");
                    if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                    {
                        Log.WriteLine($"Received {command.ToUpper()} with error code {response.StatusCode}");
                    }
                    if (callback != null)
                    {
                        callback(null, 0);
                    }
                }
            }
            catch (Exception e)
            {
                Stats[cmd][1]++;
                if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                {
                    Log.WriteLine($"Sync server request failed: {e.Message}");
                }
                Problems.Report("sync", "Can't reach the party sync server, so parts and teammates' damage may not update. Aether keeps trying.");
                onError?.Invoke(e); // some calls pass no handler, and throwing here (async void) would take the app down
            }
        }
    }
}

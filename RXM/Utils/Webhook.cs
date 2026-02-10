using System.Text;
using System.Text.Json;
using System;
using System.Threading.Tasks;
using System.Net.Http;

namespace RXM.Utils
{
    // unused. not sure what to use it for
    public static class Webhook
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        public static async Task SendBtn(string source, string button)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(button))
                return;

            var WH = "https://discord.com/api/webhooks/1462338562207907860/1Gjb_Ij22rxho85JyXxVTcL1YbAPczD8ZDwlEJLH7utRhukPqu-44-gCEU6mSZ1lMsPp";
            var Req = new
            {
                username = "RNET",
                content = $"Source is {source}\nButton: {button}"
            };

            try
            {
                var json = JsonSerializer.Serialize(Req);
                using var RequestContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(WH, RequestContent);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Couldn't send to webhook: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Couldn't send to webhook: {ex.Message}");
            }
        }
    }
}

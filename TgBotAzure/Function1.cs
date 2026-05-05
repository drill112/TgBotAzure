using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class TelegramWebhook
{
    private readonly HttpClient _httpClient = new HttpClient();

    private const string TELEGRAM_TOKEN = "PASTE_YOUR_TELEGRAM_TOKEN";
    private const string GEMINI_API_KEY = "PASTE_YOUR_GEMINI_KEY";
    private const string storage = "PASTE_YOUR_AZURE_CONNECTION";

    private const string CONTAINER_NAME = "reviews";

    [Function("TelegramWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var update = JsonSerializer.Deserialize<TelegramUpdate>(body);

        if (update?.message?.text == null)
            return req.CreateResponse(System.Net.HttpStatusCode.OK);

        var chatId = update.message.chat.id;
        var userText = update.message.text;

        string prompt = "Ты Senior C# разработчик. Проверь код, оцени от 1 до 10 и дай советы:\n" + userText;

        var aiResponse = await CallGemini(prompt);

        var fileName = $"review_{DateTime.UtcNow:yyyyMMddHHmmss}.txt";

        var blobClient = new BlobContainerClient(STORAGE_CONNECTION, CONTAINER_NAME);
        await blobClient.CreateIfNotExistsAsync();

        var blob = blobClient.GetBlobClient(fileName);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(aiResponse));
        await blob.UploadAsync(stream, overwrite: true);

        var fileUrl = blob.Uri.ToString();

        await SendMessage(chatId, $"Готово ✅\n{fileUrl}");

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        return response;
    }

    private async Task<string> CallGemini(string prompt)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(
            $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key={GEMINI_API_KEY}",
            content
        );

        var responseString = await response.Content.ReadAsStringAsync();

        try
        {
            var json = JsonDocument.Parse(responseString);

            if (!json.RootElement.TryGetProperty("candidates", out var candidates))
                return "AI error";

            return candidates[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
        }
        catch
        {
            return "Error parsing AI response";
        }
    }

    private async Task SendMessage(long chatId, string text)
    {
        var url = $"https://api.telegram.org/bot{TELEGRAM_TOKEN}/sendMessage";

        var payload = new
        {
            chat_id = chatId,
            text = text
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        await _httpClient.PostAsync(url, content);
    }
}

public class TelegramUpdate
{
    public Message message { get; set; }
}

public class Message
{
    public Chat chat { get; set; }
    public string text { get; set; }
}

public class Chat
{
    public long id { get; set; }
}
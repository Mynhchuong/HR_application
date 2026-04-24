using Newtonsoft.Json;
using System.Text;

namespace HR_web.API;

public class ApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string ClientName = "SamhoAPI";

    public ApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }


    public async Task<HttpResponseMessage?> GetAsync_Raw(string endpoint, string queryString = "")
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        var url = endpoint;
        if (!string.IsNullOrEmpty(queryString))
            url += "?" + queryString;

        try { return await client.GetAsync(url); }
        catch { return null; }
    }

    public async Task<T?> GetAsync<T>(string endpoint, string queryString = "")
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        var url = endpoint;
        if (!string.IsNullOrEmpty(queryString))
            url += "?" + queryString;

        try
        {
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(json) && !json.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                    return JsonConvert.DeserializeObject<T>(json);
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"API Error: {response.StatusCode} - {errorText}");
            }
            return default;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception calling API: {ex.Message}");
            return default;
        }
    }

    public async Task<T?> GetAsync2<T>(string endpoint, string queryString = "")
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        var url = endpoint;
        if (!string.IsNullOrEmpty(queryString))
            url += "?" + queryString;

        try
        {
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (json.StartsWith("null", StringComparison.OrdinalIgnoreCase))
                    json = json[4..].Trim();

                if (!string.IsNullOrWhiteSpace(json) && !json.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                    return JsonConvert.DeserializeObject<T>(json);
            }
            return default;
        }
        catch
        {
            return default;
        }
    }

    public async Task<T?> GetFullUrlAsync<T>(string fullUrl)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        try
        {
            var response = await client.GetAsync(fullUrl);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(json))
                    return JsonConvert.DeserializeObject<T>(json);
            }
            return default;
        }
        catch
        {
            return default;
        }
    }


    public async Task<HttpResponseMessage?> PostFormAsync(string endpoint, Dictionary<string, string> formData)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        try
        {
            var content = new FormUrlEncodedContent(formData);
            return await client.PostAsync(endpoint, content);
        }
        catch
        {
            return null;
        }
    }


    public async Task<HttpResponseMessage?> PostAsync(string endpoint, object data)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await client.PostAsync(endpoint, content);
        }
        catch
        {
            return null;
        }
    }


    public async Task<T?> PutAsync<T>(string endpoint, object data)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PutAsync(endpoint, content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(result);
            }
            return default;
        }
        catch
        {
            return default;
        }
    }


    public async Task<T?> PatchAsync<T>(string endpoint, object data)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(result);
            }
            return default;
        }
        catch
        {
            return default;
        }
    }


    public async Task<bool> DeleteAsync(string endpoint, string queryString = "")
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        var url = endpoint;
        if (!string.IsNullOrEmpty(queryString))
            url += "?" + queryString;
        try
        {
            var response = await client.DeleteAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

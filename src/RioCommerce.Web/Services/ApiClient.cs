using System.Net.Http.Json;
using RioCommerce.Core.DTOs.Common;
namespace RioCommerce.Web.Services;
public class ApiClient
{
    private readonly HttpClient _http;
    public ApiClient(HttpClient http) => _http = http;
    public async Task<T?> GetAsync<T>(string url)
    {
        var response = await _http.GetFromJsonAsync<ApiResponse<T>>(url);
        return response != null && response.Success ? response.Data : default;
    }
}

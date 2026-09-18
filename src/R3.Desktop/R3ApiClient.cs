using System.Net.Http;
using System.Net.Http.Json;
using R3.Contracts;

namespace R3.Desktop;

public sealed class R3ApiClient(HttpClient httpClient)
{
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try { using var response = await httpClient.GetAsync("health", ct); return response.IsSuccessStatusCode; } catch (HttpRequestException) { return false; } catch (TaskCanceledException) { return false; }
    }
    public async Task<PagedResult<CompanyListItemDto>?> GetCompaniesAsync(PagedRequest request, CancellationToken ct = default) => await httpClient.GetFromJsonAsync<PagedResult<CompanyListItemDto>>($"api/v1/companies?page={request.SafePage}&pageSize={request.SafePageSize}&search={Uri.EscapeDataString(request.Search ?? "")}", ct);
}

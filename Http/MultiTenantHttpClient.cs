using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IQeSign.VeriFactu.Http;
using IQeSign.VeriFactu.Models.Responses;
using IQeSign.VeriFactu.MultiTenant.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IQeSign.VeriFactu.MultiTenant.Http;

/// <summary>
/// Cliente HTTP interno multi-tenant para la API IQ eSign.
/// Mantiene una caché de tokens JWT independiente por cada <c>credentialGuid</c>,
/// con refresco automático y control de concurrencia por tenant.
/// </summary>
internal sealed class MultiTenantHttpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IQeSignMultiTenantOptions _options;
    private readonly ILogger<MultiTenantHttpClient> _logger;

    // Caché de tokens por tenant: clave = credentialGuid
    private readonly ConcurrentDictionary<string, TokenEntry> _tokenCache = new();

    public MultiTenantHttpClient(
        IHttpClientFactory httpClientFactory,
        IOptions<IQeSignMultiTenantOptions> options,
        ILogger<MultiTenantHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Métodos públicos de petición HTTP (incluyen credentialGuid por tenant)
    // -------------------------------------------------------------------------

    /// <summary>Realiza una petición GET autenticada para el tenant indicado.</summary>
    public Task<TResponse> GetAsync<TResponse>(
        string credentialGuid, string relativeUrl, CancellationToken ct = default)
        where TResponse : ApiResponse
        => SendAsync<TResponse>(credentialGuid, HttpMethod.Get, relativeUrl, body: null, ct);

    /// <summary>Realiza una petición GET autenticada con query string para el tenant indicado.</summary>
    public Task<TResponse> GetAsync<TResponse>(
        string credentialGuid, string relativeUrl,
        IDictionary<string, string?> queryParams, CancellationToken ct = default)
        where TResponse : ApiResponse
    {
        var url = BuildUrl(relativeUrl, queryParams);
        return SendAsync<TResponse>(credentialGuid, HttpMethod.Get, url, body: null, ct);
    }

    /// <summary>Realiza una petición POST autenticada para el tenant indicado.</summary>
    public Task<TResponse> PostAsync<TResponse>(
        string credentialGuid, string relativeUrl, object body, CancellationToken ct = default)
        where TResponse : ApiResponse
        => SendAsync<TResponse>(credentialGuid, HttpMethod.Post, relativeUrl, body, ct);

    /// <summary>Realiza una petición PUT autenticada para el tenant indicado.</summary>
    public Task<TResponse> PutAsync<TResponse>(
        string credentialGuid, string relativeUrl, object body, CancellationToken ct = default)
        where TResponse : ApiResponse
        => SendAsync<TResponse>(credentialGuid, HttpMethod.Put, relativeUrl, body, ct);

    /// <summary>Realiza una petición DELETE autenticada para el tenant indicado.</summary>
    public Task<TResponse> DeleteAsync<TResponse>(
        string credentialGuid, string relativeUrl, CancellationToken ct = default)
        where TResponse : ApiResponse
        => SendAsync<TResponse>(credentialGuid, HttpMethod.Delete, relativeUrl, body: null, ct);

    // -------------------------------------------------------------------------
    // Implementación interna
    // -------------------------------------------------------------------------

    private async Task<TResponse> SendAsync<TResponse>(
        string credentialGuid,
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken ct)
        where TResponse : ApiResponse
    {
        var token = await GetOrRefreshTokenAsync(credentialGuid, ct).ConfigureAwait(false);
        var client = CreateHttpClient();

        using var request = BuildRequest(method, relativeUrl, body, token);

        _logger.LogDebug("IQeSign MultiTenant API [{Tenant}] → {Method} {Url}",
            MaskCredential(credentialGuid), method, relativeUrl);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error de comunicación IQeSign MultiTenant [{Tenant}] ({Method} {Url})",
                MaskCredential(credentialGuid), method, relativeUrl);
            throw;
        }

        return await DeserializeResponseAsync<TResponse>(response, method, relativeUrl)
            .ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Gestión de tokens por tenant
    // -------------------------------------------------------------------------

    private async Task<string> GetOrRefreshTokenAsync(string credentialGuid, CancellationToken ct)
    {
        var entry = _tokenCache.GetOrAdd(credentialGuid, _ => new TokenEntry());

        // Fast path: token vigente para este tenant
        if (entry.Token is not null && DateTime.UtcNow < entry.ExpiresAt)
            return entry.Token;

        // Semáforo por tenant para evitar refreshes concurrentes del mismo credencial
        await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check tras adquirir el semáforo
            if (entry.Token is not null && DateTime.UtcNow < entry.ExpiresAt)
                return entry.Token;

            entry.Token = await FetchTokenAsync(credentialGuid, ct).ConfigureAwait(false);
            entry.ExpiresAt = DateTime.UtcNow.AddHours(23); // Margen de 1h antes de los 24h reales
            return entry.Token;
        }
        finally
        {
            entry.Semaphore.Release();
        }
    }

    private async Task<string> FetchTokenAsync(string credentialGuid, CancellationToken ct)
    {
        _logger.LogDebug("Solicitando token JWT para tenant [{Tenant}]...",
            MaskCredential(credentialGuid));

        var client = CreateHttpClient();
        var loginBody = new { CredentialGuid = credentialGuid };

        using var loginRequest = BuildRequest(HttpMethod.Post, "/api/v2/login", loginBody, bearerToken: null);
        var loginResponse = await client.SendAsync(loginRequest, ct).ConfigureAwait(false);

        var loginResult = await DeserializeResponseAsync<LoginResponse>(
            loginResponse, HttpMethod.Post, "/api/v2/login").ConfigureAwait(false);

        if (!loginResult.IsSuccess || loginResult.Result?.Token is null)
            throw new IQeSignAuthException(
                $"Error al autenticar tenant [{MaskCredential(credentialGuid)}] en IQ eSign API. " +
                $"Código: {loginResult.ErrorCode}. Mensaje: {loginResult.ErrorMessage}");

        _logger.LogDebug("Token JWT obtenido correctamente para tenant [{Tenant}].",
            MaskCredential(credentialGuid));

        return loginResult.Result.Token;
    }

    /// <summary>
    /// Invalida el token cacheado de un tenant, forzando un nuevo login en la siguiente petición.
    /// Útil cuando la API devuelve 401 o el token es rechazado por causas externas.
    /// </summary>
    internal void InvalidateToken(string credentialGuid)
    {
        if (_tokenCache.TryGetValue(credentialGuid, out var entry))
            entry.ExpiresAt = DateTime.MinValue;
    }

    /// <summary>
    /// Elimina de la caché todos los tokens expirados.
    /// Se puede llamar periódicamente para liberar memoria en escenarios con muchos tenants.
    /// </summary>
    internal void PurgeExpiredTokens()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _tokenCache.Keys)
        {
            if (_tokenCache.TryGetValue(key, out var entry) && entry.ExpiresAt < now)
                _tokenCache.TryRemove(key, out _);
        }
    }

    /// <summary>Devuelve el número de tenants con token activo en caché.</summary>
    internal int ActiveTenantCount => _tokenCache.Count(kv => DateTime.UtcNow < kv.Value.ExpiresAt);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HttpRequestMessage BuildRequest(
        HttpMethod method, string relativeUrl, object? body, string? bearerToken)
    {
        var request = new HttpRequestMessage(method, relativeUrl);

        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task<TResponse> DeserializeResponseAsync<TResponse>(
        HttpResponseMessage response, HttpMethod method, string url)
        where TResponse : ApiResponse
    {
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new IQeSignApiException(
                $"La API IQ eSign devolvió HTTP {(int)response.StatusCode} en {method} {url}. " +
                $"Respuesta: {content}",
                (int)response.StatusCode);

        var result = JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
        return result ?? throw new IQeSignApiException(
            $"La respuesta de la API IQ eSign no pudo deserializarse ({method} {url}).",
            (int)response.StatusCode);
    }

    private HttpClient CreateHttpClient()
        => _httpClientFactory.CreateClient(_options.GetHttpClientName());

    private static string BuildUrl(string baseUrl, IDictionary<string, string?> queryParams)
    {
        var nonNull = queryParams.Where(kv => kv.Value is not null).ToList();
        if (nonNull.Count == 0) return baseUrl;

        var query = string.Join("&", nonNull.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        return $"{baseUrl}?{query}";
    }

    /// <summary>Enmascara el credencial en los logs: muestra solo los primeros 8 caracteres.</summary>
    private static string MaskCredential(string credentialGuid)
        => credentialGuid.Length > 8
            ? $"{credentialGuid[..8]}..."
            : credentialGuid;

    // -------------------------------------------------------------------------
    // Token entry (por tenant)
    // -------------------------------------------------------------------------

    private sealed class TokenEntry
    {
        public string? Token { get; set; }
        public DateTime ExpiresAt { get; set; } = DateTime.MinValue;

        /// <summary>Semáforo individual por tenant para evitar refreshes concurrentes.</summary>
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
    }
}

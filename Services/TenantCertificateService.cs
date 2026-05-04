using IQeSign.VeriFactu.Models.Requests;
using IQeSign.VeriFactu.Models.Responses;
using IQeSign.VeriFactu.MultiTenant.Http;
using IQeSign.VeriFactu.Services;

namespace IQeSign.VeriFactu.MultiTenant.Services;

/// <summary>
/// Implementación de <see cref="ICertificateService"/> vinculada a un tenant concreto.
/// Todas las peticiones incluyen automáticamente el <c>credentialGuid</c> del tenant.
/// </summary>
internal sealed class TenantCertificateService : ICertificateService
{
    private readonly MultiTenantHttpClient _client;
    private readonly string _credentialGuid;

    internal TenantCertificateService(MultiTenantHttpClient client, string credentialGuid)
    {
        _client = client;
        _credentialGuid = credentialGuid;
    }

    /// <inheritdoc/>
    public Task<AddCertificateResponse> AddAsync(AddCertificateRequest request, CancellationToken ct = default)
        => _client.PostAsync<AddCertificateResponse>(_credentialGuid, "/api/v2/Certificate", request, ct);

    /// <inheritdoc/>
    public Task<DeleteCertificateResponse> DeleteAsync(string id, CancellationToken ct = default)
        => _client.DeleteAsync<DeleteCertificateResponse>(_credentialGuid, $"/api/v2/Certificate/{Uri.EscapeDataString(id)}", ct);

    /// <inheritdoc/>
    public Task<GetCertificateResponse> GetByIdAsync(string id, CancellationToken ct = default)
        => _client.GetAsync<GetCertificateResponse>(_credentialGuid, $"/api/v2/Certificate/{Uri.EscapeDataString(id)}", ct);

    /// <inheritdoc/>
    public Task<DownloadCertificateResponse> DownloadAsync(string id, CancellationToken ct = default)
        => _client.GetAsync<DownloadCertificateResponse>(_credentialGuid, $"/api/v2/Certificate/{Uri.EscapeDataString(id)}/Download", ct);

    /// <inheritdoc/>
    public Task<ListCertificatesResponse> ListAsync(CancellationToken ct = default)
        => _client.GetAsync<ListCertificatesResponse>(_credentialGuid, "/api/v2/Certificate/List", ct);
}

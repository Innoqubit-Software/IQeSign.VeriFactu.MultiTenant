using IQeSign.VeriFactu.Models.Requests;
using IQeSign.VeriFactu.Models.Responses;
using IQeSign.VeriFactu.MultiTenant.Http;
using IQeSign.VeriFactu.Services;

namespace IQeSign.VeriFactu.MultiTenant.Services;

/// <summary>
/// Implementación de <see cref="IVeriFactuService"/> vinculada a un tenant concreto.
/// Todas las peticiones incluyen automáticamente el <c>credentialGuid</c> del tenant.
/// </summary>
internal sealed class TenantVeriFactuService : IVeriFactuService
{
    private readonly MultiTenantHttpClient _client;
    private readonly string _credentialGuid;

    internal TenantVeriFactuService(MultiTenantHttpClient client, string credentialGuid)
    {
        _client = client;
        _credentialGuid = credentialGuid;
    }

    /// <inheritdoc/>
    public Task<GetCodesResponse> GetCodesAsync(CancellationToken ct = default)
        => _client.GetAsync<GetCodesResponse>(_credentialGuid, "/api/v2/VeriFactu/Codes", ct);

    /// <inheritdoc/>
    public Task<GetUsageResponse> GetUsageAsync(CancellationToken ct = default)
        => _client.GetAsync<GetUsageResponse>(_credentialGuid, "/api/v2/VeriFactu/Usage", ct);

    /// <inheritdoc/>
    public Task<AddDocumentResponse> AddDocumentAsync(AddDocumentRequest request, CancellationToken ct = default)
        => _client.PostAsync<AddDocumentResponse>(_credentialGuid, "/api/v2/VeriFactu/Document", request, ct);

    /// <inheritdoc/>
    public Task<GetDocumentResponse> GetDocumentByIdAsync(string id, CancellationToken ct = default)
        => _client.GetAsync<GetDocumentResponse>(_credentialGuid, $"/api/v2/VeriFactu/Document/{Uri.EscapeDataString(id)}", ct);

    /// <inheritdoc/>
    public Task<UpdateDocumentResponse> UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default)
        => _client.PutAsync<UpdateDocumentResponse>(_credentialGuid, $"/api/v2/VeriFactu/Document/{Uri.EscapeDataString(id)}", request, ct);

    /// <inheritdoc/>
    public Task<CancelDocumentResponse> CancelDocumentAsync(string id, CancelDocumentRequest request, CancellationToken ct = default)
        => _client.PutAsync<CancelDocumentResponse>(_credentialGuid, $"/api/v2/VeriFactu/Document/{Uri.EscapeDataString(id)}/Cancel", request, ct);

    /// <inheritdoc/>
    public Task<ListDocumentsResponse> ListDocumentsAsync(GetDocumentListRequest? request = null, CancellationToken ct = default)
    {
        var queryParams = new Dictionary<string, string?>
        {
            ["initDate"] = request?.InitDate,
            ["finishDate"] = request?.FinishDate
        };

        return _client.GetAsync<ListDocumentsResponse>(_credentialGuid, "/api/v2/VeriFactu/Document/List", queryParams, ct);
    }

    /// <inheritdoc/>
    public Task<CheckDocumentResponse> CheckDocumentsAsync(CancellationToken ct = default)
        => _client.GetAsync<CheckDocumentResponse>(_credentialGuid, "/api/v2/VeriFactu/Document/Check", ct);
}

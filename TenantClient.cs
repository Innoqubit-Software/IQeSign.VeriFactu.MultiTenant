using IQeSign.VeriFactu.MultiTenant.Http;
using IQeSign.VeriFactu.MultiTenant.Services;
using IQeSign.VeriFactu.Services;

namespace IQeSign.VeriFactu.MultiTenant;

/// <summary>
/// Representa el contexto de un tenant concreto dentro de la plataforma IQ eSign.
/// Expone los servicios <see cref="Certificate"/> y <see cref="VeriFactu"/> ya vinculados
/// al <c>credentialGuid</c> del tenant, de forma que las llamadas no requieren volver
/// a indicar las credenciales.
/// </summary>
/// <example>
/// <code>
/// var tenant = multiTenantClient.ForTenant("credentialGuid-del-cliente");
/// var response = await tenant.VeriFactu.AddDocumentAsync(request, ct);
/// var certs = await tenant.Certificate.ListAsync(ct);
/// </code>
/// </example>
public sealed class TenantClient
{
    /// <summary>
    /// Servicio de gestión de certificados digitales (.pfx) para este tenant.
    /// </summary>
    public ICertificateService Certificate { get; }

    /// <summary>
    /// Servicio de gestión de documentos VeriFactu para este tenant.
    /// </summary>
    public IVeriFactuService VeriFactu { get; }

    internal TenantClient(MultiTenantHttpClient httpClient, string credentialGuid)
    {
        Certificate = new TenantCertificateService(httpClient, credentialGuid);
        VeriFactu = new TenantVeriFactuService(httpClient, credentialGuid);
    }
}

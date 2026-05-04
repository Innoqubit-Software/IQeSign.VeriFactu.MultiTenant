using IQeSign.VeriFactu.MultiTenant.Http;

namespace IQeSign.VeriFactu.MultiTenant;

/// <summary>
/// Cliente principal multi-tenant para la API IQ eSign VeriFactu.
/// Registrado como <b>singleton</b> en el contenedor DI, mantiene una caché de tokens JWT
/// independiente por cada tenant (<c>credentialGuid</c>) con refresco automático.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="ForTenant"/> para obtener un <see cref="TenantClient"/> pre-vinculado
/// a las credenciales del tenant. Cada <see cref="TenantClient"/> expone los servicios
/// <c>Certificate</c> y <c>VeriFactu</c> listos para usar sin indicar el credencial de nuevo.
/// </para>
/// <para>
/// Los tokens JWT se obtienen automáticamente en la primera petición de cada tenant
/// y se renuevan en segundo plano cuando caducan (margen de seguridad de 1 h sobre los 24 h reales).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Registro en Program.cs:
/// builder.Services.AddIQeSignVeriFactuMultiTenant(options =>
/// {
///     options.Environment = IQeSignEnvironment.Production;
///     options.TimeoutSeconds = 30;
/// });
///
/// // Uso en un servicio:
/// public class FacturacionMultiTenant(IQeSignMultiTenantClient multiTenant)
/// {
///     public async Task EnviarAsync(string credentialGuid, AddDocumentRequest request, CancellationToken ct)
///     {
///         var tenant = multiTenant.ForTenant(credentialGuid);
///         var response = await tenant.VeriFactu.AddDocumentAsync(request, ct);
///
///         if (!response.IsSuccess)
///             throw new Exception($"[{response.ErrorCode}] {response.ErrorMessage}");
///     }
/// }
/// </code>
/// </example>
public sealed class IQeSignMultiTenantClient
{
    private readonly MultiTenantHttpClient _httpClient;

    /// <summary>
    /// Inicializa el cliente multi-tenant.
    /// Normalmente instanciado por el contenedor DI mediante <c>AddIQeSignVeriFactuMultiTenant</c>.
    /// </summary>
    /// <param name="httpClient">Cliente HTTP interno con caché de tokens por tenant.</param>
    internal IQeSignMultiTenantClient(MultiTenantHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Devuelve un <see cref="TenantClient"/> vinculado al tenant indicado.
    /// La instancia creada es ligera (no almacena estado) y puede descartarse tras su uso.
    /// El token JWT del tenant se gestiona en la caché compartida del singleton.
    /// </summary>
    /// <param name="credentialGuid">
    /// Identificador de credenciales del tenant en IQ Portal.
    /// Debe ser un GUID válido con acceso activo a la solución IQ eSign VeriFactu.
    /// </param>
    /// <returns>Cliente pre-vinculado al tenant con acceso a <c>Certificate</c> y <c>VeriFactu</c>.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="credentialGuid"/> es <c>null</c> o vacío.</exception>
    public TenantClient ForTenant(string credentialGuid)
    {
        if (string.IsNullOrWhiteSpace(credentialGuid))
            throw new ArgumentNullException(nameof(credentialGuid),
                "El credentialGuid del tenant no puede ser nulo o vacío.");

        return new TenantClient(_httpClient, credentialGuid);
    }

    /// <summary>
    /// Invalida el token cacheado del tenant indicado, forzando un nuevo login en la siguiente petición.
    /// Útil si el token de un tenant concreto es rechazado externamente (p.ej. revocación manual).
    /// </summary>
    /// <param name="credentialGuid">Identificador del tenant cuyo token se quiere invalidar.</param>
    public void InvalidateToken(string credentialGuid)
        => _httpClient.InvalidateToken(credentialGuid);

    /// <summary>
    /// Elimina de la caché los tokens de los tenants que ya han expirado.
    /// Se puede llamar periódicamente para liberar memoria en aplicaciones con muchos tenants.
    /// </summary>
    public void PurgeExpiredTokens()
        => _httpClient.PurgeExpiredTokens();

    /// <summary>
    /// Número de tenants con un token activo (no expirado) en la caché interna.
    /// Útil para monitorización y diagnóstico.
    /// </summary>
    public int ActiveTenantCount => _httpClient.ActiveTenantCount;
}

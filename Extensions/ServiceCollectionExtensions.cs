using IQeSign.VeriFactu.MultiTenant.Configuration;
using IQeSign.VeriFactu.MultiTenant.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IQeSign.VeriFactu.MultiTenant.Extensions;

/// <summary>
/// Extensiones de <see cref="IServiceCollection"/> para registrar el cliente
/// IQ eSign VeriFactu Multi-Tenant en el contenedor de dependencias.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra el cliente multi-tenant de IQ eSign VeriFactu usando una acción de configuración.
    /// </summary>
    /// <param name="services">La colección de servicios de la aplicación.</param>
    /// <param name="configure">Acción para configurar las opciones del cliente multi-tenant.</param>
    /// <returns>La colección de servicios para encadenar llamadas.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddIQeSignVeriFactuMultiTenant(options =>
    /// {
    ///     options.Environment = IQeSignEnvironment.Production;
    ///     options.TimeoutSeconds = 30;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddIQeSignVeriFactuMultiTenant(
        this IServiceCollection services,
        Action<IQeSignMultiTenantOptions> configure)
    {
        services.Configure(configure);
        RegisterServices(services);
        return services;
    }

    /// <summary>
    /// Registra el cliente multi-tenant de IQ eSign VeriFactu usando una sección de configuración.
    /// </summary>
    /// <param name="services">La colección de servicios de la aplicación.</param>
    /// <param name="configuration">
    /// Sección de configuración que contiene las opciones de <see cref="IQeSignMultiTenantOptions"/>
    /// (nombre de sección por defecto: <c>"IQeSignMultiTenant"</c>).
    /// </param>
    /// <returns>La colección de servicios para encadenar llamadas.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddIQeSignVeriFactuMultiTenant(
    ///     builder.Configuration.GetSection(IQeSignMultiTenantOptions.SectionName));
    /// </code>
    /// </example>
    public static IServiceCollection AddIQeSignVeriFactuMultiTenant(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<IQeSignMultiTenantOptions>(configuration);
        RegisterServices(services);
        return services;
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Dos clientes HTTP nombrados, uno por entorno
        services.AddHttpClient(HttpClientNames.Production, (sp, client) =>
        {
            client.BaseAddress = new Uri("https://iqesignapi.azurewebsites.net");
            ConfigureTimeout(client, sp);
        });

        services.AddHttpClient(HttpClientNames.Staging, (sp, client) =>
        {
            client.BaseAddress = new Uri("https://iqesignapistaging.azurewebsites.net");
            ConfigureTimeout(client, sp);
        });

        // MultiTenantHttpClient como singleton (mantiene la caché de tokens entre peticiones)
        services.AddSingleton<MultiTenantHttpClient>();

        // Facade principal expuesto a los consumidores
        // (factory explícita porque el constructor acepta el tipo interno MultiTenantHttpClient)
        services.AddSingleton(sp =>
            new IQeSignMultiTenantClient(sp.GetRequiredService<MultiTenantHttpClient>()));
    }

    private static void ConfigureTimeout(System.Net.Http.HttpClient client, IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<IQeSignMultiTenantOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }
}

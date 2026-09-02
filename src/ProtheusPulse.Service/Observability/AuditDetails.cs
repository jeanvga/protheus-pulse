using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProtheusPulse.Service.Observability;

/// <summary>
/// Serializa o detalhe sanitizado que acompanha cada evento de auditoria.
/// </summary>
/// <remarks>
/// Com as opções padrão do <see cref="JsonSerializer"/> um enum vira número, e quem lê a
/// auditoria via "Type: 2" em vez de "Type: Webhook". O registro só serve se for legível
/// meses depois, sem consultar o código para traduzir o índice.
/// </remarks>
public static class AuditDetails
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(object details) => JsonSerializer.Serialize(details, Options);
}

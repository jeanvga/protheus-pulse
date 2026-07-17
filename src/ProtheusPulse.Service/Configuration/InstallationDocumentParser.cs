using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ProtheusPulse.Service.Configuration;

public static class InstallationDocumentParser
{
    public const int MaximumDocumentCharacters = 512 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static ParseResult Parse(string? format, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ParseResult.Failure("Informe o conteúdo do arquivo de importação.");
        }

        if (content.Length > MaximumDocumentCharacters)
        {
            return ParseResult.Failure($"O arquivo deve possuir no máximo {MaximumDocumentCharacters / 1024} KiB.");
        }

        try
        {
            var document = format?.Trim().ToLowerInvariant() switch
            {
                "json" => JsonSerializer.Deserialize<InstallationImportDocument>(content, JsonOptions),
                "yaml" or "yml" => YamlDeserializer.Deserialize<InstallationImportDocument>(content),
                _ => null
            };

            return document is null
                ? ParseResult.Failure("Formato inválido. Use json, yaml ou yml.")
                : ParseResult.Success(document);
        }
        catch (JsonException)
        {
            return ParseResult.Failure("JSON inválido ou com campos não reconhecidos.");
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return ParseResult.Failure("YAML inválido ou com campos não reconhecidos.");
        }
    }

    public sealed record ParseResult(InstallationImportDocument? Document, IReadOnlyList<string> Errors)
    {
        public bool IsValid => Document is not null && Errors.Count == 0;

        public static ParseResult Success(InstallationImportDocument document) => new(document, []);
        public static ParseResult Failure(string error) => new(null, [error]);
    }
}

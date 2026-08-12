using System.Text;

using NSwag;

using Raptor21.OpenApi.Generics.CodeGen;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

string? input = null;
var settings = new RefitClientGeneratorSettings();
string? output = null;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];

    switch (arg)
    {
        case "--output" or "-o":
            output = Next(args, ref i, arg);
            break;
        case "--namespace" or "-n":
            settings.Namespace = Next(args, ref i, arg);
            break;
        case "--single-interface":
            settings.Grouping = InterfaceGrouping.Single;
            break;
        case "--interface-name":
            settings.SingleInterfaceName = Next(args, ref i, arg);
            settings.Grouping = InterfaceGrouping.Single;
            break;
        case "--no-headers":
            settings.ExcludeAllHeaderParameters = true;
            break;
        case "--skip-header":
            settings.ExcludedHeaderParameters.Add(Next(args, ref i, arg));
            break;
        case "--exclude-tag":
            settings.ExcludedTags.Add(Next(args, ref i, arg));
            break;
        case "--payload-property":
            settings.PayloadPropertyName = Next(args, ref i, arg);
            break;
        case "--no-cancellation-tokens":
            settings.GenerateCancellationTokens = false;
            break;
        case "--path-prefix":
            settings.PathPrefix = Next(args, ref i, arg);
            break;
        case "--di":
            settings.GenerateDependencyInjection = true;
            break;
        case "--registration-method":
            settings.RegistrationMethodName = Next(args, ref i, arg);
            settings.GenerateDependencyInjection = true;
            break;
        case "--using":
            settings.AdditionalNamespaces.Add(Next(args, ref i, arg));
            break;
        case "--map":
            {
                var mapping = Next(args, ref i, arg);
                var separator = mapping.IndexOf('=');
                if (separator <= 0)
                {
                    Console.Error.WriteLine($"--map expects 'ProjectedTypeName=LocalTypeName', got '{mapping}'.");
                    return 1;
                }

                settings.TypeMappings[mapping[..separator]] = mapping[(separator + 1)..];
                break;
            }
        default:
            if (arg.StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option '{arg}'. Run with --help to see what is available.");
                return 1;
            }

            input = arg;
            break;
    }
}

if (input is null)
{
    Console.Error.WriteLine("No OpenAPI document given. Pass a file path or a URL.");
    return 1;
}

try
{
    var document = input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? await OpenApiDocument.FromUrlAsync(input)
        : await OpenApiDocument.FromFileAsync(input);

    var code = new RefitClientGenerator(settings).Generate(document);

    if (output is null)
    {
        Console.Out.Write(code);
    }
    else
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(output, code, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {output}");
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Generation failed: {ex.Message}");
    return 1;
}

static string Next(string[] args, ref int index, string option)
{
    if (index + 1 >= args.Length)
        throw new ArgumentException($"Option '{option}' expects a value.");

    return args[++index];
}

static void PrintUsage()
{
    Console.WriteLine("""
        raptor21-openapi <document> [options]

          Generates a Refit client from an OpenAPI document, restoring the generic response
          contracts the document carries as metadata instead of redefining them.

          <document>                  Path or URL of the OpenAPI document.

        Options
          -o, --output <file>         Write to a file instead of standard output.
          -n, --namespace <ns>        Namespace for generated code. Default: GeneratedClient.
              --single-interface      Emit one interface for the whole document instead of one per tag.
              --interface-name <name> Name for that single interface. Implies --single-interface.
              --no-headers            Leave every header parameter out of generated signatures.
              --skip-header <name>    Leave one header parameter out. Repeatable.
              --exclude-tag <tag>     Skip every operation carrying this tag. Repeatable.
              --payload-property <p>  Envelope property holding the payload. Default: data.
              --path-prefix <path>    Prepend this to every route, for a service reached through a
                                      gateway that mounts it under a prefix.
              --map <from>=<to>       Map a projected contract type onto a local one. Repeatable.
              --using <namespace>     Extra using directive in the generated file. Repeatable.
              --di                    Also emit an extension method registering every interface as a
                                      Refit client. Needs Refit.HttpClientFactory in the target project.
              --registration-method <name>
                                      Name for that method. Default: AddGeneratedApis. Implies --di.
              --no-cancellation-tokens
                                      Omit the trailing CancellationToken parameter.
        """);
}

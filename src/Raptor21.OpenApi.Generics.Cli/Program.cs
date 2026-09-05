using System.Text;

using NSwag;

using Raptor21.OpenApi.Generics.CodeGen;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

string? input = null;
string? output = null;
var language = "csharp";

// Shared/raw options, applied to the language-specific settings after parsing so both paths see identical
// flag semantics.
var ns = "GeneratedClient";
var single = false;
string? interfaceName = null;
var noHeaders = false;
var skipHeaders = new List<string>();
var excludeTags = new List<string>();
var payloadProperty = "data";
string? pathPrefix = null;
var maps = new List<(string From, string To)>();
var usings = new List<string>();
var noCancellationTokens = false;
var di = false;
string? registrationMethod = null;
var queries = false;
string? bodyName = null;
var optionalBody = false;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];

    switch (arg)
    {
        case "--output" or "-o":
            output = Next(args, ref i, arg);
            break;
        case "--language" or "-l":
            language = Next(args, ref i, arg).ToLowerInvariant();
            break;
        case "--queries":
            queries = true;
            break;
        case "--body-name":
            bodyName = Next(args, ref i, arg);
            break;
        case "--optional-body":
            optionalBody = true;
            break;
        case "--namespace" or "-n":
            ns = Next(args, ref i, arg);
            break;
        case "--single-interface":
            single = true;
            break;
        case "--interface-name":
            interfaceName = Next(args, ref i, arg);
            single = true;
            break;
        case "--no-headers":
            noHeaders = true;
            break;
        case "--skip-header":
            skipHeaders.Add(Next(args, ref i, arg));
            break;
        case "--exclude-tag":
            excludeTags.Add(Next(args, ref i, arg));
            break;
        case "--payload-property":
            payloadProperty = Next(args, ref i, arg);
            break;
        case "--no-cancellation-tokens":
            noCancellationTokens = true;
            break;
        case "--path-prefix":
            pathPrefix = Next(args, ref i, arg);
            break;
        case "--di":
            di = true;
            break;
        case "--registration-method":
            registrationMethod = Next(args, ref i, arg);
            di = true;
            break;
        case "--using":
            usings.Add(Next(args, ref i, arg));
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

                maps.Add((mapping[..separator], mapping[(separator + 1)..]));
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

if (language is not ("csharp" or "typescript"))
{
    Console.Error.WriteLine($"Unknown language '{language}'. Expected 'csharp' or 'typescript'.");
    return 1;
}

if (queries && language != "typescript")
{
    Console.Error.WriteLine("--queries applies only to --language typescript.");
    return 1;
}

if ((bodyName is not null || optionalBody) && language != "typescript")
{
    Console.Error.WriteLine("--body-name and --optional-body apply only to --language typescript.");
    return 1;
}

try
{
    var document = input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                   || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? await OpenApiDocument.FromUrlAsync(input)
        : await OpenApiDocument.FromFileAsync(input);

    return language == "typescript"
        ? await GenerateTypeScript(document, output)
        : await GenerateCSharp(document, output);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Generation failed: {ex.Message}");
    return 1;
}

async Task<int> GenerateCSharp(OpenApiDocument document, string? outputPath)
{
    var settings = new RefitClientGeneratorSettings
    {
        Namespace = ns,
        SingleInterfaceName = interfaceName ?? "IApiClient",
        Grouping = single ? InterfaceGrouping.Single : InterfaceGrouping.ByTag,
        GenerateCancellationTokens = !noCancellationTokens,
        ExcludeAllHeaderParameters = noHeaders,
        PayloadPropertyName = payloadProperty,
        PathPrefix = pathPrefix,
        GenerateDependencyInjection = di,
        RegistrationMethodName = registrationMethod ?? "AddGeneratedApis",
    };

    foreach (var header in skipHeaders) settings.ExcludedHeaderParameters.Add(header);
    foreach (var tag in excludeTags) settings.ExcludedTags.Add(tag);
    foreach (var (from, to) in maps) settings.TypeMappings[from] = to;
    foreach (var u in usings) settings.AdditionalNamespaces.Add(u);

    var code = new RefitClientGenerator(settings).Generate(document);

    if (outputPath is null)
    {
        Console.Out.Write(code);
    }
    else
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(outputPath, code, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {outputPath}");
    }

    return 0;
}

async Task<int> GenerateTypeScript(OpenApiDocument document, string? outputPath)
{
    var settings = new TypeScriptClientGeneratorSettings
    {
        SingleClientName = interfaceName ?? "ApiClient",
        Grouping = single ? InterfaceGrouping.Single : InterfaceGrouping.ByTag,
        ExcludeAllHeaderParameters = noHeaders,
        PayloadPropertyName = payloadProperty,
        PathPrefix = pathPrefix,
        GenerateQueries = queries,
        BodyParameterName = bodyName ?? "request",
        BodyParameterRequired = !optionalBody,
    };

    foreach (var header in skipHeaders) settings.ExcludedHeaderParameters.Add(header);
    foreach (var tag in excludeTags) settings.ExcludedTags.Add(tag);
    foreach (var (from, to) in maps) settings.TypeMappings[from] = to;

    var result = new TypeScriptClientGenerator(settings).Generate(document);

    var files = new List<(string Name, string Content)>
    {
        ("types.ts", result.Types),
        ("client.ts", result.Client),
    };
    if (result.Queries is not null)
        files.Add(("queries.ts", result.Queries));

    if (outputPath is null)
    {
        // No directory to write into: emit every file to standard output with a header separator.
        foreach (var (name, content) in files)
        {
            Console.Out.WriteLine($"// ==== {name} ====");
            Console.Out.Write(content);
            Console.Out.WriteLine();
        }

        return 0;
    }

    Directory.CreateDirectory(outputPath);
    var encoding = new UTF8Encoding(false);

    foreach (var (name, content) in files)
    {
        var path = Path.Combine(outputPath, name);
        await File.WriteAllTextAsync(path, content, encoding);
        Console.WriteLine($"Wrote {path}");
    }

    return 0;
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

          Generates a typed client from an OpenAPI document, restoring the generic response
          contracts the document carries as metadata instead of redefining them.

          <document>                  Path or URL of the OpenAPI document.

        Options
          -o, --output <path>         C#: write to a file instead of standard output.
                                      TypeScript: a directory to write types.ts/client.ts/(queries.ts) into.
          -l, --language <lang>       csharp (default) or typescript.
              --queries               TypeScript only: also emit queries.ts with TanStack Query hooks.
              --body-name <name>      TypeScript only: name of the request-body parameter. Default: request.
              --optional-body         TypeScript only: keep a body optional when the document does not mark
                                      it required (default treats every body as required — see README).
          -n, --namespace <ns>        C# namespace for generated code. Default: GeneratedClient.
              --single-interface      Emit one interface/client for the whole document instead of one per tag.
              --interface-name <name> Name for that single interface/client. Implies --single-interface.
              --no-headers            Leave every header parameter out of generated signatures.
              --skip-header <name>    Leave one header parameter out. Repeatable.
              --exclude-tag <tag>     Skip every operation carrying this tag. Repeatable.
              --payload-property <p>  Envelope property holding the payload. Default: data.
              --path-prefix <path>    Prepend this to every route, for a service reached through a
                                      gateway that mounts it under a prefix.
              --map <from>=<to>       Map a projected contract type onto a local one. Repeatable.
              --using <namespace>     C#: extra using directive in the generated file. Repeatable.
              --di                    C#: also emit an extension method registering every interface as a
                                      Refit client. Needs Refit.HttpClientFactory in the target project.
              --registration-method <name>
                                      C#: name for that method. Default: AddGeneratedApis. Implies --di.
              --no-cancellation-tokens
                                      C#: omit the trailing CancellationToken parameter.
        """);
}

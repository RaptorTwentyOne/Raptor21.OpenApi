using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Raptor21.OpenApi.Generics.CodeGen;

using Xunit;

namespace Raptor21.OpenApi.Generics.Tests;

/// <summary>
/// Guards the two shapes of <c>queries.ts</c> that a consumer only discovers at <c>tsc</c> time, because the
/// emitter itself is happy either way.
/// </summary>
/// <remarks>
/// <para>
/// Both were live regressions. <c>queries.ts</c> flattens every client class's methods into module-level hooks,
/// so two tags whose operations derive the same method name produced the same <c>use…</c> hook twice and the
/// consuming app died on a duplicate declaration before rendering anything. And the per-tag key factory used to
/// declare its parameters required while the hook passed <c>string | undefined</c> into them — a TS2345 at a
/// call the emitter wrote itself.
/// </para>
/// <para>
/// The fixture reproduces the real document's shape rather than a contrived one: no <c>operationId</c> anywhere,
/// so every name comes from the route, and the collision comes from two unrelated routes ending in the same
/// segment — <c>/api/customers/{id}</c> under <c>CustomerDetail</c> and <c>/api/notifications/push/customers</c>
/// under <c>Notifications</c> both derive <c>customers</c>.
/// </para>
/// <para>
/// The two invariants are written as functions over the emitted text (<see cref="DuplicateTopLevelExports"/>,
/// <see cref="AuditKeyFactories"/>) rather than as inline assertions, so they can be pointed at every fixture in
/// the suite and — in the two <c>…detector_fires…</c> tests — at the verbatim pre-fix output, which proves the
/// guard is not passing vacuously.
/// </para>
/// </remarks>
public sealed class QueriesHookNamingTests
{
    private static async Task<string> GenerateQueriesAsync(string fixture)
    {
        var document = await Documents.FromFixtureAsync(fixture);
        var settings = new TypeScriptClientGeneratorSettings { GenerateQueries = true };
        var output = new TypeScriptClientGenerator(settings).Generate(document);

        Assert.NotNull(output.Queries);
        return Normalize(output.Queries!);
    }

    private static Task<string> GenerateHookFixtureAsync() => GenerateQueriesAsync("hooks.openapi.json");

    /// <summary>Every fixture in the suite whose operations produce hooks.</summary>
    public static TheoryData<string> AllFixtures => new() { "hooks.openapi.json", "widgets.openapi.json", "sample.openapi.json" };

    // ---- bug 1: cross-tag hook collision ----

    [Fact]
    public async Task Hook_names_are_unique_across_tags()
    {
        var queries = await GenerateHookFixtureAsync();

        var names = HookNames(queries);

        Assert.NotEmpty(names);
        Assert.Equal(names.Distinct(StringComparer.Ordinal).Count(), names.Count);
    }

    [Fact]
    public async Task Colliding_hook_is_tag_qualified_on_both_sides_not_first_wins()
    {
        var queries = await GenerateHookFixtureAsync();

        // Both operations derive the method name `customers`, so neither may keep the bare hook name: a
        // first-wins scheme would rename only the loser, which leaves the short name meaning whichever tag
        // happened to sort first — and that meaning silently moves when a tag is added.
        Assert.Contains("export function useCustomerDetailCustomers(", queries);
        Assert.Contains("export function useNotificationsCustomers(", queries);
        Assert.DoesNotContain("export function useCustomers(", queries);

        // And the qualification is not a numeric suffix — `useCustomers2` names nothing a reader can place.
        Assert.DoesNotContain("export function useCustomers2(", queries);
    }

    [Fact]
    public async Task Non_colliding_hook_keeps_its_short_name()
    {
        var queries = await GenerateHookFixtureAsync();

        // `/api/search/widgets` is the only operation deriving `widgets`, so it is left alone: the collision
        // repair must not spread tag prefixes over the whole file.
        Assert.Contains("export function useWidgets(", queries);
        Assert.DoesNotContain("useSearchWidgets", queries);

        // Same on the mutation side of the file.
        Assert.Contains("export function usePush(", queries);
    }

    [Fact]
    public async Task Renamed_hook_still_calls_the_client_of_its_own_tag()
    {
        var queries = await GenerateHookFixtureAsync();

        // Renaming must not re-point: each qualified hook keeps calling its own tag's client instance, and its
        // own tag's key factory.
        Assert.Contains("queryKey: customerDetailKeys.customers(id), queryFn: () => customerDetailApi.customers(id)", queries);
        Assert.Contains("queryKey: notificationsKeys.customers(), queryFn: () => notificationsApi.customers()", queries);
    }

    [Fact]
    public void Duplicate_export_detector_fires_on_the_pre_fix_output()
    {
        // Sensitivity check: the same detector the fixtures are held to, pointed at what the emitter produced
        // before the fix. If this stops reporting `useCustomers`, the guard above has gone blind.
        var duplicates = DuplicateTopLevelExports(PreFixQueries);

        Assert.Contains("useCustomers", duplicates);
    }

    // ---- bug 2: optional parameters in the per-tag key factory ----

    [Fact]
    public async Task Key_factory_declares_optional_parameters_optional()
    {
        var queries = await GenerateHookFixtureAsync();

        // `q` is an optional query parameter, `page` a required one. The hook passes both straight into the key
        // factory, so a required `q: string` there is a TS2345 against the hook's own `string | undefined`.
        Assert.Contains("widgets: (page: number, q?: string) =>", queries);
        Assert.Contains("export function useWidgets(page: number, q?: string,", queries);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Key_factory_parameters_mirror_their_hook_exactly(string fixture)
    {
        var queries = await GenerateQueriesAsync(fixture);

        var audit = AuditKeyFactories(queries);

        Assert.True(audit.PairsChecked > 0, $"No query hook/key factory pair was found in the output for '{fixture}'.");
        Assert.True(audit.Mismatches.Count == 0, $"queries.ts for '{fixture}':\n  " + string.Join("\n  ", audit.Mismatches));
    }

    [Fact]
    public void Key_factory_mismatch_detector_fires_on_the_pre_fix_output()
    {
        // Sensitivity check for the mirror rule: before the fix the key factory declared `q: string` while the
        // hook declared `q?: string`, which is exactly the pair TypeScript rejected.
        var audit = AuditKeyFactories(PreFixQueries);

        Assert.True(audit.PairsChecked > 0);
        Assert.Contains(audit.Mismatches, m => m.Contains("q?: string", StringComparison.Ordinal));
    }

    // ---- general shape invariant ----

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task No_top_level_export_is_declared_twice(string fixture)
    {
        var queries = await GenerateQueriesAsync(fixture);

        var duplicates = DuplicateTopLevelExports(queries);

        Assert.True(duplicates.Count == 0, $"queries.ts for '{fixture}' declares these identifiers more than once: {string.Join(", ", duplicates)}.");
    }

    // ---- the invariants, as functions over the emitted text ----

    /// <summary>Names declared more than once at the top level of a generated <c>queries.ts</c>.</summary>
    private static IReadOnlyList<string> DuplicateTopLevelExports(string queries)
        => Regex.Matches(Normalize(queries), @"^export (?:function|const|interface|type|class) (\w+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

    private readonly record struct KeyFactoryAudit(int PairsChecked, IReadOnlyList<string> Mismatches);

    /// <summary>
    /// Checks every query hook against the key factory entry it calls: the parameter lists must be identical,
    /// optional markers included, because the hook passes its own parameters positionally into that call.
    /// </summary>
    private static KeyFactoryAudit AuditKeyFactories(string queries)
    {
        var text = Normalize(queries);
        var factories = KeyFactories(text);
        var mismatches = new List<string>();
        var pairs = 0;

        foreach (var hook in Hooks(text))
        {
            var reference = Regex.Match(hook.Body, @"queryKey: (\w+)\.(\w+)\(");
            if (!reference.Success)
                continue; // a mutation hook has no key factory entry

            var key = reference.Groups[1].Value + "." + reference.Groups[2].Value;
            if (!factories.TryGetValue(key, out var factoryParameters))
            {
                mismatches.Add($"{hook.Name} calls key factory entry '{key}', which is not emitted.");
                continue;
            }

            // The hook's trailing `options?` parameter is not part of the key.
            var hookParameters = SplitTopLevel(hook.Parameters);
            if (hookParameters.Count == 0)
            {
                mismatches.Add($"{hook.Name} declares no parameters at all, not even the options bag.");
                continue;
            }

            hookParameters.RemoveAt(hookParameters.Count - 1);
            var keyParameters = SplitTopLevel(factoryParameters);
            pairs++;

            if (!hookParameters.SequenceEqual(keyParameters, StringComparer.Ordinal))
            {
                mismatches.Add(
                    $"{hook.Name} passes ({string.Join(", ", hookParameters)}) into {key}, " +
                    $"which declares ({string.Join(", ", keyParameters)}).");
            }
        }

        return new KeyFactoryAudit(pairs, mismatches);
    }

    // ---- parsing helpers ----

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static List<string> HookNames(string queries)
        => Regex.Matches(queries, @"^export function (use\w+)\(", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();

    private readonly record struct Hook(string Name, string Parameters, string Body);

    /// <summary>Splits the file into hooks: the declaration's parameter list, plus everything up to the next hook.</summary>
    private static List<Hook> Hooks(string queries)
    {
        var hooks = new List<Hook>();
        var matches = Regex.Matches(queries, @"^export function (use\w+)\((.*)\) \{$", RegexOptions.Multiline).ToList();

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : queries.Length;
            hooks.Add(new Hook(matches[i].Groups[1].Value, matches[i].Groups[2].Value, queries.Substring(start, end - start)));
        }

        return hooks;
    }

    /// <summary>Reads every per-tag key factory, keyed as <c>widgetsKeys.getWidgets</c>, to its parameter list.</summary>
    private static Dictionary<string, string> KeyFactories(string queries)
    {
        var factories = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;

        foreach (var line in queries.Split('\n'))
        {
            var open = Regex.Match(line, @"^export const (\w+) = \{$");
            if (open.Success)
            {
                current = open.Groups[1].Value;
                continue;
            }

            if (line.StartsWith("}", StringComparison.Ordinal))
            {
                current = null;
                continue;
            }

            if (current is null)
                continue;

            var entry = Regex.Match(line, @"^  (\w+): \((.*)\) => \[");
            if (entry.Success)
                factories[current + "." + entry.Groups[1].Value] = entry.Groups[2].Value;
        }

        return factories;
    }

    /// <summary>Splits a TypeScript parameter list on its top-level commas, so generic arguments stay intact.</summary>
    private static List<string> SplitTopLevel(string parameters)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters))
            return parts;

        var depth = 0;
        var start = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            switch (parameters[i])
            {
                case '<' or '(' or '[' or '{':
                    depth++;
                    break;
                case '>' or ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(parameters.Substring(start, i - start).Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(parameters.Substring(start).Trim());
        return parts;
    }

    /// <summary>
    /// What the emitter produced for <c>hooks.openapi.json</c> before either fix: <c>useCustomers</c> declared
    /// twice, and a key factory whose <c>q</c> is required while its hook's is optional. Kept verbatim as the
    /// negative case both detectors are proved against.
    /// </summary>
    private const string PreFixQueries = """
        import * as ReactQuery from '@tanstack/react-query';
        import { CustomerDetailApi, NotificationsApi, SearchApi, type ClientOptions } from './client';
        import type { CustomerDto } from './types';

        export const customerDetailKeys = {
          customers: (id: string) => ['CustomerDetail', 'customers', id] as const,
        } as const;

        export function useCustomers(id: string, options?: Omit<ReactQuery.UseQueryOptions<CustomerDto>, 'queryKey' | 'queryFn'>) {
          return ReactQuery.useQuery({ queryKey: customerDetailKeys.customers(id), queryFn: () => customerDetailApi.customers(id), ...options });
        }

        export const notificationsKeys = {
          customers: () => ['Notifications', 'customers'] as const,
        } as const;

        export function useCustomers(options?: Omit<ReactQuery.UseQueryOptions<CustomerDto[]>, 'queryKey' | 'queryFn'>) {
          return ReactQuery.useQuery({ queryKey: notificationsKeys.customers(), queryFn: () => notificationsApi.customers(), ...options });
        }

        export const searchKeys = {
          widgets: (page: number, q: string) => ['Search', 'widgets', page, q] as const,
        } as const;

        export function useWidgets(page: number, q?: string, options?: Omit<ReactQuery.UseQueryOptions<CustomerDto[]>, 'queryKey' | 'queryFn'>) {
          return ReactQuery.useQuery({ queryKey: searchKeys.widgets(page, q), queryFn: () => searchApi.widgets(page, q), ...options });
        }
        """;
}

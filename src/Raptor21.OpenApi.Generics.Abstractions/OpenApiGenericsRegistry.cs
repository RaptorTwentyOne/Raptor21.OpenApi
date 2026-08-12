using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Raptor21.OpenApi.Generics;

/// <summary>Describes an open generic type that acts as a response envelope.</summary>
public sealed class ApiWrapperDescriptor
{
    /// <summary>Creates a descriptor for an open generic envelope such as <c>typeof(BaseResponse&lt;&gt;)</c>.</summary>
    /// <exception cref="ArgumentException">The type is not an open generic, or the payload index is out of range.</exception>
    public ApiWrapperDescriptor(Type openGenericType, int dataParameterIndex = 0, string dataPropertyName = "Data")
    {
        if (openGenericType is null)
            throw new ArgumentNullException(nameof(openGenericType));
        if (!openGenericType.IsGenericTypeDefinition)
            throw new ArgumentException($"'{openGenericType}' is not an open generic type definition. Pass typeof(Envelope<>), not a closed type.", nameof(openGenericType));

        var arity = openGenericType.GetGenericArguments().Length;
        if (dataParameterIndex < 0 || dataParameterIndex >= arity)
            throw new ArgumentException($"'{openGenericType}' has {arity} type parameter(s); index {dataParameterIndex} is out of range.", nameof(dataParameterIndex));

        OpenGenericType = openGenericType;
        DataParameterIndex = dataParameterIndex;
        DataPropertyName = dataPropertyName ?? "Data";
    }

    /// <summary>The open generic definition, e.g. <c>BaseResponse&lt;&gt;</c>.</summary>
    public Type OpenGenericType { get; }

    /// <summary>Index of the type parameter carrying the payload.</summary>
    public int DataParameterIndex { get; }

    /// <summary>Name of the property carrying the payload.</summary>
    public string DataPropertyName { get; }

    /// <summary>Fully qualified name without CLR arity, as written to <c>x-api-wrapper-type</c>.</summary>
    public string TypeName => TypeNaming.FullNameWithoutArity(OpenGenericType);
}

/// <summary>Describes an open generic type that wraps a payload without being the response envelope.</summary>
public sealed class DataContainerDescriptor
{
    /// <summary>Creates a descriptor for an open generic container such as <c>typeof(Page&lt;&gt;)</c>.</summary>
    /// <exception cref="ArgumentException">The type is not an open generic, or the item index is out of range.</exception>
    public DataContainerDescriptor(Type openGenericType, int itemParameterIndex = 0)
    {
        if (openGenericType is null)
            throw new ArgumentNullException(nameof(openGenericType));
        if (!openGenericType.IsGenericTypeDefinition)
            throw new ArgumentException($"'{openGenericType}' is not an open generic type definition. Pass typeof(Container<>), not a closed type.", nameof(openGenericType));

        var arity = openGenericType.GetGenericArguments().Length;
        if (itemParameterIndex < 0 || itemParameterIndex >= arity)
            throw new ArgumentException($"'{openGenericType}' has {arity} type parameter(s); index {itemParameterIndex} is out of range.", nameof(itemParameterIndex));

        OpenGenericType = openGenericType;
        ItemParameterIndex = itemParameterIndex;
    }

    /// <summary>The open generic definition, e.g. <c>Page&lt;&gt;</c>.</summary>
    public Type OpenGenericType { get; }

    /// <summary>Index of the type parameter carrying the item.</summary>
    public int ItemParameterIndex { get; }

    /// <summary>Simple name without CLR arity, as written to <c>x-data-container</c>.</summary>
    public string Name => TypeNaming.SimpleNameWithoutArity(OpenGenericType);

    /// <summary>Fully qualified name without CLR arity, as written to <c>x-data-container-type</c>.</summary>
    public string TypeName => TypeNaming.FullNameWithoutArity(OpenGenericType);
}

/// <summary>
/// The set of envelope and container contracts a projection should recognise.
/// </summary>
/// <remarks>
/// Envelopes you own can be found by attribute; envelopes from a package you cannot annotate are added
/// explicitly. Both end up here, and projection asks this one object.
/// </remarks>
public sealed class OpenApiGenericsRegistry
{
    private readonly Dictionary<Type, ApiWrapperDescriptor> _wrappers = new();
    private readonly Dictionary<Type, DataContainerDescriptor> _containers = new();

    /// <summary>Registered envelopes.</summary>
    public IReadOnlyCollection<ApiWrapperDescriptor> Wrappers => _wrappers.Values;

    /// <summary>Registered containers.</summary>
    public IReadOnlyCollection<DataContainerDescriptor> Containers => _containers.Values;

    /// <summary>Registers an envelope. Registering the same open generic twice replaces the earlier entry.</summary>
    public OpenApiGenericsRegistry AddWrapper(Type openGenericType, int dataParameterIndex = 0, string dataPropertyName = "Data")
        => AddWrapper(new ApiWrapperDescriptor(openGenericType, dataParameterIndex, dataPropertyName));

    /// <summary>Registers an envelope.</summary>
    public OpenApiGenericsRegistry AddWrapper(ApiWrapperDescriptor descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        _wrappers[descriptor.OpenGenericType] = descriptor;
        return this;
    }

    /// <summary>Registers a container. Registering the same open generic twice replaces the earlier entry.</summary>
    public OpenApiGenericsRegistry AddContainer(Type openGenericType, int itemParameterIndex = 0)
        => AddContainer(new DataContainerDescriptor(openGenericType, itemParameterIndex));

    /// <summary>Registers a container.</summary>
    public OpenApiGenericsRegistry AddContainer(DataContainerDescriptor descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        _containers[descriptor.OpenGenericType] = descriptor;
        return this;
    }

    /// <summary>Scans assemblies for types carrying <see cref="ApiWrapperAttribute"/> or <see cref="DataContainerAttribute"/>.</summary>
    public OpenApiGenericsRegistry AddFromAssemblies(params Assembly[] assemblies)
    {
        if (assemblies is null) throw new ArgumentNullException(nameof(assemblies));

        foreach (var type in assemblies.SelectMany(GetLoadableTypes))
        {
            if (!type.IsGenericTypeDefinition)
                continue;

            var wrapper = type.GetCustomAttribute<ApiWrapperAttribute>();
            if (wrapper is not null)
                AddWrapper(type, wrapper.DataParameterIndex, wrapper.DataPropertyName);

            var container = type.GetCustomAttribute<DataContainerAttribute>();
            if (container is not null)
                AddContainer(type, container.ItemParameterIndex);
        }

        return this;
    }

    /// <summary>Finds the envelope descriptor for a closed type such as <c>BaseResponse&lt;CountryDto&gt;</c>.</summary>
    public bool TryGetWrapper(Type closedType, out ApiWrapperDescriptor descriptor)
        => TryResolve(_wrappers, closedType, out descriptor!);

    /// <summary>Finds the container descriptor for a closed type such as <c>Page&lt;CustomerDto&gt;</c>.</summary>
    public bool TryGetContainer(Type closedType, out DataContainerDescriptor descriptor)
        => TryResolve(_containers, closedType, out descriptor!);

    private static bool TryResolve<TDescriptor>(Dictionary<Type, TDescriptor> source, Type closedType, out TDescriptor descriptor)
        where TDescriptor : class
    {
        descriptor = null!;

        if (closedType is null || !closedType.IsGenericType || closedType.IsGenericTypeDefinition)
            return false;

        return source.TryGetValue(closedType.GetGenericTypeDefinition(), out descriptor!);
    }

    // A referenced assembly that fails to load its full type list should not take the whole document down.
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}

/// <summary>Turns CLR type names into the arity-free names the metadata protocol uses.</summary>
public static class TypeNaming
{
    /// <summary>
    /// <c>Acme.Contracts.BaseResponse`1</c> becomes <c>Acme.Contracts.BaseResponse</c>. Nested types keep
    /// their <c>+</c> separator, which is how the CLR names them and how a reader resolves them back.
    /// </summary>
    public static string FullNameWithoutArity(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));

        var name = type.FullName ?? type.Name;
        return StripArity(name);
    }

    /// <summary><c>BaseResponse`1</c> becomes <c>BaseResponse</c>.</summary>
    public static string SimpleNameWithoutArity(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));

        return StripArity(type.Name);
    }

    private static string StripArity(string name)
    {
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name.Substring(0, tick);
    }
}

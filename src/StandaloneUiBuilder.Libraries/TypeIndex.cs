using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace StandaloneUiBuilder.Libraries;

/// <summary>An assembly opened for reading its metadata.</summary>
internal sealed class IndexedAssembly(string path, PEReader pe, MetadataReader reader)
{
    public string Path { get; } = path;

    public PEReader PE { get; } = pe;

    public MetadataReader Reader { get; } = reader;

    public string Name { get; } = reader.GetString(reader.GetAssemblyDefinition().Name);
}

/// <summary>A top-level type in an indexed assembly, by its full name without generic arity.</summary>
internal sealed record IndexedType(IndexedAssembly Assembly, TypeDefinitionHandle Handle, string Namespace, string Name)
{
    public string FullName => Namespace.Length > 0 ? $"{Namespace}.{Name}" : Name;
}

/// <summary>The types of a set of assemblies, found by name, as the scanner follows base classes and enums.</summary>
internal sealed class TypeIndex : IDisposable
{
    private readonly List<IndexedAssembly> assemblies = [];
    private readonly Dictionary<string, IndexedType> byName = new(StringComparer.Ordinal);

    public TypeIndex(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            PEReader? pe = null;
            try
            {
                pe = new PEReader(File.OpenRead(path));
                if (!pe.HasMetadata)
                {
                    pe.Dispose();
                    continue;
                }

                var reader = pe.GetMetadataReader();
                if (!reader.IsAssembly)
                {
                    pe.Dispose();
                    continue;
                }

                var assembly = new IndexedAssembly(System.IO.Path.GetFullPath(path), pe, reader);
                assemblies.Add(assembly);
                foreach (var handle in reader.TypeDefinitions)
                {
                    var definition = reader.GetTypeDefinition(handle);
                    if (definition.IsNested)
                    {
                        continue;
                    }

                    var type = new IndexedType(assembly, handle, reader.GetString(definition.Namespace), TypeNames.WithoutArity(reader.GetString(definition.Name)));
                    byName.TryAdd(type.FullName, type);
                    Types.Add(type);
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Not a .NET assembly the builder can read: skip it.
                pe?.Dispose();
            }
        }
    }

    public List<IndexedType> Types { get; } = [];

    public IndexedType? Find(string fullName) => byName.GetValueOrDefault(TypeNames.WithoutArity(fullName));

    /// <summary>An enum's members, if the type is an enum (without [Flags]) in one of the assemblies.</summary>
    public ImmutableList<string>? EnumMembers(string fullName)
    {
        if (Find(fullName) is not { } type)
        {
            return null;
        }

        var reader = type.Assembly.Reader;
        var definition = reader.GetTypeDefinition(type.Handle);
        if (definition.BaseType.IsNil || TypeNames.NameOf(reader, definition.BaseType) != "System.Enum")
        {
            return null;
        }

        foreach (var attribute in definition.GetCustomAttributes())
        {
            var constructor = reader.GetCustomAttribute(attribute).Constructor;
            if (constructor.Kind == HandleKind.MemberReference
                && TypeNames.NameOf(reader, reader.GetMemberReference((MemberReferenceHandle)constructor).Parent) == "System.FlagsAttribute")
            {
                return null;
            }
        }

        return [.. definition.GetFields()
            .Select(reader.GetFieldDefinition)
            .Where(f => (f.Attributes & (FieldAttributes.Static | FieldAttributes.Literal)) == (FieldAttributes.Static | FieldAttributes.Literal)
                && (f.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public)
            .Select(f => reader.GetString(f.Name))];
    }

    public void Dispose()
    {
        foreach (var assembly in assemblies)
        {
            assembly.PE.Dispose();
        }
    }
}

/// <summary>Type names from metadata, as full names: "System.Int32", "My.Controls.Rating", "List`1&lt;System.String&gt;".</summary>
internal sealed class TypeNames : ISignatureTypeProvider<string, object?>
{
    public static TypeNames Instance { get; } = new();

    public static string WithoutArity(string name) => name.Contains('`', StringComparison.Ordinal) ? name[..name.IndexOf('`', StringComparison.Ordinal)] : name;

    /// <summary>A nullable value's underlying type: System.Nullable&lt;System.Boolean&gt; is edited as System.Boolean.</summary>
    public static string Unwrap(string type) =>
        type.StartsWith("System.Nullable<", StringComparison.Ordinal) && type.EndsWith('>') ? type["System.Nullable<".Length..^1] : type;

    /// <summary>The full name a type reference or definition handle stands for; nested types as Outer.Inner.</summary>
    public static string? NameOf(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                var name = WithoutArity(reader.GetString(definition.Name));
                if (definition.IsNested)
                {
                    return NameOf(reader, definition.GetDeclaringType()) + "." + name;
                }

                var ns = reader.GetString(definition.Namespace);
                return ns.Length > 0 ? $"{ns}.{name}" : name;
            case HandleKind.TypeReference:
                var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                var referenceName = WithoutArity(reader.GetString(reference.Name));
                if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                {
                    return NameOf(reader, (TypeReferenceHandle)reference.ResolutionScope) + "." + referenceName;
                }

                var referenceNamespace = reader.GetString(reference.Namespace);
                return referenceNamespace.Length > 0 ? $"{referenceNamespace}.{referenceName}" : referenceName;
            case HandleKind.TypeSpecification:
                var decoded = reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(Instance, null);
                return decoded.Contains('<', StringComparison.Ordinal) ? decoded[..decoded.IndexOf('<', StringComparison.Ordinal)] : decoded;
            default:
                return null;
        }
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Void => "System.Void",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => "?",
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => NameOf(reader, handle) ?? "?";

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => NameOf(reader, handle) ?? "?";

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(",", typeArguments)}>";

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPinnedType(string elementType) => elementType;

    public string GetFunctionPointerType(MethodSignature<string> signature) => "?";

    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
}

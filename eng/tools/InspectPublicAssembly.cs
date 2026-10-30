using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Inspect only public contract metadata, without loading or decompiling vendor code.
if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run eng/tools/InspectPublicAssembly.cs -- assembly.dll [type-name-filter]");
    return 2;
}

using var input = File.OpenRead(args[0]);
using var image = new PEReader(input);
var metadata = image.GetMetadataReader();
var names = new SignatureNames();
foreach (var handle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(handle);
    var visibility = type.Attributes & TypeAttributes.VisibilityMask;
    if (visibility is not (TypeAttributes.Public or TypeAttributes.NestedPublic))
        continue;
    string name = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
    if (args.Length == 2 && !name.Contains(args[1], StringComparison.OrdinalIgnoreCase))
        continue;
    Console.WriteLine(name);
    foreach (var fieldHandle in type.GetFields())
    {
        var field = metadata.GetFieldDefinition(fieldHandle);
        if ((field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public)
            Console.WriteLine($"  field {metadata.GetString(field.Name)}");
    }
    foreach (var methodHandle in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(methodHandle);
        if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            continue;
        var signature = method.DecodeSignature(names, (object?)null);
        var parameters = method.GetParameters()
            .Select(metadata.GetParameter)
            .Where(parameter => parameter.SequenceNumber > 0)
            .OrderBy(parameter => parameter.SequenceNumber)
            .Select(parameter => metadata.GetString(parameter.Name)
                + ((parameter.Attributes & ParameterAttributes.Optional) != 0 ? " [optional]" : string.Empty))
            .ToArray();
        Console.WriteLine($"  {signature.ReturnType} {metadata.GetString(method.Name)}({string.Join(", ", signature.ParameterTypes.Select((parameterType, index) => parameterType + " " + parameters[index]))})");
    }
}
return 0;

sealed class SignatureNames : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "function-pointer";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
    }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

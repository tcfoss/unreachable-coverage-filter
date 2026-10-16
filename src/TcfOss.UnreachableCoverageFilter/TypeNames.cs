using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace TcfOss.UnreachableCoverageFilter;

public sealed class TypeNames(MetadataReader metadata) : ISignatureTypeProvider<string, GenericContext>
{
    public string GetArrayType(string elementType, ArrayShape shape)
    {
        return elementType + "[" + new string(',', shape.Rank - 1) + "]";
    }

    public string GetByReferenceType(string elementType)
    {
        return elementType + "&";
    }

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        return "methodptr";
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        return genericType + "<" + string.Join(",", typeArguments) + ">";
    }

    public string GetGenericMethodParameter(GenericContext genericContext, int index)
    {
        return metadata.GetString(metadata.GetGenericParameter(metadata.GetMethodDefinition(genericContext.Method).GetGenericParameters().ElementAt(index)).Name);
    }

    public string GetGenericTypeParameter(GenericContext genericContext, int index)
    {
        return metadata.GetString(metadata.GetGenericParameter(metadata.GetTypeDefinition(genericContext.Type).GetGenericParameters().ElementAt(index)).Name);
    }

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
    {
        return unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
    }

    public string GetPinnedType(string elementType)
    {
        return elementType;
    }

    public string GetPointerType(string elementType)
    {
        return elementType + "*";
    }

    public string GetSZArrayType(string elementType)
    {
        return elementType + "[]";
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return "System." + (typeCode == PrimitiveTypeCode.TypedReference ? "TypedReference" : typeCode.ToString());
    }

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        return type.IsNested
            ? GetTypeFromDefinition(reader, type.GetDeclaringType(), 0) + "/" + reader.GetString(type.Name)
            : Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        TypeReference type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, 0) + "/" + reader.GetString(type.Name)
            : Qualify(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    public string GetTypeFromSpecification(MetadataReader reader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }

    private static string Qualify(string namespaceName, string name)
    {
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }
}

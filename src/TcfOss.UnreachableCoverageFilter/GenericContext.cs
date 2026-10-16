using System.Reflection.Metadata;

namespace TcfOss.UnreachableCoverageFilter;

public sealed record GenericContext(TypeDefinitionHandle Type, MethodDefinitionHandle Method);

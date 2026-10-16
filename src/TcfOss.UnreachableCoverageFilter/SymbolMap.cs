using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TcfOss.UnreachableCoverageFilter;

public sealed class SymbolMap : IDisposable
{
    private readonly FileStream _assemblyStream;
    private readonly PEReader _assembly;
    private readonly MetadataReaderProvider _symbols;
    private readonly Dictionary<string, MethodDefinitionHandle> _methods = new(StringComparer.Ordinal);

    public SymbolMap(string assemblyPath)
    {
        _assemblyStream = File.OpenRead(assemblyPath);
        _assembly = new PEReader(_assemblyStream);
        try
        {
            if (!_assembly.TryOpenAssociatedPortablePdb(assemblyPath, File.OpenRead, out MetadataReaderProvider? symbols, out _))
            {
                throw new InvalidDataException($"No matching portable PDB for {assemblyPath}.");
            }
            _symbols = symbols ?? throw new InvalidDataException($"No matching portable PDB for {assemblyPath}.");
            MetadataReader metadata = _assembly.GetMetadataReader();
            var names = new TypeNames(metadata);
            foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
            {
                MethodDefinition method = metadata.GetMethodDefinition(handle);
                var context = new GenericContext(method.GetDeclaringType(), handle);
                MethodSignature<string> signature = method.DecodeSignature(names, context);
                string typeName = names.GetTypeFromDefinition(metadata, method.GetDeclaringType(), 0);
                string name = $"{signature.ReturnType} {typeName}::{metadata.GetString(method.Name)}({string.Join(",", signature.ParameterTypes)})";
                if (!_methods.TryAdd(name, handle))
                {
                    _methods[name] = default;
                }
            }
        }
        catch
        {
            _assembly.Dispose();
            _assemblyStream.Dispose();
            throw;
        }
    }

    public SourceLocation? Resolve(string methodName, int destinationOffset)
    {
        if (!_methods.TryGetValue(methodName, out MethodDefinitionHandle handle) || handle.IsNil)
        {
            return null;
        }

        MetadataReader metadata = _assembly.GetMetadataReader();
        MethodDefinition method = metadata.GetMethodDefinition(handle);
        if (method.RelativeVirtualAddress == 0 || destinationOffset < 0
            || destinationOffset >= _assembly.GetMethodBody(method.RelativeVirtualAddress).GetILContent().Length)
        {
            return null;
        }
        MetadataReader pdb = _symbols.GetMetadataReader();
        MethodDebugInformation debug = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
        SequencePoint? containing = null;
        foreach (SequencePoint point in debug.GetSequencePoints())
        {
            if (point.Offset > destinationOffset)
            {
                break;
            }
            containing = point;
        }
        if (containing is not { IsHidden: false } visible)
        {
            return null;
        }
        DocumentHandle document = visible.Document.IsNil ? debug.Document : visible.Document;
        return document.IsNil ? null : GetLocation(pdb, document, visible.StartLine, visible.EndLine);
    }

    public SourceLocation? ResolveLine(string methodName, int line)
    {
        if (!_methods.TryGetValue(methodName, out MethodDefinitionHandle handle) || handle.IsNil)
        {
            return null;
        }
        MetadataReader pdb = _symbols.GetMetadataReader();
        MethodDebugInformation debug = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
        SourceLocation[] locations = [.. debug.GetSequencePoints().Where(point => !point.IsHidden && point.StartLine == line)
            .Select(point => GetLocation(pdb, point.Document.IsNil ? debug.Document : point.Document, point.StartLine, point.EndLine))
            .DistinctBy(location => (location.Path, location.StartLine, location.EndLine))];
        return locations.Length == 1 ? locations[0] : null;
    }

    private static SourceLocation GetLocation(MetadataReader pdb, DocumentHandle handle, int start, int end)
    {
        Document document = pdb.GetDocument(handle);
        return new SourceLocation(pdb.GetString(document.Name), start, end,
            FileHasher.GetHasher(pdb.GetGuid(document.HashAlgorithm)), [.. pdb.GetBlobBytes(document.Hash)]);
    }

    public void Dispose()
    {
        _symbols.Dispose();
        _assembly.Dispose();
        _assemblyStream.Dispose();
    }
}

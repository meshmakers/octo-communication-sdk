using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// Path-only data context exposed to ETL pipeline nodes. All access to the underlying
/// document is performed through string paths; no JSON object types appear in the API surface.
/// </summary>
/// <remarks>
/// Implements <see cref="IDisposable"/> because root contexts may own a <see cref="JsonDocument"/>
/// whose unmanaged buffer is released only on <see cref="IDisposable.Dispose"/>. Callers that
/// construct a root context (typically <c>DataContextImpl</c>) should wrap it in <c>using</c>.
/// Child / iteration contexts hold no owned document; their <c>Dispose</c> is a no-op.
/// </remarks>
public interface IDataContext : IDisposable
{
    /// <summary>Reference to the parent context, or <c>null</c> if this is the root context.</summary>
    IDataContext? Parent { get; }

    /// <summary>Returns true if a value exists at the given path.</summary>
    bool Exists(string path);

    /// <summary>Returns the <see cref="DataKind"/> classification of the value at the given path.</summary>
    DataKind GetKind(string path);

    /// <summary>Returns the length of the array or string at the given path.</summary>
    int Length(string path);

    /// <summary>Enumerates the property keys of the object at the given path.</summary>
    IEnumerable<string> Keys(string path);

    /// <summary>Reads the value at the given path and deserializes it to <typeparamref name="T"/>.</summary>
    T? Get<T>(string path);

    /// <summary>
    /// Reads the array at the given path and deserializes each element to <typeparamref name="T"/>.
    /// Serialization uses <see cref="SystemTextJsonOptions.Default"/> (the SDK default, carrying CK/Rt converters).
    /// </summary>
    IEnumerable<T?>? GetArray<T>(string path);

    /// <summary>
    /// Reads the value at <paramref name="path"/> as its natural CLR scalar
    /// (bool / long / double / DateTime / string / null), via the shared JsonScalar rules.
    /// Object and array kinds return null — navigate those via <c>Get&lt;T&gt;</c>.
    /// </summary>
    object? GetValue(string path, bool parseDateStrings = true);

    /// <summary>
    /// Reads <paramref name="path"/> as <typeparamref name="T"/>. Returns false when the path is
    /// absent (distinguishing missing from a present default), true otherwise (including explicit null).
    /// </summary>
    bool TryGet<T>(string path, out T? value);

    /// <summary>Writes a value to the overlay at the given path using default semantics.</summary>
    void Set<T>(string path, T? value);

    /// <summary>Writes a value to the overlay at the given path with explicit document/value/write semantics.</summary>
    void Set<T>(string path,
        T? value,
        DocumentModes documentMode,
        ValueKinds valueKind,
        TargetValueWriteModes writeMode);

    /// <summary>Removes the value at the given path from the overlay.</summary>
    void Clear(string path);

    /// <summary>Iterates the array at the given path, invoking <paramref name="body"/> for each element with a child context.</summary>
    Task IterateArrayAsync(string path, Func<IDataContext, Task> body);

    /// <summary>
    /// Iterates the array at <paramref name="path"/> like <see cref="IterateArrayAsync(string, Func{IDataContext, Task})"/>,
    /// but each child context exposes additional alias paths whose values are resolved once
    /// from the parent up front. This lets the body of the loop read e.g. <c>$.full.X</c>
    /// to access fields on the outer document while still iterating individual array items.
    /// </summary>
    /// <param name="path">JSONPath of the array to iterate.</param>
    /// <param name="aliases">
    /// Alias entries: each pair maps a logical alias path (e.g. <c>$.full</c>) visible to the
    /// child context to a source path (e.g. <c>$</c>) evaluated against this parent context
    /// before iteration begins. Reads on the child match aliases by longest prefix.
    /// </param>
    /// <param name="body">Callback invoked per array element with a child context.</param>
    Task IterateArrayAsync(string path, IReadOnlyList<(string Alias, string SourcePath)> aliases,
        Func<IDataContext, Task> body);

    /// <summary>Iterates the object properties at the given path, invoking <paramref name="body"/> for each (key, child context).</summary>
    Task IterateObjectAsync(string path, Func<string, IDataContext, Task> body);

    /// <summary>Iterates each match of a JSONPath expression, invoking <paramref name="body"/> with a child context.</summary>
    Task IterateMatchesAsync(string jsonPath, Func<IDataContext, Task> body);

    /// <summary>
    /// For each match of <paramref name="jsonPath"/>, invokes <paramref name="body"/> with a
    /// sub-context rooted at that match. Mutations performed by the body are written back to
    /// THIS context's overlay at the match's canonical path. Allocations are proportional to
    /// what the body writes, not to the full document size.
    /// </summary>
    /// <remarks>
    /// The body uses Get/Set on the sub-context to read and modify the match in place via
    /// "$.&lt;sub-property&gt;" paths. Mutations are applied to this context's overlay after the
    /// body completes for each match.
    /// </remarks>
    Task UpdateMatchesAsync(string jsonPath, Func<IDataContext, Task> body);

    /// <summary>Returns a detached sub-context rooted at <paramref name="path"/>, or null if absent.
    /// The returned context owns its backing document; dispose it when done. Writes to it do NOT
    /// merge back into this context.</summary>
    IDataContext? Select(string path);

    /// <summary>Returns a detached, read-oriented sub-context per match of <paramref name="jsonPath"/>.
    /// Writes to a returned context do NOT merge back (use <see cref="UpdateMatchesAsync"/> for that).
    /// Replaces the former EnumerateMatches JsonNode escape hatch.</summary>
    IEnumerable<IDataContext> SelectMatches(string jsonPath);

    /// <summary>Copies the value at <paramref name="sourcePath"/> to <paramref name="targetPath"/>.</summary>
    void CopyTo(string sourcePath, string targetPath);

    /// <summary>Writes the JSON value at the given path to the destination stream as UTF-8.</summary>
    void WriteJsonTo(string path, Stream destination);

    /// <summary>Sets the value at the given path from a UTF-8 JSON document.</summary>
    void SetFromJson(string path, ReadOnlyMemory<byte> utf8Json);
}

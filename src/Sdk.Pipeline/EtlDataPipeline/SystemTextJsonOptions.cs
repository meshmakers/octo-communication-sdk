using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline;

/// <summary>
/// The OctoMesh ETL pipeline System.Text.Json options. A thin pipeline-flavored override of the
/// canonical Rt-model serializer <see cref="RtSystemTextJsonSerializer"/> (octo-construction-kit-engine,
/// <c>Runtime.Contracts/Serialization/</c>, next to <c>RtNewtonsoftSerializer</c>).
/// </summary>
/// <remarks>
/// <para>
/// The converter family (CK/Rt id converters + <c>RtAttributesConverter</c>), case-insensitive
/// property matching, and lenient number handling all come from
/// <see cref="RtSystemTextJsonSerializer"/>. This bundle changes exactly one thing for the pipeline:
/// </para>
/// <para>
/// <b>Null preservation.</b> <see cref="RtSystemTextJsonSerializer"/> drops null properties (matching
/// the legacy <c>RtNewtonsoftSerializer</c>). The pipeline instead <b>preserves</b> explicit nulls
/// (<see cref="JsonIgnoreCondition.Never"/>) so node code can distinguish "intentionally null" from
/// "absent": <c>GetKind("$.x")</c> returns <c>DataKind.Null</c> for the former and
/// <c>DataKind.Undefined</c> for the latter, and patch / CK-mutation semantics depend on that
/// distinction end-to-end.
/// </para>
/// <para>
/// <b>Per-call null-strip override.</b> A wire-format consumer that needs nulls dropped can copy and
/// flip the condition locally rather than mutating this bundle:
/// </para>
/// <code>
/// var wireOptions = new JsonSerializerOptions(SystemTextJsonOptions.Default)
/// {
///     DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
/// };
/// JsonSerializer.Serialize(dto, wireOptions);
/// </code>
/// </remarks>
public static class SystemTextJsonOptions
{
    /// <summary>
    /// Shared default options instance used across the pipeline, mesh-adapter, and downstream plugins
    /// that round-trip CK runtime types through System.Text.Json. Preserves explicit nulls.
    /// </summary>
    public static readonly JsonSerializerOptions Default = CreateDefault();

    /// <summary>
    /// Options for constructing the DataContext's document <see cref="System.Text.Json.Nodes.JsonNode"/>
    /// tree — identical to <see cref="Default"/> EXCEPT <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>
    /// is <c>false</c>, so <c>JsonObject</c>s built via this bundle navigate property names
    /// <b>case-sensitively</b> (Newtonsoft <c>JObject</c> / JSONPath <c>SelectToken</c> parity).
    /// </summary>
    /// <remarks>
    /// Newtonsoft's behavior is split: document navigation (the <c>JObject</c> indexer and
    /// <c>SelectToken</c>) is case-SENSITIVE, while typed <c>ToObject&lt;T&gt;</c> CLR-member binding is
    /// case-INSENSITIVE. The pipeline matches both by using THIS bundle only for the element→node
    /// bridge that produces the navigable document tree (see <c>JsonDetach.ToNode</c> and the
    /// CLR→node path of <c>DataContextImpl.Set&lt;T&gt;</c>), while typed <c>Get&lt;T&gt;</c>
    /// (<c>Deserialize&lt;T&gt;</c>) keeps using the case-insensitive <see cref="Default"/>.
    /// The CK-engine runtime types (<c>EntityUpdateInfo&lt;T&gt;</c>, <c>RtEntity</c>, <c>RtRecord</c>,
    /// <c>RtAssociation</c>) have camelCase <c>[JsonConstructor]</c> parameters bound against PascalCase
    /// wire keys, so their typed binding REQUIRES case-insensitivity — which is why the flag is split
    /// here rather than flipped on <see cref="Default"/>. All other settings (converters, encoder,
    /// number handling, null preservation) are inherited unchanged from <see cref="Default"/>.
    /// </remarks>
    public static readonly JsonSerializerOptions NodeNavigation = CreateNodeNavigation();

    /// <summary>
    /// Creates a fresh pipeline options instance: the <see cref="RtSystemTextJsonSerializer"/> default
    /// with null preservation enabled. Callers needing further overrides start from this and copy.
    /// </summary>
    public static JsonSerializerOptions CreateDefault()
    {
        var options = RtSystemTextJsonSerializer.CreateDefault();
        // Pipeline-specific: keep explicit nulls so DataKind.Null and DataKind.Undefined stay distinct.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        return options;
    }

    /// <summary>
    /// Creates a fresh document-node-construction options instance: <see cref="CreateDefault"/> with
    /// case-sensitive property matching. JsonNodes deserialized under these options carry
    /// <c>JsonNodeOptions.PropertyNameCaseInsensitive == false</c>, making their
    /// <c>TryGetPropertyValue</c>/indexer navigation case-sensitive (Newtonsoft <c>JObject</c> parity).
    /// </summary>
    public static JsonSerializerOptions CreateNodeNavigation()
    {
        var options = new JsonSerializerOptions(CreateDefault());
        // Document navigation is case-sensitive (Newtonsoft JObject/SelectToken parity); typed
        // Deserialize<T> stays case-insensitive on Default (Newtonsoft ToObject<T> parity).
        options.PropertyNameCaseInsensitive = false;
        return options;
    }
}

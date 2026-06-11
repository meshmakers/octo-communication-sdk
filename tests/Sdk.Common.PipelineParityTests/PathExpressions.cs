namespace Sdk.Common.PipelineParityTests;

/// <summary>
/// Curated JSONPath corpus spanning every dialect feature supported by the
/// <c>JsonPathParser</c>/<c>JsonPathWalker</c>: root, dotted property access,
/// integer index, single and chained wildcards, recursive descent (with and without
/// trailing property/wildcard/filter), string-equality filter (including the
/// <c>$..[?(@.X == 'lit')]</c> production-style pattern), and edge cases that yield
/// empty result sets.
///
/// Used by <c>ReadParityTests</c> to assert that for every (input document, path)
/// pair, Newtonsoft's <c>SelectTokens</c> and the walker agree.
/// </summary>
public static class PathExpressions
{
    public static readonly string[] All =
    {
        // Root and dotted
        "$",
        "$.foo",
        "$.orders",
        "$.EdaMessages",
        "$.Machines",
        "$.full_doc.nested",
        "$._items",

        // Array index
        "$.orders[0]",
        "$.orders[1]",
        "$.EdaMessages[0].MeteringPoints[0]",
        "$.Machines[0].Id",

        // Wildcards (single and chained)
        "$.orders[*]",
        "$.orders[*].id",
        "$.orders[*].items[*]",
        "$.orders[*].items[*].sku",
        "$.EdaMessages[*].MeteringPoints[*]",
        "$.EdaMessages[*].MeteringPoints[*].Value",
        "$.Machines[*].Status",

        // Recursive descent
        "$..MeteringPoints",
        "$..Status",
        "$..items",
        "$..Id",

        // Recursive descent + wildcard
        "$..MeteringPoints[*]",
        "$..items[*]",

        // Equality filters on arrays
        "$.Machines[?(@.Id == 'machine_1')]",
        "$.Machines[?(@.Id == 'machine_3')].Value",
        "$.orders[?(@.customer.attrs.code == 'X1')]",
        "$.EdaMessages[*].MeteringPoints[?(@.Status == 'OK')]",

        // Recursive descent + filter (production-style pattern)
        "$..[?(@.Id == 'machine_1')].Value",
        "$..[?(@.Id == 'machine_2')].Status",
        "$..[?(@.Status == 'OK')]",

        // Empty / edge cases
        "$.missing.path",
        "$.orders[99]",
        "$.Machines[?(@.Id == 'no_such_machine')]",
    };
}

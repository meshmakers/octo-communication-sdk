# FormatStringNode Usage Example

The `FormatStringNode` allows you to format strings using JSON path placeholders within your ETL pipeline.

## Configuration

```json
{
  "type": "FormatString",
  "version": 1,
  "configuration": {
    "format": "User {$.user.name} from {$.user.location} has {$.orderCount} orders",
    "targetPath": "$.formattedMessage",
    "nullValue": "N/A"
  }
}
```

## Features

- **C# String Interpolation Style**: Use `{$.path.to.value}` syntax for placeholders
- **Multiple Placeholders**: Support for multiple JSON paths in a single format string
- **Null Handling**: Configurable null value representation (default: "NULL")
- **Error Handling**: Throws `PipelineExecutionException` for:
  - Non-existent JSON paths
  - Paths resolving to objects or arrays (only simple values allowed)

## Example Input Data

```json
{
  "user": {
    "name": "John Doe",
    "location": "New York",
    "email": "john@example.com"
  },
  "orderCount": 42,
  "lastLogin": null
}
```

## Example Configurations

### Basic formatting:
```json
{
  "format": "Welcome {$.user.name}!",
  "targetPath": "$.welcomeMessage"
}
```
Output: `"Welcome John Doe!"`

### With null values:
```json
{
  "format": "Last login: {$.lastLogin}",
  "targetPath": "$.loginStatus",
  "nullValue": "Never"
}
```
Output: `"Last login: Never"`

### Complex formatting:
```json
{
  "format": "Customer: {$.user.name} ({$.user.email}) - Orders: {$.orderCount}",
  "targetPath": "$.customerSummary"
}
```
Output: `"Customer: John Doe (john@example.com) - Orders: 42"`

## Integration in Pipeline

The node can be used in a pipeline configuration like this:

```json
{
  "pipeline": {
    "nodes": [
      {
        "name": "FormatUserMessage",
        "type": "FormatString",
        "configuration": {
          "format": "Hello {$.firstName} {$.lastName}, your account balance is {$.balance}",
          "targetPath": "$.personalizedMessage",
          "nullValue": "Unknown"
        }
      }
    ]
  }
}
```

## Error Cases

The node will throw a `PipelineExecutionException` if:
1. A JSON path doesn't exist in the data
2. A JSON path resolves to an object or array (only primitive values are supported)
3. Invalid JSON path syntax is used
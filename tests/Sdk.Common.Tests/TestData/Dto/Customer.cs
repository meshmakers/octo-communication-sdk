namespace Sdk.Common.Tests.TestData.Dto;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string EMail { get; set; } = null!;
    public string? Tel1 { get; set; }
    public string? Tel2 { get; set; }
    public string? VatId { get; set; }

}
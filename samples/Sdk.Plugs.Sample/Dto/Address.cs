namespace Sdk.Plugs.Sample.Dto;

public class Address
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Company { get; set; }
    public string? NameAddition { get; set; }
    public string Street { get; set; } = null!;
    public string HouseNumber { get; set; } = null!;
    public string Zip { get; set; } = null!;
    public string City { get; set; } = null!;
    public string? CountryIso2 { get; set; }
    public string Country { get; set; } = null!;
    public string EMail { get; set; } = null!;
    public string Phone { get; set; } = null!;
}
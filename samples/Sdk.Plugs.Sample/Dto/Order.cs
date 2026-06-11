namespace Sdk.Plugs.Sample.Dto;

public class Order
{
    public int InvoiceNumber { get; set; }
    public DateTime InvoiceDate { get; set; }
    public Address InvoiceAddress { get; set; } = null!;
    public Address ShippingAddress { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public OrderItem[] Items { get; set; } = null!;
}
namespace NoneadCSS.Domain.Entities;

public class Email
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
    public int Type { get; set; } = 1;

    public Customer Customer { get; set; } = null!;
}

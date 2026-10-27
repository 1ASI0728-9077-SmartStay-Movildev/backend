namespace SmartStay.Profiles.Domain.Model.ValueObjects;

public record StreetAddress(string Street, string Number, string City, string PostalCode, string Country)
{
    public StreetAddress() : this(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty) { }
    public string FullAddress => $"{Street} {Number}, {City}, {PostalCode}, {Country}".Trim();
}

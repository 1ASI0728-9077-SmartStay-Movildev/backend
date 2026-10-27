using SmartStay.Profiles.Domain.Model.Commands;
using SmartStay.Profiles.Domain.Model.ValueObjects;

namespace SmartStay.Profiles.Domain.Model.Aggregates;

public class Profile
{
    public Profile()
    {
        Name = new PersonName();
        Email = new EmailAddress();
        Address = new StreetAddress();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Profile(string firstName, string lastName, string email, string street, string number, string city, string postalCode, string country, int? userId = null)
    {
        Name = new PersonName(firstName, lastName);
        Email = new EmailAddress(email);
        Address = new StreetAddress(street, number, city, postalCode, country);
        UserId = userId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Profile(CreateProfileCommand command) : this(
        command.FirstName, command.LastName, command.Email, command.Street, command.Number, command.City, command.PostalCode, command.Country, command.UserId)
    {
    }

    public int Id { get; private set; }
    public int? UserId { get; private set; }
    public PersonName Name { get; private set; }
    public EmailAddress Email { get; private set; }
    public StreetAddress Address { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public string FullName => Name.FullName;
    public string EmailAddress => Email.Address;
    public string StreetAddress => Address.FullAddress;

    public void LinkToUser(int userId)
    {
        UserId = userId;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

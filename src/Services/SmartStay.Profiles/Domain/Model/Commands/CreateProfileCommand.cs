namespace SmartStay.Profiles.Domain.Model.Commands;

public record CreateProfileCommand(
    string FirstName,
    string LastName,
    string Email,
    string Street,
    string Number,
    string City,
    string PostalCode,
    string Country,
    int? UserId = null
);

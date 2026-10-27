using System.ComponentModel.DataAnnotations;

namespace SmartStay.Profiles.Interfaces.REST.Resources;

public record CreateProfileResource(
    [Required] string FirstName,
    [Required] string LastName,
    [Required] [EmailAddress] string Email,
    [Required] string Street,
    [Required] string Number,
    [Required] string City,
    [Required] string PostalCode,
    [Required] string Country,
    int? UserId = null
);

public record ProfileResource(
    int Id,
    string FullName,
    string Email,
    string StreetAddress,
    int? UserId
);

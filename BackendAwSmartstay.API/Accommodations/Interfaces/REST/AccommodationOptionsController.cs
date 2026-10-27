using System.Net.Mime;
using BackendAwSmartstay.API.Accommodations.Domain.Services;
using BackendAwSmartstay.API.Accommodations.Interfaces.REST.Resources;
using BackendAwSmartstay.API.IAM.Domain.Model.Constants;
using BackendAwSmartstay.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace BackendAwSmartstay.API.Accommodations.Interfaces.REST;

/// <summary>
/// RESTful API interface controller responsible for handling master data options (Categories and Amenities).
/// Desacoplado de AppDbContext: utiliza servicios de aplicación del dominio Accommodations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/accommodations/options")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Accommodation Options (Master Data)")]
public class AccommodationOptionsController(
    IAccommodationOptionsQueryService queryService,
    IAccommodationOptionsCommandService commandService) : ControllerBase
{
    [HttpGet("categories")]
    [Authorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get hotel categories catalogue", OperationId = "GetCategories")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of categories", typeof(IEnumerable<string>))]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await queryService.GetAllCategoriesAsync();
        return Ok(categories);
    }

    [HttpGet("amenities")]
    [Authorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get available room and hotel amenities", OperationId = "GetAmenities")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of amenities", typeof(IEnumerable<string>))]
    public async Task<IActionResult> GetAmenities()
    {
        var amenities = await queryService.GetAllAmenitiesAsync();
        return Ok(amenities);
    }

    [HttpPost("categories")]
    [Authorize(UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Create a new hotel category", OperationId = "CreateCategory")]
    [SwaggerResponse(StatusCodes.Status201Created, "Created category", typeof(string))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "Category already exists")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryResource resource)
    {
        var (success, error) = await commandService.CreateCategoryAsync(resource.Name);
        if (!success) return Conflict(error);

        return CreatedAtAction(nameof(GetCategories), new { }, resource.Name);
    }

    [HttpPost("amenities")]
    [Authorize(UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Create a new amenity", OperationId = "CreateAmenity")]
    [SwaggerResponse(StatusCodes.Status201Created, "Created amenity", typeof(string))]
    [SwaggerResponse(StatusCodes.Status409Conflict, "Amenity already exists")]
    public async Task<IActionResult> CreateAmenity([FromBody] CreateAmenityResource resource)
    {
        var (success, error) = await commandService.CreateAmenityAsync(resource.Name);
        if (!success) return Conflict(error);

        return CreatedAtAction(nameof(GetAmenities), new { }, resource.Name);
    }
}

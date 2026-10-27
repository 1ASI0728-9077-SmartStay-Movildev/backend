using System.Net.Mime;
using BackendAwSmartstay.API.Accommodations.Domain.Services;
using BackendAwSmartstay.API.Accommodations.Interfaces.REST.Resources;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Accommodations.Infrastructure.Security;
using SmartStay.Contracts.Constants;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Accommodations.Interfaces.REST;

[AccommodationsAuthorize]
[ApiController]
[Route("api/v1/accommodations/options")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Accommodation Options (Master Data)")]
public class AccommodationOptionsController(
    IAccommodationOptionsQueryService queryService,
    IAccommodationOptionsCommandService commandService) : ControllerBase
{
    [HttpGet("categories")]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get hotel categories catalogue", OperationId = "GetCategories")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of categories", typeof(IEnumerable<string>))]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await queryService.GetAllCategoriesAsync();
        return Ok(categories);
    }

    [HttpGet("amenities")]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get available room and hotel amenities", OperationId = "GetAmenities")]
    [SwaggerResponse(StatusCodes.Status200OK, "List of amenities", typeof(IEnumerable<string>))]
    public async Task<IActionResult> GetAmenities()
    {
        var amenities = await queryService.GetAllAmenitiesAsync();
        return Ok(amenities);
    }

    [HttpPost("categories")]
    [AccommodationsAuthorize(UserRoles.ChainAdmin)]
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
    [AccommodationsAuthorize(UserRoles.ChainAdmin)]
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

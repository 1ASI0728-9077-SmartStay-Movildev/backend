using System.Net.Mime;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Commands;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Queries;
using BackendAwSmartstay.API.Accommodations.Domain.Services;
using BackendAwSmartstay.API.Accommodations.Interfaces.REST.Resources;
using BackendAwSmartstay.API.Accommodations.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Accommodations.Infrastructure.Security;
using SmartStay.Contracts.Constants;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Accommodations.Interfaces.REST;

/// <summary>
/// RESTful API interface controller for Hotels in SmartStay.Accommodations microservice.
/// </summary>
[AccommodationsAuthorize]
[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Hotel Endpoints")]
public class HotelsController(
    IHotelCommandService hotelCommandService,
    IHotelQueryService hotelQueryService) : ControllerBase
{
    [HttpGet]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get all hotels", OperationId = "GetAllHotels")]
    [SwaggerResponse(StatusCodes.Status200OK, "The hotel resource list was successfully fetched.", typeof(IEnumerable<HotelResource>))]
    public async Task<IActionResult> GetAllHotels()
    {
        var hotels = await hotelQueryService.Handle(new GetAllHotelsQuery());
        var resources = hotels.Select(HotelResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpGet("{hotelId:int}")]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get hotel by its unique identifier", OperationId = "GetHotelById")]
    [SwaggerResponse(StatusCodes.Status200OK, "The target hotel was located and returned.", typeof(HotelResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No hotel was identified with the provided query parameters.")]
    public async Task<IActionResult> GetHotelById(int hotelId)
    {
        var hotel = await hotelQueryService.Handle(new GetHotelByIdQuery(hotelId));
        if (hotel is null) return NotFound();
        var resource = HotelResourceFromEntityAssembler.ToResourceFromEntity(hotel);
        return Ok(resource);
    }

    [HttpPost]
    [AccommodationsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Create a new hotel property node", OperationId = "CreateHotel")]
    [SwaggerResponse(StatusCodes.Status201Created, "The hotel aggregate was created successfully.", typeof(HotelResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The incoming resource creation payload is invalid.")]
    public async Task<IActionResult> CreateHotel([FromBody] CreateHotelResource resource)
    {
        var createHotelCommand = CreateHotelCommandFromResourceAssembler.ToCommandFromResource(resource);
        var hotel = await hotelCommandService.Handle(createHotelCommand);
        if (hotel is null) return BadRequest();
        var hotelResource = HotelResourceFromEntityAssembler.ToResourceFromEntity(hotel);
        return CreatedAtAction(nameof(GetHotelById), new { hotelId = hotel.Id }, hotelResource);
    }

    [HttpPut("{hotelId:int}")]
    [AccommodationsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Update structural hotel properties", OperationId = "UpdateHotel")]
    [SwaggerResponse(StatusCodes.Status200OK, "The target hotel was updated successfully.", typeof(HotelResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No matching hotel was found to apply updates.")]
    public async Task<IActionResult> UpdateHotel(int hotelId, [FromBody] UpdateHotelResource resource)
    {
        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor != null && !actor.CanAccessHotel(hotelId))
        {
            return Forbid();
        }

        var updateHotelCommand = UpdateHotelCommandFromResourceAssembler.ToCommandFromResource(hotelId, resource);
        var hotel = await hotelCommandService.Handle(updateHotelCommand);
        if (hotel is null) return NotFound();
        var hotelResource = HotelResourceFromEntityAssembler.ToResourceFromEntity(hotel);
        return Ok(hotelResource);
    }

    [HttpDelete("{hotelId:int}")]
    [AccommodationsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Delete a registered hotel property", OperationId = "DeleteHotel")]
    [SwaggerResponse(StatusCodes.Status200OK, "The hotel aggregate was successfully deleted.", typeof(HotelResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No matching hotel was found.")]
    public async Task<IActionResult> DeleteHotel(int hotelId)
    {
        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor != null && !actor.CanAccessHotel(hotelId))
        {
            return Forbid();
        }

        var deleteHotelCommand = new DeleteHotelCommand(hotelId);
        var hotel = await hotelCommandService.Handle(deleteHotelCommand);
        if (hotel is null) return NotFound();
        var hotelResource = HotelResourceFromEntityAssembler.ToResourceFromEntity(hotel);
        return Ok(hotelResource);
    }
}

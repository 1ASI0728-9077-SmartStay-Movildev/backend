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
/// RESTful API interface controller for RoomTypes in SmartStay.Accommodations microservice.
/// </summary>
[AccommodationsAuthorize]
[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Room Type Endpoints")]
public class RoomTypesController(
    IRoomTypeCommandService roomTypeCommandService,
    IRoomTypeQueryService roomTypeQueryService) : ControllerBase
{
    [HttpGet("{roomTypeId:int}")]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get room type by its unique identifier", OperationId = "GetRoomTypeById")]
    [SwaggerResponse(StatusCodes.Status200OK, "The room type category was located and converted successfully.", typeof(RoomTypeResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No room type aggregate matched the supplied structural identifier.")]
    public async Task<IActionResult> GetRoomTypeById(int roomTypeId)
    {
        var getRoomTypeByIdQuery = new GetRoomTypeByIdQuery(roomTypeId);
        var roomType = await roomTypeQueryService.Handle(getRoomTypeByIdQuery);
        if (roomType is null) return NotFound();
        var resource = RoomTypeResourceFromEntityAssembler.ToResourceFromEntity(roomType);
        return Ok(resource);
    }

    [HttpPost]
    [AccommodationsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Create a new room type category", OperationId = "CreateRoomType")]
    [SwaggerResponse(StatusCodes.Status201Created, "The room type category was registered successfully.", typeof(RoomTypeResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The incoming input parameters are invalid.")]
    public async Task<IActionResult> CreateRoomType([FromBody] CreateRoomTypeResource resource)
    {
        var createRoomTypeCommand = CreateRoomTypeCommandFromResourceAssembler.ToCommandFromResource(resource);
        var roomType = await roomTypeCommandService.Handle(createRoomTypeCommand);
        if (roomType is null) return BadRequest();
        var roomTypeResource = RoomTypeResourceFromEntityAssembler.ToResourceFromEntity(roomType);
        return CreatedAtAction(nameof(GetRoomTypeById), new { roomTypeId = roomType.Id }, roomTypeResource);
    }

    [HttpGet]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get all registered room type categories", OperationId = "GetAllRoomTypes")]
    [SwaggerResponse(StatusCodes.Status200OK, "The entire list of room types was extracted.", typeof(IEnumerable<RoomTypeResource>))]
    public async Task<IActionResult> GetAllRoomTypes()
    {
        var getAllRoomTypesQuery = new GetAllRoomTypesQuery();
        var roomTypes = await roomTypeQueryService.Handle(getAllRoomTypesQuery);
        var resources = roomTypes.Select(RoomTypeResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }
}

using System.Net.Mime;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Commands;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Queries;
using BackendAwSmartstay.API.Accommodations.Domain.Services;
using BackendAwSmartstay.API.Accommodations.Interfaces.REST.Resources;
using BackendAwSmartstay.API.Accommodations.Interfaces.REST.Transform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Accommodations.Infrastructure.Security;
using SmartStay.Contracts.Constants;
using SmartStay.Contracts.Http;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Accommodations.Interfaces.REST;

/// <summary>
/// RESTful API interface controller for Rooms in SmartStay.Accommodations microservice.
/// </summary>
[AccommodationsAuthorize]
[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Room Endpoints")]
public class RoomsController(
    IRoomCommandService roomCommandService,
    IRoomQueryService roomQueryService) : ControllerBase
{
    [HttpGet("search")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Search rooms by district, guests capacity and budget", OperationId = "SearchRooms")]
    [SwaggerResponse(StatusCodes.Status200OK, "Rooms matching the criteria.", typeof(IEnumerable<RoomSearchResult>))]
    public async Task<IActionResult> SearchRooms(
        [FromQuery] string? district = null,
        [FromQuery] int? guests = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null)
    {
        var rooms = await roomQueryService.SearchRoomsAsync(district, guests, minPrice, maxPrice);
        var response = rooms.Select(r => new RoomSearchResult(
            RoomId: r.Id,
            HotelId: r.HotelId,
            HotelName: r.Hotel?.Name ?? "SmartStay Hotel",
            District: r.Hotel?.District ?? r.Hotel?.City ?? "Lima",
            City: r.Hotel?.City ?? "Lima",
            RoomTypeName: r.RoomType?.Name ?? "Standard Room",
            NightlyPrice: r.Price,
            Currency: r.Currency,
            Capacity: r.Capacity,
            IsBookable: r.IsBookable,
            TourPath: r.TourPath,
            Amenities: r.Amenities ?? []
        ));

        return Ok(response);
    }

    [HttpGet("{roomId:int}/tour")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Get 360 degree virtual tour manifest for a room", OperationId = "GetRoomTour")]
    [SwaggerResponse(StatusCodes.Status200OK, "Virtual tour manifest.", typeof(RoomTourManifest))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No virtual tour available for this room.")]
    public async Task<IActionResult> GetRoomTour(int roomId)
    {
        var room = await roomQueryService.Handle(new GetRoomByIdQuery(roomId));
        if (room is null || string.IsNullOrWhiteSpace(room.TourPath))
        {
            return NotFound(new { message = $"No virtual 360 tour available for room {roomId}." });
        }

        var basePath = room.TourPath.TrimEnd('/');
        var manifest = new RoomTourManifest(
            RoomId: room.Id,
            StartNodeId: "entry",
            Nodes: new List<TourNode>
            {
                new TourNode(
                    Id: "entry",
                    Panorama: $"{basePath}/entry.jpg",
                    Name: "Entrada",
                    Links: new List<TourLink>
                    {
                        new TourLink("window", 0.0, 0.0)
                    }
                ),
                new TourNode(
                    Id: "window",
                    Panorama: $"{basePath}/window.jpg",
                    Name: "Ventana y Balcón",
                    Links: new List<TourLink>
                    {
                        new TourLink("entry", Math.PI, 0.0)
                    }
                )
            }
        );

        return Ok(manifest);
    }

    [HttpGet("{roomId:int}")]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get room by its unique identifier", OperationId = "GetRoomById")]
    [SwaggerResponse(StatusCodes.Status200OK, "The room aggregate was located and converted successfully.", typeof(RoomResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "No room aggregate matched the supplied structural identifier.")]
    public async Task<IActionResult> GetRoomById(int roomId)
    {
        var getRoomByIdQuery = new GetRoomByIdQuery(roomId);
        var room = await roomQueryService.Handle(getRoomByIdQuery);
        if (room is null) return NotFound();
        var resource = RoomResourceFromEntityAssembler.ToResourceFromEntity(room);
        return Ok(resource);
    }

    [HttpPost]
    [AccommodationsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Create a new room entry", OperationId = "CreateRoom")]
    [SwaggerResponse(StatusCodes.Status201Created, "The room aggregate root was successfully registered.", typeof(RoomResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The provided construction resource schema layout is invalid.")]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomResource resource)
    {
        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor != null && !actor.CanAccessHotel(resource.HotelId))
        {
            return Forbid();
        }

        var createRoomCommand = CreateRoomCommandFromResourceAssembler.ToCommandFromResource(resource);
        var room = await roomCommandService.Handle(createRoomCommand);
        if (room is null) return BadRequest();
        var roomResource = RoomResourceFromEntityAssembler.ToResourceFromEntity(room);
        return CreatedAtAction(nameof(GetRoomById), new { roomId = room.Id }, roomResource);
    }

    [HttpGet]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get all rooms", OperationId = "GetAllRooms")]
    [SwaggerResponse(StatusCodes.Status200OK, "The overall room inventory was successfully pulled.", typeof(IEnumerable<RoomResource>))]
    public async Task<IActionResult> GetAllRooms()
    {
        var getAllRoomsQuery = new GetAllRoomsQuery();
        var rooms = await roomQueryService.Handle(getAllRoomsQuery);
        var resources = rooms.Select(RoomResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpGet("type/{roomTypeId:int}")]
    [AccommodationsAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get rooms by room type", OperationId = "GetRoomsByRoomTypeId")]
    [SwaggerResponse(StatusCodes.Status200OK, "Rooms list filtered by type.", typeof(IEnumerable<RoomResource>))]
    public async Task<IActionResult> GetRoomsByRoomTypeId(int roomTypeId)
    {
        var getRoomsByRoomTypeIdQuery = new GetRoomsByTypeQuery(roomTypeId);
        var rooms = await roomQueryService.Handle(getRoomsByRoomTypeIdQuery);
        var resources = rooms.Select(RoomResourceFromEntityAssembler.ToResourceFromEntity);
        return Ok(resources);
    }

    [HttpPut("{roomId:int}")]
    [AccommodationsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Update room specifications", OperationId = "UpdateRoom")]
    [SwaggerResponse(StatusCodes.Status200OK, "The target room aggregate state updates were successfully applied.", typeof(RoomResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The target room aggregate could not be found.")]
    public async Task<IActionResult> UpdateRoom(int roomId, [FromBody] UpdateRoomResource resource)
    {
        var existing = await roomQueryService.Handle(new GetRoomByIdQuery(roomId));
        if (existing is null) return NotFound();

        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor != null && !actor.CanAccessHotel(existing.HotelId))
        {
            return Forbid();
        }

        var updateRoomCommand = UpdateRoomCommandFromResourceAssembler.ToCommandFromResource(roomId, resource);
        var room = await roomCommandService.Handle(updateRoomCommand);
        if (room is null) return NotFound();
        var roomResource = RoomResourceFromEntityAssembler.ToResourceFromEntity(room);
        return Ok(roomResource);
    }

    [HttpDelete("{roomId:int}")]
    [AccommodationsAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Delete a room node", OperationId = "DeleteRoom")]
    [SwaggerResponse(StatusCodes.Status200OK, "The target room entry was dropped successfully.", typeof(RoomResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The target room identifier could not be verified.")]
    public async Task<IActionResult> DeleteRoom(int roomId)
    {
        var existing = await roomQueryService.Handle(new GetRoomByIdQuery(roomId));
        if (existing is null) return NotFound();

        var actor = HttpContext.Items["CurrentActor"] as CurrentActor;
        if (actor != null && !actor.CanAccessHotel(existing.HotelId))
        {
            return Forbid();
        }

        var deleteRoomCommand = new DeleteRoomCommand(roomId);
        var room = await roomCommandService.Handle(deleteRoomCommand);
        if (room is null) return NotFound();
        var roomResource = RoomResourceFromEntityAssembler.ToResourceFromEntity(room);
        return Ok(roomResource);
    }
}

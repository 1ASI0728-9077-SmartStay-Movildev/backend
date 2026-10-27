using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using SmartStay.Contracts.Constants;
using SmartStay.Profiles.Domain.Model.Queries;
using SmartStay.Profiles.Domain.Model.ValueObjects;
using SmartStay.Profiles.Domain.Services;
using SmartStay.Profiles.Infrastructure.Security;
using SmartStay.Profiles.Interfaces.REST.Resources;
using SmartStay.Profiles.Interfaces.REST.Transform;
using Swashbuckle.AspNetCore.Annotations;

namespace SmartStay.Profiles.Interfaces.REST;

[ApiController]
[Route("api/v1/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[SwaggerTag("Available Profile Endpoints")]
public class ProfilesController(
    IProfileCommandService profileCommandService,
    IProfileQueryService profileQueryService) : ControllerBase
{
    [HttpGet("{profileId:int}")]
    [ProfilesAuthorize(UserRoles.Admin, UserRoles.ChainAdmin, UserRoles.Guest)]
    [SwaggerOperation(Summary = "Get profile by ID", OperationId = "GetProfileById")]
    [SwaggerResponse(StatusCodes.Status200OK, "Profile found.", typeof(ProfileResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Profile not found.")]
    public async Task<IActionResult> GetProfileById(int profileId)
    {
        var profile = await profileQueryService.Handle(new GetProfileByIdQuery(profileId));
        if (profile is null) return NotFound();
        return Ok(ProfileAssemblers.ToResourceFromEntity(profile));
    }

    [HttpGet("user/{userId:int}")]
    [ProfilesAuthorize(UserRoles.Admin, UserRoles.ChainAdmin, UserRoles.Guest)]
    [SwaggerOperation(Summary = "Get profile by user ID", OperationId = "GetProfileByUserId")]
    [SwaggerResponse(StatusCodes.Status200OK, "Profile found.", typeof(ProfileResource))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Profile not found.")]
    public async Task<IActionResult> GetProfileByUserId(int userId)
    {
        var profile = await profileQueryService.Handle(new GetProfileByUserIdQuery(userId));
        if (profile is null) return NotFound();
        return Ok(ProfileAssemblers.ToResourceFromEntity(profile));
    }

    [HttpPost]
    [ProfilesAuthorize(UserRoles.Guest, UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Create a new identity profile", OperationId = "CreateProfile")]
    [SwaggerResponse(StatusCodes.Status201Created, "Profile created.", typeof(ProfileResource))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid profile data.")]
    public async Task<IActionResult> CreateProfile([FromBody] CreateProfileResource resource)
    {
        var command = ProfileAssemblers.ToCommandFromResource(resource);
        var profile = await profileCommandService.Handle(command);
        if (profile is null) return BadRequest("Could not create profile aggregate.");

        var responseResource = ProfileAssemblers.ToResourceFromEntity(profile);
        return CreatedAtAction(nameof(GetProfileById), new { profileId = profile.Id }, responseResource);
    }

    [HttpGet]
    [ProfilesAuthorize(UserRoles.Admin, UserRoles.ChainAdmin)]
    [SwaggerOperation(Summary = "Get all tracked identity profiles", OperationId = "GetAllProfiles")]
    [SwaggerResponse(StatusCodes.Status200OK, "Profiles returned.", typeof(IEnumerable<ProfileResource>))]
    public async Task<IActionResult> GetAllProfiles()
    {
        var profiles = await profileQueryService.Handle(new GetAllProfilesQuery());
        return Ok(profiles.Select(ProfileAssemblers.ToResourceFromEntity));
    }
}

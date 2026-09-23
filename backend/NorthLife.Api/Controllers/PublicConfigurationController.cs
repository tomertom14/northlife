using Microsoft.AspNetCore.Mvc;

namespace NorthLife.Api.Controllers;

[ApiController]
[Route("api/config/public")]
public sealed class PublicConfigurationController(IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        googleMapsApiKey = configuration["GoogleMaps:ApiKey"] ?? string.Empty,
        googleMapsMapId = configuration["GoogleMaps:MapId"] ?? string.Empty,
    });
}

using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/faculty")]
public class FacultyController : ControllerBase
{
    private readonly IFacultyService _faculty;
    public FacultyController(IFacultyService faculty) => _faculty = faculty;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<FacultyCard>>>> List([FromQuery] bool homeOnly = false)
        => Ok(ApiResponse<List<FacultyCard>>.Ok(await _faculty.ListAsync(homeOnly)));

    [HttpGet("{code}")]
    public async Task<ActionResult<ApiResponse<FacultyProfile>>> Profile(string code)
    {
        var profile = await _faculty.GetByCodeAsync(code);
        return profile == null
            ? NotFound(ApiResponse<FacultyProfile>.Fail("Faculty not found"))
            : Ok(ApiResponse<FacultyProfile>.Ok(profile));
    }
}

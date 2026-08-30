using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

// Public, anonymous franchisee application capture. Approval happens via FranchiseController.
[ApiController]
[Route("api/franchisees")]
public class FranchiseRegistrationController : ControllerBase
{
    private readonly IFranchiseService _svc;
    public FranchiseRegistrationController(IFranchiseService svc) => _svc = svc;

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> Register([FromBody] FranchiseRegistrationRequest request)
    {
        var (ok, error, id) = await _svc.RegisterAsync(request);
        return ok
            ? Ok(ApiResponse<string>.Ok(id!.Value.ToString(), "We've sent a verification code to your contact. Enter it to submit your application for review."))
            : BadRequest(ApiResponse<string>.Fail(error!));
    }

    // Confirms the franchisee's contact OTP. On success the application moves from Unverified to
    // Pending and the admin review queue/notification is triggered.
    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> Verify([FromBody] FranchiseVerifyRequest request)
    {
        var (ok, error) = await _svc.VerifyApplicationAsync(request.Target, request.Code);
        return ok
            ? Ok(ApiResponse<string>.Ok("verified", "Contact verified — your application is now submitted for admin review."))
            : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
